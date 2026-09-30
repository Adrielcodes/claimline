using System.Text.Json;
using Claimline.Contracts;
using OpenAI.Chat;

namespace Claimline.Triage;

/// <summary>
/// Extracts structured facts with an Azure OpenAI deployment using a strict JSON schema, so the model
/// cannot return free-form output. Authenticated with managed identity; there is no API key anywhere.
/// </summary>
public sealed class AzureOpenAIClaimAnalyzer(ChatClient chat) : IClaimAnalyzer
{
    public string Name => "azure-openai";

    private const string SystemPrompt = """
        You are an auto-insurance first-notice-of-loss analyst. Read the policyholder's description and extract facts.
        Only report what the text supports. Do not decide coverage, fault, fraud, or payment.
        damageSeverity: minor (cosmetic, drivable), moderate (panel/part replacement), severe (structural, airbags, towed),
        totalLoss (burned, flooded, stolen and not recovered, or repair clearly exceeds value).
        summary: one neutral sentence an adjuster can scan in a queue.
        """;

    private static readonly BinaryData Schema = BinaryData.FromString("""
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["damageSeverity", "injuriesMentioned", "vehiclesInvolved", "thirdPartyInvolved", "drivable", "damagedAreas", "summary"],
          "properties": {
            "damageSeverity": { "type": "string", "enum": ["minor", "moderate", "severe", "total-loss"] },
            "injuriesMentioned": { "type": "boolean" },
            "vehiclesInvolved": { "type": "integer" },
            "thirdPartyInvolved": { "type": "boolean" },
            "drivable": { "type": "boolean" },
            "damagedAreas": { "type": "array", "items": { "type": "string" } },
            "summary": { "type": "string" }
          }
        }
        """);

    public async Task<ClaimAnalysis> AnalyzeAsync(ClaimSubmitted claim, CancellationToken ct)
    {
        var options = new ChatCompletionOptions
        {
            Temperature = 0,
            MaxOutputTokenCount = 400,
            ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat("claim_analysis", Schema, jsonSchemaIsStrict: true),
        };

        var user = $"""
            Loss type: {claim.LossType}
            Loss location: {claim.LossLocation}
            Policyholder says injuries occurred: {claim.InjuriesReported}
            Description:
            {claim.Description}
            """;

        ChatCompletion completion = await chat.CompleteChatAsync(
            [new SystemChatMessage(SystemPrompt), new UserChatMessage(user)], options, ct);

        var analysis = JsonSerializer.Deserialize<ClaimAnalysis>(completion.Content[0].Text, ClaimlineJson.Options)
            ?? throw new InvalidOperationException("Model returned an empty analysis.");

        return analysis with { VehiclesInvolved = Math.Clamp(analysis.VehiclesInvolved, 1, 10) };
    }
}
