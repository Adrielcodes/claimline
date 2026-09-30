using Claimline.Api.Claims;
using Claimline.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Claimline.Api.Data;

/// <summary>
/// Demo policies (dates kept relative to today so every scenario stays reproducible) and a handful of sample
/// claims. Sample claims go through the real intake path, so they are triaged by the worker like any other FNOL.
/// </summary>
public static class DemoData
{
    private static readonly string[] FullCoverage = ["liability", "collision", "comprehensive", "uninsured-motorist", "rental"];

    public static async Task EnsureAsync(IServiceProvider services, bool sampleClaims, CancellationToken ct)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ClaimlineDb>();
        var clock = scope.ServiceProvider.GetRequiredService<TimeProvider>();
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

        foreach (var template in Policies(today))
        {
            var existing = await db.Policies.FirstOrDefaultAsync(p => p.PolicyNumber == template.PolicyNumber, ct);
            if (existing is null)
            {
                db.Policies.Add(template);
                continue;
            }
            existing.EffectiveDate = template.EffectiveDate;
            existing.ExpirationDate = template.ExpirationDate;
            existing.Coverages = template.Coverages;
            existing.PriorClaims = template.PriorClaims;
            existing.DemoScenario = template.DemoScenario;
        }
        await db.SaveChangesAsync(ct);

        if (!sampleClaims || await db.Claims.AnyAsync(ct)) return;

        var intake = scope.ServiceProvider.GetRequiredService<ClaimIntake>();
        var now = clock.GetUtcNow();
        foreach (var request in SampleClaims(now))
            await intake.SubmitAsync(request, idempotencyKey: $"seed-{request.PolicyNumber}-{request.LossType}", ct);
    }

    private static IEnumerable<Policy> Policies(DateOnly today)
    {
        Policy Make(string number, string first, string last, string vehicle, decimal value, int startedDaysAgo,
            string[] coverages, int priorClaims, string scenario, int termDays = 365) => new()
        {
            PolicyNumber = number,
            InsuredFirstName = first,
            InsuredLastName = last,
            InsuredEmail = $"{first.ToLowerInvariant()}.{last.ToLowerInvariant()}@example.com",
            State = "FL",
            EffectiveDate = today.AddDays(-startedDaysAgo),
            ExpirationDate = today.AddDays(-startedDaysAgo + termDays),
            Coverages = [.. coverages],
            CollisionDeductible = 500,
            Vehicle = vehicle,
            VehicleValue = value,
            PriorClaims = priorClaims,
            DemoScenario = scenario,
        };

        yield return Make("CL-100245", "Maria", "Santos", "2023 Toyota Camry SE", 24_000, 210, FullCoverage, 0,
            "Clean record, full coverage. Good for Fast Track and Standard routes.");
        yield return Make("CL-100311", "James", "Carter", "2019 Ford F-150 XLT", 28_500, 9, FullCoverage, 0,
            "Policy started 9 days ago, which raises an early-loss fraud signal.");
        yield return Make("CL-100487", "Aisha", "Khan", "2012 Honda Civic LX", 6_500, 120, ["liability", "uninsured-motorist"], 0,
            "Liability only. Collision and theft claims go to Coverage Review.");
        yield return Make("CL-100522", "Derek", "Olsen", "2021 Tesla Model 3", 31_000, 300, FullCoverage, 3,
            "Three prior claims, which raises a claim-frequency fraud signal.");
        yield return Make("CL-100630", "Priya", "Natarajan", "2020 Mazda CX-5", 19_500, 400, FullCoverage, 0,
            "Lapsed policy (expired about a month ago).", termDays: 365);
        yield return Make("CL-100718", "Luis", "Romero", "2016 Nissan Altima", 9_800, 150, FullCoverage, 1,
            "Older, lower-value car. Heavy damage tips it into Total Loss.");
    }

    private static IEnumerable<SubmitClaimRequest> SampleClaims(DateTimeOffset now)
    {
        yield return new("CL-100245", "Santos", LossType.Collision, now.AddDays(-1), "Publix parking lot, Coral Gables, FL",
            "Backing out of a parking spot I scraped a concrete pillar. Small dent and scratches on the rear bumper. No one was hurt and the car drives fine.",
            900, false, false, null);
        yield return new("CL-100245", "Santos", LossType.Glass, now.AddDays(-3), "I-95 northbound near Fort Lauderdale",
            "A rock kicked up by a truck hit the windshield and left a chip that is spreading into a crack across the driver side.",
            450, false, false, null);
        yield return new("CL-100311", "Carter", LossType.Theft, now.AddDays(-2), "Apartment complex lot, Hialeah, FL",
            "Truck was parked overnight and gone in the morning.",
            28_000, false, false, null);
        yield return new("CL-100487", "Khan", LossType.Collision, now.AddDays(-4), "SW 8th St and 27th Ave, Miami, FL",
            "I was stopped at a red light when the other driver rear-ended me. Rear bumper and trunk are pushed in, taillight broken. The car is still drivable.",
            3_200, false, true, null);
        yield return new("CL-100718", "Romero", LossType.Collision, now.AddDays(-1), "Florida Turnpike, Homestead, FL",
            "Another car merged into my lane on the highway and I was t-boned on the passenger side. Airbags deployed, the front axle is bent and the car had to be towed. My passenger was taken to the hospital with neck pain.",
            null, true, true, null);
        yield return new("CL-100522", "Olsen", LossType.Vandalism, now.AddDays(-45), "Parking garage, Brickell, FL",
            "Someone keyed both doors on the driver side and smashed the side mirror while the car was parked in the garage.",
            2_100, false, false, null);
    }
}
