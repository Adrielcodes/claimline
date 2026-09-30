using Claimline.Contracts;

namespace Claimline.Api.Data;

public sealed class Policy
{
    public int Id { get; set; }
    public required string PolicyNumber { get; set; }
    public required string InsuredFirstName { get; set; }
    public required string InsuredLastName { get; set; }
    public required string InsuredEmail { get; set; }
    public required string State { get; set; }
    public DateOnly EffectiveDate { get; set; }
    public DateOnly ExpirationDate { get; set; }
    public List<string> Coverages { get; set; } = [];
    public decimal CollisionDeductible { get; set; }
    public required string Vehicle { get; set; }
    public decimal VehicleValue { get; set; }
    public int PriorClaims { get; set; }

    /// <summary>Shown on the public demo form so visitors can try each scenario.</summary>
    public string? DemoScenario { get; set; }

    public PolicySnapshot ToSnapshot() => new(
        PolicyNumber, $"{InsuredFirstName} {InsuredLastName}", InsuredEmail, EffectiveDate, ExpirationDate,
        Coverages, CollisionDeductible, VehicleValue, Vehicle, PriorClaims);
}

public enum ClaimStatus { Submitted, Triaged }

public sealed class Claim
{
    public Guid Id { get; set; }
    public required string ClaimNumber { get; set; }
    public int PolicyId { get; set; }
    public Policy Policy { get; set; } = null!;

    public LossType LossType { get; set; }
    public DateTimeOffset LossDate { get; set; }
    public DateTimeOffset ReportedAt { get; set; }
    public required string LossLocation { get; set; }
    public required string Description { get; set; }
    public decimal? EstimatedAmount { get; set; }
    public bool InjuriesReported { get; set; }
    public bool PoliceReportFiled { get; set; }
    public string? ContactPhone { get; set; }
    public string? IdempotencyKey { get; set; }

    public ClaimStatus Status { get; set; }

    /// <summary>Bumped on every (re)triage request so a late result from an older attempt can be ignored.</summary>
    public int TriageAttempt { get; set; } = 1;
    public DateTimeOffset LastSubmittedAt { get; set; }

    public TriageRoute? Route { get; set; }
    public int? SeverityScore { get; set; }
    public int? FraudScore { get; set; }
    public decimal? ReserveAmount { get; set; }
    public string? Analyzer { get; set; }
    public ClaimAnalysis? Analysis { get; set; }
    public List<TriageSignal> Signals { get; set; } = [];
    public DateTimeOffset? TriagedAt { get; set; }

    public List<ClaimEvent> Timeline { get; set; } = [];

    public byte[] RowVersion { get; set; } = [];

    public ClaimSubmitted ToSubmittedEvent() => new(
        Id, ClaimNumber, LossType, LossDate, ReportedAt, LossLocation, Description,
        EstimatedAmount, InjuriesReported, PoliceReportFiled, Policy.ToSnapshot(), TriageAttempt);
}

/// <summary>Human-readable audit trail shown on the claim page.</summary>
public sealed class ClaimEvent
{
    public long Id { get; set; }
    public Guid ClaimId { get; set; }
    public DateTimeOffset At { get; set; }
    public required string Type { get; set; }
    public required string Title { get; set; }
    public string? Detail { get; set; }
}

/// <summary>Transactional outbox row: written in the same transaction as the state change it announces.</summary>
public sealed class OutboxMessage
{
    public Guid Id { get; set; }
    public required string EventType { get; set; }
    public required string CorrelationId { get; set; }
    public required string Payload { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public int Attempts { get; set; }
    public string? LastError { get; set; }
}

/// <summary>Inbox for idempotent consumers: (consumer, message id) is recorded with the side effects it caused.</summary>
public sealed class ProcessedMessage
{
    public required string Consumer { get; set; }
    public required string MessageId { get; set; }
    public DateTimeOffset ProcessedAt { get; set; }
}
