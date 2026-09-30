using Claimline.Contracts;

namespace Claimline.Triage;

public sealed record TriageResult(
    TriageRoute Route,
    int SeverityScore,
    int FraudScore,
    decimal ReserveAmount,
    IReadOnlyList<TriageSignal> Signals);

/// <summary>
/// Deterministic, side-effect-free triage rules. The AI analyzer only extracts facts from the narrative;
/// every decision that routes a claim or sets a reserve is made here, where it can be unit tested and audited.
/// </summary>
public static class TriageEngine
{
    public const int SiuThreshold = 50;
    public const decimal FastTrackCeiling = 2_500m;
    public const decimal InjuryReserve = 15_000m;

    public static TriageResult Evaluate(ClaimSubmitted claim, ClaimAnalysis analysis)
    {
        var signals = new List<TriageSignal>();
        signals.AddRange(CoverageSignals(claim));
        signals.AddRange(FraudSignals(claim, analysis));
        signals.AddRange(SeveritySignals(claim, analysis));

        var fraudScore = Math.Min(100, signals.Where(s => s.Category == SignalCategory.Fraud).Sum(s => s.Weight));
        var severityScore = Math.Min(100, signals.Where(s => s.Category == SignalCategory.Severity).Sum(s => s.Weight));
        var reserve = Reserve(claim, analysis);
        var route = Route(claim, analysis, signals, fraudScore, severityScore, reserve);

        return new TriageResult(route, severityScore, fraudScore, reserve, signals);
    }

    public static string RequiredCoverage(LossType lossType) =>
        lossType == LossType.Collision ? "collision" : "comprehensive";

    private static IEnumerable<TriageSignal> CoverageSignals(ClaimSubmitted claim)
    {
        var policy = claim.Policy;
        var lossDay = DateOnly.FromDateTime(claim.LossDate.UtcDateTime);

        if (lossDay < policy.EffectiveDate || lossDay > policy.ExpirationDate)
            yield return new("POLICY_NOT_IN_FORCE", SignalCategory.Coverage, 100,
                $"Loss date {lossDay:yyyy-MM-dd} is outside the policy period ({policy.EffectiveDate:yyyy-MM-dd} to {policy.ExpirationDate:yyyy-MM-dd}).");

        var required = RequiredCoverage(claim.LossType);
        if (!policy.Coverages.Contains(required, StringComparer.OrdinalIgnoreCase))
            yield return new("COVERAGE_NOT_PURCHASED", SignalCategory.Coverage, 100,
                $"A {claim.LossType.ToString().ToLowerInvariant()} loss needs {required} coverage, which this policy does not carry.");
    }

    private static IEnumerable<TriageSignal> FraudSignals(ClaimSubmitted claim, ClaimAnalysis analysis)
    {
        var policy = claim.Policy;
        var lossDay = DateOnly.FromDateTime(claim.LossDate.UtcDateTime);
        var daysIntoPolicy = lossDay.DayNumber - policy.EffectiveDate.DayNumber;
        var reportLagDays = (claim.ReportedAt - claim.LossDate).TotalDays;

        if (daysIntoPolicy is >= 0 and <= 30)
            yield return new("EARLY_POLICY_LOSS", SignalCategory.Fraud, 25,
                $"Loss occurred {daysIntoPolicy} day(s) after the policy started.");

        if (reportLagDays > 30)
            yield return new("LATE_REPORTING", SignalCategory.Fraud, 20,
                $"Reported {(int)reportLagDays} days after the loss.");

        if (policy.PriorClaims >= 2)
            yield return new("CLAIM_FREQUENCY", SignalCategory.Fraud, 15,
                $"{policy.PriorClaims} prior claims on this policy.");

        if (claim.LossType == LossType.Theft && !claim.PoliceReportFiled)
            yield return new("THEFT_WITHOUT_POLICE_REPORT", SignalCategory.Fraud, 25,
                "Theft reported without a police report.");
        else if (!claim.PoliceReportFiled && claim.EstimatedAmount > 10_000m)
            yield return new("LARGE_LOSS_NO_POLICE_REPORT", SignalCategory.Fraud, 10,
                "Estimate above $10,000 with no police report.");

        if (claim.EstimatedAmount is { } estimate && policy.VehicleValue > 0 && estimate > policy.VehicleValue * 0.9m
            && analysis.DamageSeverity != DamageSeverity.TotalLoss)
            yield return new("ESTIMATE_EXCEEDS_DAMAGE", SignalCategory.Fraud, 15,
                "Estimate is near the vehicle's full value, but the description does not read as a total loss.");

        if (claim.Description.Trim().Length < 60)
            yield return new("THIN_NARRATIVE", SignalCategory.Fraud, 5,
                "Very little detail in the loss description.");
    }

    private static IEnumerable<TriageSignal> SeveritySignals(ClaimSubmitted claim, ClaimAnalysis analysis)
    {
        var (weight, label) = analysis.DamageSeverity switch
        {
            DamageSeverity.Minor => (10, "Minor damage"),
            DamageSeverity.Moderate => (35, "Moderate damage"),
            DamageSeverity.Severe => (60, "Severe damage"),
            _ => (85, "Damage consistent with a total loss"),
        };
        yield return new("DAMAGE_" + analysis.DamageSeverity.ToString().ToUpperInvariant(), SignalCategory.Severity, weight, label + ".");

        if (HasInjuries(claim, analysis))
            yield return new("INJURIES", SignalCategory.Severity, 25, "Injuries reported or described.");

        if (analysis.ThirdPartyInvolved)
            yield return new("THIRD_PARTY", SignalCategory.Severity, 10, "Another party is involved (possible liability exposure).");

        if (analysis.VehiclesInvolved > 2)
            yield return new("MULTI_VEHICLE", SignalCategory.Severity, 10, $"{analysis.VehiclesInvolved} vehicles involved.");

        if (!analysis.Drivable)
            yield return new("NOT_DRIVABLE", SignalCategory.Severity, 10, "Vehicle is not drivable (tow and storage costs).");
    }

    private static TriageRoute Route(ClaimSubmitted claim, ClaimAnalysis analysis, List<TriageSignal> signals,
        int fraudScore, int severityScore, decimal reserve)
    {
        if (signals.Any(s => s.Category == SignalCategory.Coverage))
            return TriageRoute.CoverageReview;

        if (fraudScore >= SiuThreshold)
            return TriageRoute.SpecialInvestigations;

        if (HasInjuries(claim, analysis))
            return TriageRoute.BodilyInjury;

        var vehicleValue = claim.Policy.VehicleValue;
        if (analysis.DamageSeverity == DamageSeverity.TotalLoss || (vehicleValue > 0 && reserve >= vehicleValue * 0.75m))
            return TriageRoute.TotalLoss;

        if (severityScore < 30 && fraudScore < 20 && reserve <= FastTrackCeiling)
            return TriageRoute.FastTrack;

        return TriageRoute.Standard;
    }

    /// <summary>Initial case reserve: the larger of the claimant's estimate and a severity-based floor (capped at vehicle value), plus an injury reserve.</summary>
    private static decimal Reserve(ClaimSubmitted claim, ClaimAnalysis analysis)
    {
        var vehicleValue = claim.Policy.VehicleValue;
        var floor = analysis.DamageSeverity switch
        {
            DamageSeverity.Minor => 1_500m,
            DamageSeverity.Moderate => 4_500m,
            DamageSeverity.Severe => 12_000m,
            _ => vehicleValue,
        };

        var property = Math.Max(floor, claim.EstimatedAmount ?? 0m);
        if (vehicleValue > 0)
            property = Math.Min(property, vehicleValue);

        var injury = HasInjuries(claim, analysis) ? InjuryReserve : 0m;
        return decimal.Round(property + injury, 2);
    }

    private static bool HasInjuries(ClaimSubmitted claim, ClaimAnalysis analysis) =>
        claim.InjuriesReported || analysis.InjuriesMentioned;
}
