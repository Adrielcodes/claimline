using System.Text.RegularExpressions;
using Claimline.Contracts;
using Microsoft.Extensions.Logging;

namespace Claimline.Triage;

/// <summary>Turns a policyholder's free-text description into structured facts. Never makes routing decisions.</summary>
public interface IClaimAnalyzer
{
    string Name { get; }
    Task<ClaimAnalysis> AnalyzeAsync(ClaimSubmitted claim, CancellationToken ct);
}

public sealed record AnalyzerOutcome(ClaimAnalysis Analysis, string Analyzer);

/// <summary>Keyword-based analyzer. Runs with no cloud dependencies and is the fallback when the LLM is unavailable.</summary>
public sealed partial class HeuristicClaimAnalyzer : IClaimAnalyzer
{
    public string Name => "heuristic";

    // A trailing * means "prefix"; everything else must match as a whole word or phrase ("er" must not match "driver").
    private static readonly string[] TotalLossTerms = ["totaled", "total loss", "burned", "caught fire", "flooded", "submerged", "rolled over", "frame damage", "underwater"];
    private static readonly string[] SevereTerms = ["airbag*", "towed", "tow truck", "crushed", "axle", "engine", "t-boned", "head-on", "highway", "freeway", "rollover"];
    private static readonly string[] MinorTerms = ["scratch*", "scuff*", "dent*", "chip*", "crack*", "mirror", "parking lot", "bumper", "minor", "small", "keyed"];
    private static readonly string[] InjuryTerms = ["injur*", "hurt", "hospital*", "ambulance", "whiplash", "bleeding", "pain", "emergency room", "paramedic*", "concussion"];
    private static readonly string[] ThirdPartyTerms = ["other driver", "other car", "other vehicle", "rear-ended", "rear ended", "hit me", "hit my", "ran a red", "pedestrian", "cyclist", "t-boned", "sideswiped"];
    private static readonly string[] NotDrivableTerms = ["towed", "tow truck", "not drivable", "undrivable", "won't start", "wont start", "stranded", "totaled", "stolen"];
    private static readonly string[] Areas = ["front bumper", "rear bumper", "bumper", "hood", "trunk", "windshield", "door", "fender", "quarter panel", "headlight", "taillight", "mirror", "roof", "wheel", "tire", "side"];

    public Task<ClaimAnalysis> AnalyzeAsync(ClaimSubmitted claim, CancellationToken ct)
    {
        var text = " " + claim.Description.ToLowerInvariant() + " ";

        var severity = claim.LossType switch
        {
            LossType.Glass => DamageSeverity.Minor,
            LossType.Theft when ContainsAny(text, "recovered") => DamageSeverity.Moderate,
            LossType.Theft => DamageSeverity.TotalLoss,
            _ when ContainsAny(text, TotalLossTerms) => DamageSeverity.TotalLoss,
            _ when ContainsAny(text, SevereTerms) => DamageSeverity.Severe,
            _ when ContainsAny(text, MinorTerms) => DamageSeverity.Minor,
            _ => DamageSeverity.Moderate,
        };

        var injuries = ContainsAny(text, InjuryTerms) && !ContainsAny(text, "no one was hurt", "nobody was hurt", "no injuries", "not injured", "no one injured");
        var thirdParty = claim.LossType == LossType.Collision && ContainsAny(text, ThirdPartyTerms);
        var vehicles = CountVehicles(text, thirdParty);
        var damagedAreas = Areas.Where(a => ContainsTerm(text, a)).Take(4).ToList();

        var analysis = new ClaimAnalysis(
            severity,
            injuries,
            vehicles,
            thirdParty,
            Drivable: !ContainsAny(text, NotDrivableTerms) && severity != DamageSeverity.TotalLoss,
            damagedAreas,
            Summary: Summarize(claim, severity, thirdParty));

        return Task.FromResult(analysis);
    }

    private static int CountVehicles(string text, bool thirdParty)
    {
        var match = VehicleCount().Match(text);
        if (match.Success)
        {
            var word = match.Groups[1].Value;
            return word switch { "two" => 2, "three" => 3, "four" => 4, "five" => 5, _ => int.TryParse(word, out var n) ? n : 2 };
        }
        return thirdParty ? 2 : 1;
    }

    private static string Summarize(ClaimSubmitted claim, DamageSeverity severity, bool thirdParty)
    {
        var firstSentence = claim.Description.Split(['.', '!', '\n'], 2)[0].Trim();
        if (firstSentence.Length > 140) firstSentence = firstSentence[..137] + "...";
        var who = thirdParty ? " involving another party" : "";
        var label = severity == DamageSeverity.TotalLoss ? "Probable total-loss" : severity.ToString();
        return $"{label} {claim.LossType.ToString().ToLowerInvariant()} loss{who}: {firstSentence}.";
    }

    private static bool ContainsAny(string text, params string[] terms) => terms.Any(term => ContainsTerm(text, term));

    private static bool ContainsTerm(string text, string term)
    {
        var prefix = term.EndsWith('*');
        var pattern = @"\b" + Regex.Escape(prefix ? term[..^1] : term) + (prefix ? "" : @"\b");
        return Regex.IsMatch(text, pattern);
    }

    [GeneratedRegex(@"\b(two|three|four|five|\d)[ -](?:car|vehicle)s?\b")]
    private static partial Regex VehicleCount();
}

/// <summary>
/// Tries the primary (LLM) analyzer with a timeout and falls back to the heuristic one on any failure,
/// so an AI outage slows triage quality down instead of stopping claims from moving.
/// </summary>
public sealed class ResilientClaimAnalyzer(
    IClaimAnalyzer? primary,
    HeuristicClaimAnalyzer fallback,
    ILogger<ResilientClaimAnalyzer> logger,
    TimeSpan? timeout = null)
{
    private readonly TimeSpan _timeout = timeout ?? TimeSpan.FromSeconds(20);

    public async Task<AnalyzerOutcome> AnalyzeAsync(ClaimSubmitted claim, CancellationToken ct)
    {
        if (primary is not null)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(_timeout);
            try
            {
                return new(await primary.AnalyzeAsync(claim, cts.Token), primary.Name);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Analyzer {Analyzer} failed for claim {ClaimNumber}; falling back to {Fallback}",
                    primary.Name, claim.ClaimNumber, fallback.Name);
            }
        }

        return new(await fallback.AnalyzeAsync(claim, ct), fallback.Name);
    }
}
