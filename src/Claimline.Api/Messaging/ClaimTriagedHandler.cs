using Azure.Messaging.ServiceBus;
using Claimline.Api.Data;
using Claimline.Contracts;
using Claimline.ServiceDefaults;
using Microsoft.EntityFrameworkCore;

namespace Claimline.Api.Messaging;

public enum ProjectionOutcome { Applied, Duplicate, Stale }

/// <summary>
/// Applies a triage decision to the claim read model. The inbox row and the claim update commit together,
/// so a redelivered message is detected and skipped rather than applied twice.
/// </summary>
public sealed class ClaimTriagedHandler(ClaimlineDb db, TimeProvider clock, ILogger<ClaimTriagedHandler> logger)
{
    public const string ConsumerName = "api.projection";

    public async Task<ProjectionOutcome> HandleAsync(ClaimTriaged evt, MessageContext context, CancellationToken ct)
    {
        if (await db.ProcessedMessages.AnyAsync(p => p.Consumer == ConsumerName && p.MessageId == context.MessageId, ct))
        {
            logger.LogInformation("Skipping duplicate {MessageId}", context.MessageId);
            return ProjectionOutcome.Duplicate;
        }

        var claim = await db.Claims.FirstOrDefaultAsync(c => c.Id == evt.ClaimId, ct)
            ?? throw new PermanentMessageFailure("ClaimNotFound", $"No claim with id {evt.ClaimId}.");

        var now = clock.GetUtcNow();
        db.ProcessedMessages.Add(new ProcessedMessage { Consumer = ConsumerName, MessageId = context.MessageId, ProcessedAt = now });

        ProjectionOutcome outcome;
        if (evt.Attempt < claim.TriageAttempt)
        {
            db.ClaimEvents.Add(new ClaimEvent
            {
                ClaimId = claim.Id,
                At = now,
                Type = "stale-result-ignored",
                Title = $"Ignored triage result from attempt {evt.Attempt}",
                Detail = $"Attempt {claim.TriageAttempt} is the current one.",
            });
            outcome = ProjectionOutcome.Stale;
        }
        else
        {
            var latency = evt.TriagedAt - claim.LastSubmittedAt;
            claim.Status = ClaimStatus.Triaged;
            claim.Route = evt.Route;
            claim.SeverityScore = evt.SeverityScore;
            claim.FraudScore = evt.FraudScore;
            claim.ReserveAmount = evt.ReserveAmount;
            claim.Analysis = evt.Analysis;
            claim.Signals = [.. evt.Signals];
            claim.Analyzer = evt.Analyzer;
            claim.TriagedAt = evt.TriagedAt;

            db.ClaimEvents.Add(new ClaimEvent
            {
                ClaimId = claim.Id,
                At = now,
                Type = "triaged",
                Title = $"Routed to {RouteNames.Display(evt.Route)}",
                Detail = $"Severity {evt.SeverityScore}, fraud {evt.FraudScore}, reserve ${evt.ReserveAmount:N0}. " +
                         $"Analyzed by {evt.Analyzer}; {latency.TotalMilliseconds:N0} ms after submission.",
            });

            ClaimlineTelemetry.TriageLatency.Record(latency.TotalMilliseconds,
                new KeyValuePair<string, object?>("analyzer", evt.Analyzer));
            outcome = ProjectionOutcome.Applied;
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsDuplicateInboxRow(ex))
        {
            // A concurrent redelivery committed first.
            return ProjectionOutcome.Duplicate;
        }

        return outcome;
    }

    private static bool IsDuplicateInboxRow(DbUpdateException ex) =>
        ex.InnerException is Microsoft.Data.SqlClient.SqlException { Number: 2627 or 2601 }
        && ex.Entries.Any(e => e.Entity is ProcessedMessage);
}

public sealed class ClaimTriagedConsumer(ServiceBusClient client, IServiceScopeFactory scopes, ILogger<ClaimTriagedConsumer> logger)
    : ServiceBusConsumer<ClaimTriaged>(client, scopes, logger, Topology.ProjectionSubscription)
{
    protected override Task HandleAsync(ClaimTriaged message, MessageContext context, IServiceProvider services, CancellationToken ct) =>
        services.GetRequiredService<ClaimTriagedHandler>().HandleAsync(message, context, ct);
}

public static class RouteNames
{
    public static string Display(TriageRoute route) => route switch
    {
        TriageRoute.FastTrack => "Fast Track",
        TriageRoute.Standard => "Standard Adjusting",
        TriageRoute.BodilyInjury => "Bodily Injury",
        TriageRoute.TotalLoss => "Total Loss",
        TriageRoute.SpecialInvestigations => "Special Investigations (SIU)",
        TriageRoute.CoverageReview => "Coverage Review",
        _ => route.ToString(),
    };
}
