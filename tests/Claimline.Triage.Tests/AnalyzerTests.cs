using Claimline.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using static Claimline.Triage.Tests.ClaimBuilder;

namespace Claimline.Triage.Tests;

public class HeuristicClaimAnalyzerTests
{
    private readonly HeuristicClaimAnalyzer _analyzer = new();

    private Task<ClaimAnalysis> Analyze(string description, LossType lossType = LossType.Collision) =>
        _analyzer.AnalyzeAsync(Claim(lossType, description), TestContext.Current.CancellationToken);

    [Fact]
    public async Task Words_containing_injury_terms_do_not_count_as_injuries()
    {
        // Regression: "er " (emergency room) used to match "driver side", and "pain" matched "paint".
        var analysis = await Analyze("Someone keyed the driver side doors and scratched the paint.", LossType.Vandalism);

        Assert.False(analysis.InjuriesMentioned);
    }

    [Theory]
    [InlineData("The other driver ran a red light. I was taken to the hospital with a concussion.")]
    [InlineData("My passenger was injured and complained of neck pain.")]
    public async Task Detects_injuries(string description)
    {
        Assert.True((await Analyze(description)).InjuriesMentioned);
    }

    [Fact]
    public async Task Explicit_no_injury_statement_wins()
    {
        var analysis = await Analyze("Minor fender bender with the other driver, no one was hurt.");

        Assert.False(analysis.InjuriesMentioned);
        Assert.True(analysis.ThirdPartyInvolved);
    }

    [Theory]
    [InlineData("Small dent and scratches on the rear bumper.", DamageSeverity.Minor)]
    [InlineData("Front end damage, headlight and hood need replacing.", DamageSeverity.Moderate)]
    [InlineData("I was t-boned, airbags deployed and the car was towed.", DamageSeverity.Severe)]
    [InlineData("The car caught fire after the crash and is totaled.", DamageSeverity.TotalLoss)]
    public async Task Classifies_damage_severity(string description, DamageSeverity expected)
    {
        Assert.Equal(expected, (await Analyze(description)).DamageSeverity);
    }

    [Fact]
    public async Task Counts_vehicles_in_a_pileup()
    {
        var analysis = await Analyze("Three-car pileup on I-95 when the other driver braked hard.");

        Assert.Equal(3, analysis.VehiclesInvolved);
    }

    [Fact]
    public async Task Unrecovered_theft_is_a_total_loss_and_not_drivable()
    {
        var analysis = await Analyze("My car was stolen from the driveway overnight.", LossType.Theft);

        Assert.Equal(DamageSeverity.TotalLoss, analysis.DamageSeverity);
        Assert.False(analysis.Drivable);
    }
}

public class ResilientClaimAnalyzerTests
{
    private static readonly ClaimAnalysis PrimaryResult = Analysis(DamageSeverity.Severe);

    private sealed class StubAnalyzer(Func<CancellationToken, Task<ClaimAnalysis>> run) : IClaimAnalyzer
    {
        public string Name => "stub-llm";
        public Task<ClaimAnalysis> AnalyzeAsync(ClaimSubmitted claim, CancellationToken ct) => run(ct);
    }

    private static ResilientClaimAnalyzer Create(IClaimAnalyzer? primary, TimeSpan? timeout = null) =>
        new(primary, new HeuristicClaimAnalyzer(), NullLogger<ResilientClaimAnalyzer>.Instance, timeout);

    [Fact]
    public async Task Uses_the_primary_analyzer_when_it_succeeds()
    {
        var outcome = await Create(new StubAnalyzer(_ => Task.FromResult(PrimaryResult)))
            .AnalyzeAsync(Claim(), TestContext.Current.CancellationToken);

        Assert.Equal("stub-llm", outcome.Analyzer);
        Assert.Same(PrimaryResult, outcome.Analysis);
    }

    [Fact]
    public async Task Falls_back_to_heuristics_when_the_primary_throws()
    {
        var outcome = await Create(new StubAnalyzer(_ => throw new HttpRequestException("429 Too Many Requests")))
            .AnalyzeAsync(Claim(), TestContext.Current.CancellationToken);

        Assert.Equal("heuristic", outcome.Analyzer);
    }

    [Fact]
    public async Task Falls_back_when_the_primary_times_out()
    {
        var slow = new StubAnalyzer(async ct =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            return PrimaryResult;
        });

        var outcome = await Create(slow, TimeSpan.FromMilliseconds(50)).AnalyzeAsync(Claim(), TestContext.Current.CancellationToken);

        Assert.Equal("heuristic", outcome.Analyzer);
    }

    [Fact]
    public async Task Runs_heuristics_only_when_no_llm_is_configured()
    {
        var outcome = await Create(primary: null).AnalyzeAsync(Claim(), TestContext.Current.CancellationToken);

        Assert.Equal("heuristic", outcome.Analyzer);
    }
}
