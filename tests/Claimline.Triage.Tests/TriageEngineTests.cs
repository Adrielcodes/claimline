using Claimline.Contracts;
using static Claimline.Triage.Tests.ClaimBuilder;

namespace Claimline.Triage.Tests;

public class TriageEngineTests
{
    [Fact]
    public void Clean_minor_collision_is_fast_tracked()
    {
        var result = TriageEngine.Evaluate(Claim(), Analysis());

        Assert.Equal(TriageRoute.FastTrack, result.Route);
        Assert.Equal(0, result.FraudScore);
        Assert.Equal(1_500m, result.ReserveAmount);
    }

    [Fact]
    public void Loss_outside_policy_period_goes_to_coverage_review()
    {
        var lapsed = Policy(startedDaysAgo: 400);

        var result = TriageEngine.Evaluate(Claim(policy: lapsed), Analysis());

        Assert.Equal(TriageRoute.CoverageReview, result.Route);
        Assert.Contains(result.Signals, s => s.Code == "POLICY_NOT_IN_FORCE");
    }

    [Theory]
    [InlineData(LossType.Collision)]
    [InlineData(LossType.Theft)]
    [InlineData(LossType.Glass)]
    public void Liability_only_policy_cannot_cover_first_party_damage(LossType lossType)
    {
        var liabilityOnly = Policy(coverages: ["liability", "uninsured-motorist"]);

        var result = TriageEngine.Evaluate(Claim(lossType, policy: liabilityOnly), Analysis());

        Assert.Equal(TriageRoute.CoverageReview, result.Route);
        Assert.Contains(result.Signals, s => s.Code == "COVERAGE_NOT_PURCHASED");
    }

    [Fact]
    public void Coverage_problems_outrank_fraud()
    {
        var claim = Claim(LossType.Theft, "Car gone.", estimate: 30_000,
            policy: Policy(startedDaysAgo: 5, coverages: ["liability"]));

        var result = TriageEngine.Evaluate(claim, Analysis(DamageSeverity.TotalLoss));

        Assert.Equal(TriageRoute.CoverageReview, result.Route);
    }

    [Fact]
    public void Early_theft_without_police_report_is_referred_to_siu()
    {
        var claim = Claim(LossType.Theft, "Truck was parked overnight and gone in the morning.",
            estimate: 28_000, policy: Policy(startedDaysAgo: 9, vehicleValue: 28_500));

        var result = TriageEngine.Evaluate(claim, Analysis(DamageSeverity.TotalLoss, drivable: false));

        Assert.Equal(TriageRoute.SpecialInvestigations, result.Route);
        Assert.True(result.FraudScore >= TriageEngine.SiuThreshold);
        Assert.Contains(result.Signals, s => s.Code == "EARLY_POLICY_LOSS");
        Assert.Contains(result.Signals, s => s.Code == "THEFT_WITHOUT_POLICE_REPORT");
    }

    [Fact]
    public void Late_reporting_and_claim_frequency_add_up_but_stay_below_siu()
    {
        var claim = Claim(lossDaysAgo: 45, policy: Policy(priorClaims: 3));

        var result = TriageEngine.Evaluate(claim, Analysis());

        Assert.Equal(35, result.FraudScore);
        Assert.Equal(TriageRoute.Standard, result.Route);
    }

    [Fact]
    public void Injuries_route_to_bodily_injury_and_add_injury_reserve()
    {
        var result = TriageEngine.Evaluate(Claim(injuries: true, estimate: null), Analysis(DamageSeverity.Moderate));

        Assert.Equal(TriageRoute.BodilyInjury, result.Route);
        Assert.Equal(4_500m + TriageEngine.InjuryReserve, result.ReserveAmount);
    }

    [Fact]
    public void Injuries_described_in_the_narrative_count_even_if_the_checkbox_was_missed()
    {
        var result = TriageEngine.Evaluate(Claim(injuries: false), Analysis(injuries: true));

        Assert.Equal(TriageRoute.BodilyInjury, result.Route);
    }

    [Fact]
    public void Total_loss_reserve_is_capped_at_vehicle_value()
    {
        var claim = Claim(estimate: 50_000, policeReport: true, policy: Policy(vehicleValue: 9_800));

        var result = TriageEngine.Evaluate(claim, Analysis(DamageSeverity.TotalLoss, drivable: false));

        Assert.Equal(TriageRoute.TotalLoss, result.Route);
        Assert.Equal(9_800m, result.ReserveAmount);
    }

    [Fact]
    public void Severe_damage_on_a_low_value_car_is_treated_as_a_total_loss()
    {
        var claim = Claim(estimate: null, policeReport: true, policy: Policy(vehicleValue: 9_800));

        var result = TriageEngine.Evaluate(claim, Analysis(DamageSeverity.Severe));

        Assert.Equal(TriageRoute.TotalLoss, result.Route);
    }

    [Fact]
    public void Estimate_near_vehicle_value_without_matching_damage_is_flagged()
    {
        var claim = Claim(estimate: 23_000, policeReport: true);

        var result = TriageEngine.Evaluate(claim, Analysis(DamageSeverity.Moderate));

        Assert.Contains(result.Signals, s => s.Code == "ESTIMATE_EXCEEDS_DAMAGE");
    }

    [Fact]
    public void Severity_score_is_capped_at_100()
    {
        var result = TriageEngine.Evaluate(
            Claim(injuries: true),
            Analysis(DamageSeverity.TotalLoss, injuries: true, vehicles: 4, thirdParty: true, drivable: false));

        Assert.Equal(100, result.SeverityScore);
    }

    [Fact]
    public void Evaluation_is_deterministic()
    {
        var claim = Claim(LossType.Theft, "Stolen from driveway.", estimate: 20_000, policy: Policy(startedDaysAgo: 3));
        var analysis = Analysis(DamageSeverity.TotalLoss);

        Assert.Equivalent(TriageEngine.Evaluate(claim, analysis), TriageEngine.Evaluate(claim, analysis));
    }
}
