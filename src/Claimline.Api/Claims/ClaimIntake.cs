using System.Text.Json;
using Claimline.Api.Data;
using Claimline.Contracts;
using Claimline.ServiceDefaults;
using Microsoft.EntityFrameworkCore;

namespace Claimline.Api.Claims;

public sealed record SubmitClaimRequest(
    string PolicyNumber,
    string LastName,
    LossType LossType,
    DateTimeOffset LossDate,
    string LossLocation,
    string Description,
    decimal? EstimatedAmount,
    bool InjuriesReported,
    bool PoliceReportFiled,
    string? ContactPhone);

public abstract record IntakeResult
{
    public sealed record Accepted(Claim Claim, bool Replayed) : IntakeResult;
    public sealed record Invalid(Dictionary<string, string[]> Errors) : IntakeResult;
}

/// <summary>
/// FNOL intake. The claim row, its first timeline entry and the claim.submitted outbox message are saved in
/// one transaction: either the claim exists and its event will be published, or neither happened.
/// </summary>
public sealed class ClaimIntake(ClaimlineDb db, TimeProvider clock)
{
    public async Task<IntakeResult> SubmitAsync(SubmitClaimRequest request, string? idempotencyKey, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            var existing = await db.Claims.Include(c => c.Policy)
                .FirstOrDefaultAsync(c => c.IdempotencyKey == idempotencyKey, ct);
            if (existing is not null)
                return new IntakeResult.Accepted(existing, Replayed: true);
        }

        var now = clock.GetUtcNow();
        var errors = Validate(request, now);
        if (errors.Count > 0)
            return new IntakeResult.Invalid(errors);

        var policyNumber = request.PolicyNumber.Trim().ToUpperInvariant();
        var lastName = request.LastName.Trim();
        var policy = await db.Policies.FirstOrDefaultAsync(p => p.PolicyNumber == policyNumber, ct);

        // Same message for "no such policy" and "wrong name" so the form can't be used to enumerate policies.
        if (policy is null || !string.Equals(policy.InsuredLastName, lastName, StringComparison.OrdinalIgnoreCase))
            return new IntakeResult.Invalid(new() { ["policyNumber"] = ["We couldn't find a policy with that number and last name."] });

        var claim = new Claim
        {
            Id = Guid.CreateVersion7(now),
            ClaimNumber = await db.NextClaimNumberAsync(now, ct),
            Policy = policy,
            LossType = request.LossType,
            LossDate = request.LossDate.ToUniversalTime(),
            ReportedAt = now,
            LastSubmittedAt = now,
            LossLocation = request.LossLocation.Trim(),
            Description = request.Description.Trim(),
            EstimatedAmount = request.EstimatedAmount,
            InjuriesReported = request.InjuriesReported,
            PoliceReportFiled = request.PoliceReportFiled,
            ContactPhone = request.ContactPhone?.Trim(),
            IdempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey,
            Status = ClaimStatus.Submitted,
        };

        db.Claims.Add(claim);
        claim.Timeline.Add(new ClaimEvent
        {
            At = now,
            Type = "fnol-received",
            Title = "First notice of loss received",
            Detail = $"{claim.LossType} loss reported on policy {policy.PolicyNumber}.",
        });
        EnqueueSubmitted(claim, now);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException) when (claim.IdempotencyKey is not null)
        {
            // Two concurrent requests with the same key: the unique index let exactly one win.
            db.ChangeTracker.Clear();
            var winner = await db.Claims.Include(c => c.Policy).FirstAsync(c => c.IdempotencyKey == claim.IdempotencyKey, ct);
            return new IntakeResult.Accepted(winner, Replayed: true);
        }

        ClaimlineTelemetry.ClaimsSubmitted.Add(1, new KeyValuePair<string, object?>("loss_type", claim.LossType.ToString()));
        return new IntakeResult.Accepted(claim, Replayed: false);
    }

    /// <summary>Re-runs triage (e.g. after a rules change). Results from older attempts are ignored when they arrive.</summary>
    public async Task<Claim?> RetriageAsync(string claimNumber, CancellationToken ct)
    {
        var claim = await db.Claims.Include(c => c.Policy).FirstOrDefaultAsync(c => c.ClaimNumber == claimNumber, ct);
        if (claim is null) return null;

        var now = clock.GetUtcNow();
        claim.TriageAttempt++;
        claim.Status = ClaimStatus.Submitted;
        claim.LastSubmittedAt = now;
        db.ClaimEvents.Add(new ClaimEvent
        {
            ClaimId = claim.Id,
            At = now,
            Type = "retriage-requested",
            Title = $"Re-triage requested (attempt {claim.TriageAttempt})",
        });
        EnqueueSubmitted(claim, now);

        await db.SaveChangesAsync(ct);
        return claim;
    }

    private void EnqueueSubmitted(Claim claim, DateTimeOffset now) =>
        db.Outbox.Add(new OutboxMessage
        {
            Id = Guid.CreateVersion7(now),
            EventType = EventTypes.ClaimSubmitted,
            CorrelationId = claim.Id.ToString(),
            Payload = JsonSerializer.Serialize(claim.ToSubmittedEvent(), ClaimlineJson.Options),
            CreatedAt = now,
        });

    private static Dictionary<string, string[]> Validate(SubmitClaimRequest r, DateTimeOffset now)
    {
        var errors = new Dictionary<string, string[]>();
        void Add(string field, string message) => errors[field] = [message];

        if (string.IsNullOrWhiteSpace(r.PolicyNumber) || r.PolicyNumber.Length > 20) Add("policyNumber", "Enter your policy number.");
        if (string.IsNullOrWhiteSpace(r.LastName) || r.LastName.Length > 60) Add("lastName", "Enter the policyholder's last name.");
        if (!Enum.IsDefined(r.LossType)) Add("lossType", "Choose what happened.");
        if (r.LossDate > now.AddMinutes(5)) Add("lossDate", "The date of loss can't be in the future.");
        if (r.LossDate < now.AddYears(-2)) Add("lossDate", "Losses older than two years must be reported by phone.");
        if (string.IsNullOrWhiteSpace(r.LossLocation) || r.LossLocation.Length > 200) Add("lossLocation", "Tell us where it happened.");
        if (string.IsNullOrWhiteSpace(r.Description) || r.Description.Trim().Length < 20) Add("description", "Describe what happened in at least 20 characters.");
        else if (r.Description.Length > 4000) Add("description", "Keep the description under 4,000 characters.");
        if (r.EstimatedAmount is < 0 or > 1_000_000) Add("estimatedAmount", "Enter an estimate between $0 and $1,000,000.");
        if (r.ContactPhone is { Length: > 30 }) Add("contactPhone", "Phone number is too long.");

        return errors;
    }
}
