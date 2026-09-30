using Claimline.Api.Data;
using Claimline.ServiceDefaults;
using Microsoft.EntityFrameworkCore;

namespace Claimline.Api.Messaging;

/// <summary>Lets request handlers wake the dispatcher right after a commit instead of waiting for the next poll.</summary>
public sealed class OutboxSignal
{
    private readonly SemaphoreSlim _signal = new(0, 1);

    public void Notify()
    {
        try { _signal.Release(); }
        catch (SemaphoreFullException) { }
    }

    public Task WaitAsync(TimeSpan timeout, CancellationToken ct) => _signal.WaitAsync(timeout, ct);
}

/// <summary>
/// Relays committed outbox rows to Service Bus. Rows are claimed with UPDLOCK/READPAST so several API replicas
/// can run this loop without double-sending; a crash between send and commit can still re-send, which is why
/// every consumer is idempotent on MessageId (at-least-once delivery, effectively-once processing).
/// </summary>
public sealed class OutboxDispatcher(
    IServiceScopeFactory scopes,
    IEventPublisher publisher,
    OutboxSignal signal,
    TimeProvider clock,
    ILogger<OutboxDispatcher> logger) : BackgroundService
{
    public const int BatchSize = 50;
    public const int MaxAttempts = 10;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var sent = await DispatchPendingAsync(stoppingToken);
                if (sent == BatchSize) continue;
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Outbox dispatch failed");
            }

            try { await signal.WaitAsync(PollInterval, stoppingToken); }
            catch (OperationCanceledException) { }
        }
    }

    public async Task<int> DispatchPendingAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ClaimlineDb>();
        var strategy = db.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var batch = await db.Outbox
                .FromSql($"SELECT TOP ({BatchSize}) * FROM dbo.Outbox WITH (UPDLOCK, READPAST, ROWLOCK) WHERE SentAt IS NULL AND Attempts < {MaxAttempts} ORDER BY CreatedAt")
                .ToListAsync(ct);

            var sent = 0;
            foreach (var message in batch)
            {
                try
                {
                    using var activity = ClaimlineTelemetry.Source.StartActivity("outbox publish " + message.EventType);
                    activity?.SetTag("messaging.message.id", message.Id);
                    activity?.SetTag("claimline.claim_id", message.CorrelationId);

                    await publisher.PublishAsync(message.EventType, message.Id.ToString(), message.CorrelationId, message.Payload, ct);

                    var now = clock.GetUtcNow();
                    message.SentAt = now;
                    sent++;
                    if (Guid.TryParse(message.CorrelationId, out var claimId))
                        db.ClaimEvents.Add(new ClaimEvent
                        {
                            ClaimId = claimId,
                            At = now,
                            Type = "event-published",
                            Title = $"{message.EventType} published to Service Bus",
                            Detail = $"Message {message.Id}",
                        });
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    message.Attempts++;
                    message.LastError = ex.Message.Length > 1000 ? ex.Message[..1000] : ex.Message;
                    logger.LogWarning(ex, "Publishing outbox message {MessageId} failed (attempt {Attempt})", message.Id, message.Attempts);
                }
            }

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return sent;
        });
    }
}
