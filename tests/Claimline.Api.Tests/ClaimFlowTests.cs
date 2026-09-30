using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Claimline.Api.Claims;
using Claimline.Api.Data;
using Claimline.Api.Messaging;
using Claimline.Contracts;
using Claimline.ServiceDefaults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Claimline.Api.Tests;

public class ClaimFlowTests(ApiFixture fixture)
{
    private readonly HttpClient _http = fixture.Factory.CreateClient();
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static object Fnol(string policy = "CL-100245", string lastName = "Santos", DateTimeOffset? lossDate = null) => new
    {
        policyNumber = policy,
        lastName,
        lossType = "collision",
        lossDate = lossDate ?? DateTimeOffset.UtcNow.AddDays(-1),
        lossLocation = "Coral Gables, FL",
        description = "Scraped a pillar in a parking garage, dented the rear bumper. Nobody was hurt.",
        estimatedAmount = 800,
        injuriesReported = false,
        policeReportFiled = false,
    };

    private async Task<SubmitClaimResponse> SubmitAsync(object body, string? idempotencyKey = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/claims") { Content = JsonContent.Create(body) };
        if (idempotencyKey is not null) request.Headers.Add("Idempotency-Key", idempotencyKey);
        var response = await _http.SendAsync(request, Ct);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<SubmitClaimResponse>(ClaimlineJson.Options, Ct))!;
    }

    [Fact]
    public async Task Fnol_is_accepted_and_the_event_is_queued_in_the_same_transaction()
    {
        var response = await _http.PostAsJsonAsync("/api/claims", Fnol(), Ct);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var body = (await response.Content.ReadFromJsonAsync<SubmitClaimResponse>(ClaimlineJson.Options, Ct))!;
        Assert.Matches(@"^CLM-\d{4}-\d{6}$", body.ClaimNumber);
        Assert.Equal($"/api/claims/{body.ClaimNumber}", response.Headers.Location?.OriginalString);

        var claim = await fixture.LoadClaimAsync(body.ClaimNumber);
        Assert.Equal(ClaimStatus.Submitted, claim.Status);

        var outbox = await fixture.WithDbAsync(db => db.Outbox.SingleAsync(o => o.CorrelationId == claim.Id.ToString(), Ct));
        Assert.Equal(EventTypes.ClaimSubmitted, outbox.EventType);
        Assert.Null(outbox.SentAt);

        var evt = JsonSerializer.Deserialize<ClaimSubmitted>(outbox.Payload, ClaimlineJson.Options)!;
        Assert.Equal("CL-100245", evt.Policy.PolicyNumber);
        Assert.Equal(1, evt.Attempt);
    }

    [Fact]
    public async Task Retrying_with_the_same_idempotency_key_returns_the_original_claim()
    {
        var key = Guid.NewGuid().ToString();

        var first = await SubmitAsync(Fnol(), key);
        using var retry = new HttpRequestMessage(HttpMethod.Post, "/api/claims") { Content = JsonContent.Create(Fnol()) };
        retry.Headers.Add("Idempotency-Key", key);
        var second = await _http.SendAsync(retry, Ct);

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal("true", second.Headers.GetValues("Idempotent-Replayed").Single());
        var replayed = (await second.Content.ReadFromJsonAsync<SubmitClaimResponse>(ClaimlineJson.Options, Ct))!;
        Assert.Equal(first.ClaimNumber, replayed.ClaimNumber);
        Assert.Equal(1, await fixture.WithDbAsync(db => db.Claims.CountAsync(c => c.IdempotencyKey == key, Ct)));
    }

    [Theory]
    [InlineData("CL-100245", "NotSantos")]
    [InlineData("CL-999999", "Santos")]
    public async Task Unknown_policy_and_wrong_name_get_the_same_error(string policy, string lastName)
    {
        var response = await _http.PostAsJsonAsync("/api/claims", Fnol(policy, lastName), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = (await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(Ct))!;
        Assert.Equal("We couldn't find a policy with that number and last name.", problem.Errors["policyNumber"].Single());
    }

    [Fact]
    public async Task Future_loss_dates_are_rejected()
    {
        var response = await _http.PostAsJsonAsync("/api/claims", Fnol(lossDate: DateTimeOffset.UtcNow.AddDays(2)), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = (await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(Ct))!;
        Assert.Contains("lossDate", problem.Errors.Keys);
    }

    [Fact]
    public async Task Outbox_dispatcher_publishes_pending_events_once_and_records_them_on_the_timeline()
    {
        var submitted = await SubmitAsync(Fnol());
        var dispatcher = new OutboxDispatcher(
            fixture.Factory.Services.GetRequiredService<IServiceScopeFactory>(), fixture.Publisher,
            new OutboxSignal(), TimeProvider.System, NullLogger<OutboxDispatcher>.Instance);

        await dispatcher.DispatchPendingAsync(Ct);
        await dispatcher.DispatchPendingAsync(Ct);

        var claim = await fixture.LoadClaimAsync(submitted.ClaimNumber);
        var published = fixture.Publisher.Messages.Where(m => m.CorrelationId == claim.Id.ToString()).ToList();
        Assert.Single(published);
        Assert.Equal(EventTypes.ClaimSubmitted, published[0].EventType);
        Assert.Contains(claim.Timeline, t => t.Type == "event-published");
        Assert.Equal(0, await fixture.WithDbAsync(db => db.Outbox.CountAsync(o => o.CorrelationId == claim.Id.ToString() && o.SentAt == null, Ct)));
    }

    [Fact]
    public async Task Triage_result_is_applied_once_even_if_the_message_is_redelivered()
    {
        var submitted = await SubmitAsync(Fnol());
        var claim = await fixture.LoadClaimAsync(submitted.ClaimNumber);
        var evt = Triaged(claim.Id, submitted.ClaimNumber, attempt: 1, TriageRoute.FastTrack);
        var context = new MessageContext(Guid.NewGuid().ToString(), claim.Id.ToString(), 1, DateTimeOffset.UtcNow);

        Assert.Equal(ProjectionOutcome.Applied, await HandleAsync(evt, context));
        Assert.Equal(ProjectionOutcome.Duplicate, await HandleAsync(evt, context with { DeliveryCount = 2 }));

        var updated = await fixture.LoadClaimAsync(submitted.ClaimNumber);
        Assert.Equal(ClaimStatus.Triaged, updated.Status);
        Assert.Equal(TriageRoute.FastTrack, updated.Route);
        Assert.Single(updated.Timeline, t => t.Type == "triaged");
    }

    [Fact]
    public async Task A_late_result_from_an_older_attempt_does_not_overwrite_a_retriage()
    {
        var submitted = await SubmitAsync(Fnol());
        var retriage = await _http.PostAsync($"/api/claims/{submitted.ClaimNumber}/retriage", null, Ct);
        Assert.Equal(HttpStatusCode.Accepted, retriage.StatusCode);
        var claim = await fixture.LoadClaimAsync(submitted.ClaimNumber);
        Assert.Equal(2, claim.TriageAttempt);

        var fresh = Triaged(claim.Id, submitted.ClaimNumber, attempt: 2, TriageRoute.Standard);
        var stale = Triaged(claim.Id, submitted.ClaimNumber, attempt: 1, TriageRoute.SpecialInvestigations);

        Assert.Equal(ProjectionOutcome.Applied, await HandleAsync(fresh, NewContext(claim.Id)));
        Assert.Equal(ProjectionOutcome.Stale, await HandleAsync(stale, NewContext(claim.Id)));

        var updated = await fixture.LoadClaimAsync(submitted.ClaimNumber);
        Assert.Equal(TriageRoute.Standard, updated.Route);
        Assert.Contains(updated.Timeline, t => t.Type == "stale-result-ignored");
    }

    [Fact]
    public async Task Claim_detail_exposes_decision_and_timeline()
    {
        var submitted = await SubmitAsync(Fnol());
        var claim = await fixture.LoadClaimAsync(submitted.ClaimNumber);
        await HandleAsync(Triaged(claim.Id, submitted.ClaimNumber, 1, TriageRoute.FastTrack), NewContext(claim.Id));

        var detail = (await _http.GetFromJsonAsync<ClaimDetail>($"/api/claims/{submitted.ClaimNumber}", ClaimlineJson.Options, Ct))!;

        Assert.Equal(ClaimStatus.Triaged, detail.Status);
        Assert.Equal("heuristic", detail.Analyzer);
        Assert.Equal("fnol-received", detail.Timeline[0].Type);
        Assert.Equal("triaged", detail.Timeline[^1].Type);
        Assert.NotNull(detail.TriageSeconds);
    }

    private async Task<ProjectionOutcome> HandleAsync(ClaimTriaged evt, MessageContext context)
    {
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ClaimTriagedHandler>().HandleAsync(evt, context, Ct);
    }

    private static MessageContext NewContext(Guid claimId) =>
        new(Guid.NewGuid().ToString(), claimId.ToString(), 1, DateTimeOffset.UtcNow);

    private static ClaimTriaged Triaged(Guid claimId, string claimNumber, int attempt, TriageRoute route) => new(
        claimId, claimNumber, attempt, route, SeverityScore: 10, FraudScore: 0, ReserveAmount: 1_500,
        Signals: [new("DAMAGE_MINOR", SignalCategory.Severity, 10, "Minor damage.")],
        new ClaimAnalysis(DamageSeverity.Minor, false, 1, false, true, ["rear bumper"], "Minor collision."),
        Analyzer: "heuristic", TriagedAt: DateTimeOffset.UtcNow);
}
