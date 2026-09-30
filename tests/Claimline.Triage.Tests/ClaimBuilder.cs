using Claimline.Contracts;

namespace Claimline.Triage.Tests;

/// <summary>A clean, fully covered, minor collision. Each test changes only what it is about.</summary>
internal static class ClaimBuilder
{
    public static readonly DateTimeOffset Now = new(2026, 9, 15, 14, 0, 0, TimeSpan.Zero);

    public static PolicySnapshot Policy(
        int startedDaysAgo = 200,
        string[]? coverages = null,
        decimal vehicleValue = 24_000,
        int priorClaims = 0) => new(
        "CL-TEST01", "Test Insured", "test@example.com",
        DateOnly.FromDateTime(Now.UtcDateTime).AddDays(-startedDaysAgo),
        DateOnly.FromDateTime(Now.UtcDateTime).AddDays(-startedDaysAgo + 365),
        coverages ?? ["liability", "collision", "comprehensive"],
        CollisionDeductible: 500, vehicleValue, "2023 Toyota Camry", priorClaims);

    public static ClaimSubmitted Claim(
        LossType lossType = LossType.Collision,
        string description = "Backed into a pole in a parking lot and dented the rear bumper. Car drives fine and nobody was hurt.",
        decimal? estimate = 900,
        bool injuries = false,
        bool policeReport = false,
        int lossDaysAgo = 1,
        PolicySnapshot? policy = null) => new(
        Guid.NewGuid(), "CLM-2026-000001", lossType, Now.AddDays(-lossDaysAgo), Now, "Miami, FL",
        description, estimate, injuries, policeReport, policy ?? Policy());

    public static ClaimAnalysis Analysis(
        DamageSeverity severity = DamageSeverity.Minor,
        bool injuries = false,
        int vehicles = 1,
        bool thirdParty = false,
        bool drivable = true) => new(severity, injuries, vehicles, thirdParty, drivable, ["rear bumper"], "Test summary.");
}
