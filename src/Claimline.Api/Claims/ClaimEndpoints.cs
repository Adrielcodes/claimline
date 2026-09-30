using Claimline.Api.Data;
using Claimline.Api.Messaging;
using Claimline.Contracts;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Claimline.Api.Claims;

public sealed record SubmitClaimResponse(string ClaimNumber, ClaimStatus Status, string StatusUrl);

public sealed record ClaimSummary(
    string ClaimNumber,
    ClaimStatus Status,
    LossType LossType,
    DateTimeOffset LossDate,
    DateTimeOffset ReportedAt,
    string InsuredName,
    string Vehicle,
    TriageRoute? Route,
    int? SeverityScore,
    int? FraudScore,
    decimal? ReserveAmount,
    string Summary);

public sealed record PolicyView(
    string PolicyNumber, string InsuredName, string State, DateOnly EffectiveDate, DateOnly ExpirationDate,
    IReadOnlyList<string> Coverages, decimal CollisionDeductible, string Vehicle, decimal VehicleValue, int PriorClaims);

public sealed record TimelineEntry(DateTimeOffset At, string Type, string Title, string? Detail);

public sealed record ClaimDetail(
    string ClaimNumber,
    ClaimStatus Status,
    LossType LossType,
    DateTimeOffset LossDate,
    DateTimeOffset ReportedAt,
    string LossLocation,
    string Description,
    decimal? EstimatedAmount,
    bool InjuriesReported,
    bool PoliceReportFiled,
    PolicyView Policy,
    int TriageAttempt,
    TriageRoute? Route,
    int? SeverityScore,
    int? FraudScore,
    decimal? ReserveAmount,
    string? Analyzer,
    ClaimAnalysis? Analysis,
    IReadOnlyList<TriageSignal> Signals,
    DateTimeOffset? TriagedAt,
    double? TriageSeconds,
    IReadOnlyList<TimelineEntry> Timeline);

public sealed record RouteStat(TriageRoute Route, int Count, decimal Reserves);

public sealed record ClaimStats(
    int Total,
    int Triaged,
    int Pending,
    decimal TotalReserves,
    double? MedianTriageSeconds,
    double? P95TriageSeconds,
    int SiuReferrals,
    IReadOnlyList<RouteStat> ByRoute,
    IReadOnlyDictionary<string, int> ByAnalyzer);

public sealed record DemoPolicy(string PolicyNumber, string LastName, string Vehicle, string Scenario);

public static class ClaimEndpoints
{
    public static void MapClaimEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api").WithTags("Claims");

        api.MapPost("/claims", SubmitAsync)
            .RequireRateLimiting("fnol")
            .WithSummary("Report a new claim (first notice of loss)")
            .WithDescription("Stores the claim and queues it for asynchronous triage. Send an Idempotency-Key header to make retries safe.");

        api.MapGet("/claims", ListAsync).WithSummary("Claims queue, newest first");
        api.MapGet("/claims/{claimNumber}", GetAsync).WithSummary("Claim detail with triage decision and audit timeline");
        api.MapPost("/claims/{claimNumber}/retriage", RetriageAsync)
            .RequireRateLimiting("fnol")
            .WithSummary("Run triage again; results from older attempts are discarded");
        api.MapGet("/stats", StatsAsync).WithSummary("Queue KPIs");
        api.MapGet("/demo/policies", DemoPoliciesAsync).WithTags("Demo").WithSummary("Sample policies for trying the FNOL form");
    }

    private static async Task<Results<Accepted<SubmitClaimResponse>, Ok<SubmitClaimResponse>, ValidationProblem>> SubmitAsync(
        SubmitClaimRequest request, HttpRequest http, ClaimIntake intake, OutboxSignal outbox, CancellationToken ct)
    {
        var key = http.Headers["Idempotency-Key"].FirstOrDefault();
        if (key is { Length: > 64 })
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["Idempotency-Key"] = ["Must be 64 characters or fewer."] });

        switch (await intake.SubmitAsync(request, key, ct))
        {
            case IntakeResult.Invalid invalid:
                return TypedResults.ValidationProblem(invalid.Errors);

            case IntakeResult.Accepted { Replayed: true } replay:
                http.HttpContext.Response.Headers["Idempotent-Replayed"] = "true";
                return TypedResults.Ok(ToResponse(replay.Claim));

            case IntakeResult.Accepted accepted:
                outbox.Notify();
                var response = ToResponse(accepted.Claim);
                return TypedResults.Accepted(response.StatusUrl, response);

            default:
                throw new InvalidOperationException();
        }
    }

    private static async Task<Ok<List<ClaimSummary>>> ListAsync(
        ClaimlineDb db, string? status, string? route, string? q, CancellationToken ct)
    {
        var query = db.Claims.AsNoTracking().Include(c => c.Policy).AsQueryable();
        if (ParseKebab<ClaimStatus>(status) is { } s) query = query.Where(c => c.Status == s);
        if (ParseKebab<TriageRoute>(route) is { } r) query = query.Where(c => c.Route == r);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            query = query.Where(c => c.ClaimNumber.Contains(term) || c.Policy.PolicyNumber.Contains(term)
                || c.Policy.InsuredLastName.Contains(term) || c.Description.Contains(term));
        }

        var claims = await query.OrderByDescending(c => c.ReportedAt).Take(100).ToListAsync(ct);
        return TypedResults.Ok(claims.Select(c => new ClaimSummary(
            c.ClaimNumber, c.Status, c.LossType, c.LossDate, c.ReportedAt,
            $"{c.Policy.InsuredFirstName} {c.Policy.InsuredLastName}", c.Vehicle(),
            c.Route, c.SeverityScore, c.FraudScore, c.ReserveAmount,
            c.Analysis?.Summary ?? Truncate(c.Description, 140))).ToList());
    }

    private static async Task<Results<Ok<ClaimDetail>, NotFound>> GetAsync(string claimNumber, ClaimlineDb db, CancellationToken ct)
    {
        var c = await db.Claims.AsNoTracking()
            .Include(x => x.Policy)
            .Include(x => x.Timeline.OrderBy(t => t.At).ThenBy(t => t.Id))
            .AsSplitQuery()
            .FirstOrDefaultAsync(x => x.ClaimNumber == claimNumber, ct);
        if (c is null) return TypedResults.NotFound();

        var p = c.Policy;
        return TypedResults.Ok(new ClaimDetail(
            c.ClaimNumber, c.Status, c.LossType, c.LossDate, c.ReportedAt, c.LossLocation, c.Description,
            c.EstimatedAmount, c.InjuriesReported, c.PoliceReportFiled,
            new PolicyView(p.PolicyNumber, $"{p.InsuredFirstName} {p.InsuredLastName}", p.State, p.EffectiveDate, p.ExpirationDate,
                p.Coverages, p.CollisionDeductible, p.Vehicle, p.VehicleValue, p.PriorClaims),
            c.TriageAttempt, c.Route, c.SeverityScore, c.FraudScore, c.ReserveAmount, c.Analyzer, c.Analysis, c.Signals,
            c.TriagedAt, c.TriagedAt is { } t ? Math.Round((t - c.LastSubmittedAt).TotalSeconds, 2) : null,
            c.Timeline.Select(e => new TimelineEntry(e.At, e.Type, e.Title, e.Detail)).ToList()));
    }

    private static async Task<Results<Accepted<SubmitClaimResponse>, NotFound>> RetriageAsync(
        string claimNumber, ClaimIntake intake, OutboxSignal outbox, CancellationToken ct)
    {
        var claim = await intake.RetriageAsync(claimNumber, ct);
        if (claim is null) return TypedResults.NotFound();
        outbox.Notify();
        var response = ToResponse(claim);
        return TypedResults.Accepted(response.StatusUrl, response);
    }

    private static async Task<Ok<ClaimStats>> StatsAsync(ClaimlineDb db, CancellationToken ct)
    {
        var rows = await db.Claims.AsNoTracking()
            .Select(c => new { c.Status, c.Route, c.ReserveAmount, c.Analyzer, c.LastSubmittedAt, c.TriagedAt })
            .ToListAsync(ct);

        var triaged = rows.Where(r => r.Status == ClaimStatus.Triaged).ToList();
        var latencies = triaged
            .Where(r => r.TriagedAt is not null)
            .Select(r => (r.TriagedAt!.Value - r.LastSubmittedAt).TotalSeconds)
            .Where(s => s >= 0)
            .Order()
            .ToList();

        return TypedResults.Ok(new ClaimStats(
            Total: rows.Count,
            Triaged: triaged.Count,
            Pending: rows.Count - triaged.Count,
            TotalReserves: triaged.Sum(r => r.ReserveAmount ?? 0),
            MedianTriageSeconds: Percentile(latencies, 0.5),
            P95TriageSeconds: Percentile(latencies, 0.95),
            SiuReferrals: triaged.Count(r => r.Route == TriageRoute.SpecialInvestigations),
            ByRoute: Enum.GetValues<TriageRoute>()
                .Select(route => new RouteStat(route,
                    triaged.Count(r => r.Route == route),
                    triaged.Where(r => r.Route == route).Sum(r => r.ReserveAmount ?? 0)))
                .ToList(),
            ByAnalyzer: triaged.GroupBy(r => r.Analyzer ?? "unknown").ToDictionary(g => g.Key, g => g.Count())));
    }

    private static async Task<Ok<List<DemoPolicy>>> DemoPoliciesAsync(ClaimlineDb db, CancellationToken ct) =>
        TypedResults.Ok(await db.Policies.AsNoTracking()
            .Where(p => p.DemoScenario != null)
            .OrderBy(p => p.PolicyNumber)
            .Select(p => new DemoPolicy(p.PolicyNumber, p.InsuredLastName, p.Vehicle, p.DemoScenario!))
            .ToListAsync(ct));

    private static SubmitClaimResponse ToResponse(Claim claim) =>
        new(claim.ClaimNumber, claim.Status, $"/api/claims/{claim.ClaimNumber}");

    private static string Vehicle(this Claim claim) => claim.Policy.Vehicle;

    /// <summary>Query strings use the same kebab-case enum names as the JSON payloads ("fast-track").</summary>
    private static T? ParseKebab<T>(string? value) where T : struct, Enum =>
        Enum.TryParse<T>(value?.Replace("-", ""), ignoreCase: true, out var parsed) ? parsed : null;

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";

    private static double? Percentile(List<double> sorted, double p)
    {
        if (sorted.Count == 0) return null;
        var index = (int)Math.Ceiling(p * sorted.Count) - 1;
        return Math.Round(sorted[Math.Clamp(index, 0, sorted.Count - 1)], 2);
    }
}
