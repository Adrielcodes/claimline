using System.Collections.Concurrent;
using Claimline.Api.Data;
using Claimline.ServiceDefaults;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.MsSql;

[assembly: AssemblyFixture(typeof(Claimline.Api.Tests.ApiFixture))]

namespace Claimline.Api.Tests;

public sealed record PublishedMessage(string EventType, string MessageId, string CorrelationId, string Payload);

public sealed class RecordingPublisher : IEventPublisher
{
    public ConcurrentQueue<PublishedMessage> Messages { get; } = new();

    public Task PublishAsync(string eventType, string messageId, string correlationId, string payloadJson, CancellationToken ct)
    {
        Messages.Enqueue(new(eventType, messageId, correlationId, payloadJson));
        return Task.CompletedTask;
    }
}

/// <summary>
/// One real SQL Server (Testcontainers) for the whole test run. Service Bus is swapped for an in-memory
/// recorder: these tests cover the API's transactional guarantees, not the broker.
/// </summary>
public sealed class ApiFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _sql = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    public WebApplicationFactory<Program> Factory { get; private set; } = null!;
    public RecordingPublisher Publisher { get; } = new();

    public async ValueTask InitializeAsync()
    {
        await _sql.StartAsync();
        var connectionString = _sql.GetConnectionString().Replace("Database=master", "Database=claimline_tests");

        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web =>
        {
            web.UseSetting("ConnectionStrings:Claims", connectionString);
            web.UseSetting("Messaging:Enabled", "false");
            web.UseSetting("Demo:SampleClaims", "false");
            web.UseSetting("RateLimiting:FnolPermitsPerMinute", "1000");
            web.ConfigureServices(services => services.AddSingleton<IEventPublisher>(Publisher));
        });

        _ = Factory.Server; // boots the app: runs migrations and seeds demo policies
    }

    public async Task WithDbAsync(Func<ClaimlineDb, Task> action)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        await action(scope.ServiceProvider.GetRequiredService<ClaimlineDb>());
    }

    public async Task<T> WithDbAsync<T>(Func<ClaimlineDb, Task<T>> action)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<ClaimlineDb>());
    }

    public Task<Claim> LoadClaimAsync(string claimNumber) => WithDbAsync(db =>
        db.Claims.Include(c => c.Timeline).AsNoTracking().SingleAsync(c => c.ClaimNumber == claimNumber));

    public async ValueTask DisposeAsync()
    {
        await Factory.DisposeAsync();
        await _sql.DisposeAsync();
    }
}
