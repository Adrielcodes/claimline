using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Claimline.Contracts;
using Claimline.ServiceDefaults;
using Claimline.Triage;

namespace Claimline.Worker;

/// <summary>
/// claim.submitted → analysis → rules → claim.triaged. Stateless: the outgoing MessageId is derived from the
/// incoming one, so a redelivered submission produces the same claim.triaged id and Service Bus duplicate
/// detection (plus the API's inbox) drops the repeat.
/// </summary>
public sealed class TriageHandler(ResilientClaimAnalyzer analyzer, IEventPublisher publisher, TimeProvider clock, ILogger<TriageHandler> logger)
{
    public async Task<ClaimTriaged> HandleAsync(ClaimSubmitted claim, MessageContext context, CancellationToken ct)
    {
        using var activity = ClaimlineTelemetry.Source.StartActivity("triage claim");
        activity?.SetTag("claimline.claim_number", claim.ClaimNumber);
        activity?.SetTag("claimline.attempt", claim.Attempt);

        var outcome = await analyzer.AnalyzeAsync(claim, ct);
        var result = TriageEngine.Evaluate(claim, outcome.Analysis);

        var triaged = new ClaimTriaged(
            claim.ClaimId, claim.ClaimNumber, claim.Attempt, result.Route, result.SeverityScore, result.FraudScore,
            result.ReserveAmount, result.Signals, outcome.Analysis, outcome.Analyzer, clock.GetUtcNow());

        activity?.SetTag("claimline.route", result.Route.ToString());
        activity?.SetTag("claimline.analyzer", outcome.Analyzer);
        ClaimlineTelemetry.ClaimsTriaged.Add(1,
            new KeyValuePair<string, object?>("route", result.Route.ToString()),
            new KeyValuePair<string, object?>("analyzer", outcome.Analyzer));

        await publisher.PublishAsync(
            EventTypes.ClaimTriaged,
            messageId: $"{context.MessageId}:triaged",
            correlationId: claim.ClaimId.ToString(),
            JsonSerializer.Serialize(triaged, ClaimlineJson.Options),
            ct);

        logger.LogInformation("Claim {ClaimNumber} routed to {Route} (severity {Severity}, fraud {Fraud}, analyzer {Analyzer})",
            claim.ClaimNumber, result.Route, result.SeverityScore, result.FraudScore, outcome.Analyzer);
        return triaged;
    }
}

public sealed class TriageConsumer(ServiceBusClient client, IServiceScopeFactory scopes, ILogger<TriageConsumer> logger)
    : ServiceBusConsumer<ClaimSubmitted>(client, scopes, logger, Topology.TriageSubscription, maxConcurrentCalls: 8)
{
    protected override Task HandleAsync(ClaimSubmitted message, MessageContext context, IServiceProvider services, CancellationToken ct) =>
        services.GetRequiredService<TriageHandler>().HandleAsync(message, context, ct);
}

/// <summary>
/// A second, independent subscriber to claim.triaged: the fan-out means adding a consumer never touches the API.
/// In production this would call Azure Communication Services; here it logs the message it would send.
/// </summary>
public sealed class NotificationConsumer(ServiceBusClient client, IServiceScopeFactory scopes, ILogger<NotificationConsumer> logger)
    : ServiceBusConsumer<ClaimTriaged>(client, scopes, logger, Topology.NotificationsSubscription)
{
    protected override Task HandleAsync(ClaimTriaged message, MessageContext context, IServiceProvider services, CancellationToken ct)
    {
        var nextStep = message.Route switch
        {
            TriageRoute.FastTrack => "An adjuster will contact you within 1 business day with a repair estimate.",
            TriageRoute.CoverageReview => "We're reviewing your policy details and will contact you within 2 business days.",
            TriageRoute.BodilyInjury => "An injury specialist will call you today.",
            TriageRoute.TotalLoss => "A total-loss specialist will contact you about your vehicle's valuation.",
            _ => "An adjuster will contact you within 2 business days.",
        };

        logger.LogInformation("Notification for {ClaimNumber}: \"We received claim {ClaimNumber}. {NextStep}\"",
            message.ClaimNumber, message.ClaimNumber, nextStep);
        return Task.CompletedTask;
    }
}
