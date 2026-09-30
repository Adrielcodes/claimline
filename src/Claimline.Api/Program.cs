using System.Threading.RateLimiting;
using Claimline.Api.Claims;
using Claimline.Api.Data;
using Claimline.Api.Messaging;
using Claimline.Contracts;
using Claimline.ServiceDefaults;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddClaimlineTelemetry("claimline-api");

builder.Services.AddDbContext<ClaimlineDb>(o => o.UseSqlServer(
    builder.Configuration.GetConnectionString("Claims"),
    // Azure SQL serverless auto-pauses; the first query after a pause fails transiently until it resumes.
    sql => sql.EnableRetryOnFailure(maxRetryCount: 6, maxRetryDelay: TimeSpan.FromSeconds(10), errorNumbersToAdd: null)));

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<ClaimIntake>();
builder.Services.AddScoped<ClaimTriagedHandler>();
builder.Services.AddSingleton<OutboxSignal>();

var messagingEnabled = builder.Configuration.GetValue("Messaging:Enabled", true);
if (messagingEnabled)
{
    builder.AddClaimlineServiceBus();
    builder.Services.AddHostedService<OutboxDispatcher>();
    builder.Services.AddHostedService<ClaimTriagedConsumer>();
}

builder.Services.ConfigureHttpJsonOptions(o => ClaimlineJson.Configure(o.SerializerOptions));
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks().AddDbContextCheck<ClaimlineDb>(tags: ["ready"]);

var fnolPermits = builder.Configuration.GetValue("RateLimiting:FnolPermitsPerMinute", 10);
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("fnol", http => RateLimitPartition.GetFixedWindowLimiter(
        http.Request.Headers["X-Forwarded-For"].FirstOrDefault()?.Split(',')[0].Trim()
            ?? http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = fnolPermits, Window = TimeSpan.FromMinutes(1) }));
});

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseRateLimiter();

app.MapOpenApi();
app.MapScalarApiReference(o => o.WithTitle("Claimline API"));
app.MapGet("/", () => Results.Redirect("/scalar")).ExcludeFromDescription();

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") });

app.MapClaimEndpoints();

if (app.Configuration.GetValue("Database:MigrateOnStartup", true))
{
    await using (var scope = app.Services.CreateAsyncScope())
        await scope.ServiceProvider.GetRequiredService<ClaimlineDb>().Database.MigrateAsync();

    if (app.Configuration.GetValue("Demo:Seed", true))
        await DemoData.EnsureAsync(app.Services, app.Configuration.GetValue("Demo:SampleClaims", true), CancellationToken.None);
}

app.Run();

public partial class Program;
