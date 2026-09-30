namespace Claimline.Contracts;

/// <summary>Service Bus topology shared by every service. Must match infra/ and deploy/servicebus-emulator.json.</summary>
public static class Topology
{
    public const string ClaimsTopic = "claims";

    /// <summary>Worker: consumes claim.submitted, runs AI analysis + triage rules.</summary>
    public const string TriageSubscription = "triage";

    /// <summary>API: consumes claim.triaged, updates the claim read model.</summary>
    public const string ProjectionSubscription = "projection";

    /// <summary>Worker: consumes claim.triaged, sends the policyholder an acknowledgement.</summary>
    public const string NotificationsSubscription = "notifications";
}

/// <summary>Message subjects. Subscriptions filter on these, so they are part of the public contract.</summary>
public static class EventTypes
{
    public const string ClaimSubmitted = "claim.submitted";
    public const string ClaimTriaged = "claim.triaged";
}

public sealed record PolicySnapshot(
    string PolicyNumber,
    string InsuredName,
    string InsuredEmail,
    DateOnly EffectiveDate,
    DateOnly ExpirationDate,
    IReadOnlyList<string> Coverages,
    decimal CollisionDeductible,
    decimal VehicleValue,
    string Vehicle,
    int PriorClaims);

/// <summary>First notice of loss, published once the claim is durably stored.</summary>
public sealed record ClaimSubmitted(
    Guid ClaimId,
    string ClaimNumber,
    LossType LossType,
    DateTimeOffset LossDate,
    DateTimeOffset ReportedAt,
    string LossLocation,
    string Description,
    decimal? EstimatedAmount,
    bool InjuriesReported,
    bool PoliceReportFiled,
    PolicySnapshot Policy,
    int Attempt = 1);

public sealed record ClaimTriaged(
    Guid ClaimId,
    string ClaimNumber,
    int Attempt,
    TriageRoute Route,
    int SeverityScore,
    int FraudScore,
    decimal ReserveAmount,
    IReadOnlyList<TriageSignal> Signals,
    ClaimAnalysis Analysis,
    string Analyzer,
    DateTimeOffset TriagedAt);

public sealed record TriageSignal(string Code, SignalCategory Category, int Weight, string Description);

/// <summary>Structured facts extracted from the free-text loss description.</summary>
public sealed record ClaimAnalysis(
    DamageSeverity DamageSeverity,
    bool InjuriesMentioned,
    int VehiclesInvolved,
    bool ThirdPartyInvolved,
    bool Drivable,
    IReadOnlyList<string> DamagedAreas,
    string Summary);

public enum LossType { Collision, Comprehensive, Theft, Glass, Vandalism, Weather }

public enum DamageSeverity { Minor, Moderate, Severe, TotalLoss }

public enum SignalCategory { Coverage, Fraud, Severity }

/// <summary>Which desk the claim lands on.</summary>
public enum TriageRoute { FastTrack, Standard, BodilyInjury, TotalLoss, SpecialInvestigations, CoverageReview }
