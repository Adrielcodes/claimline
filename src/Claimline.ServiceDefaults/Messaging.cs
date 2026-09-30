using System.Text.Json;
using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Claimline.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Claimline.ServiceDefaults;

public interface IEventPublisher
{
    /// <param name="messageId">Stable id. Consumers de-duplicate on it, so a re-send must reuse it.</param>
    Task PublishAsync(string eventType, string messageId, string correlationId, string payloadJson, CancellationToken ct);
}

public sealed class ServiceBusEventPublisher(ServiceBusClient client) : IEventPublisher, IAsyncDisposable
{
    private readonly ServiceBusSender _sender = client.CreateSender(Topology.ClaimsTopic);

    public Task PublishAsync(string eventType, string messageId, string correlationId, string payloadJson, CancellationToken ct) =>
        _sender.SendMessageAsync(new ServiceBusMessage(payloadJson)
        {
            MessageId = messageId,
            CorrelationId = correlationId,
            Subject = eventType,
            ContentType = "application/json",
        }, ct);

    public ValueTask DisposeAsync() => _sender.DisposeAsync();
}

/// <summary>Thrown when retrying a message can never succeed; the message goes straight to the dead-letter queue.</summary>
public sealed class PermanentMessageFailure(string reason, string description) : Exception(description)
{
    public string Reason { get; } = reason;
}

public sealed record MessageContext(string MessageId, string CorrelationId, int DeliveryCount, DateTimeOffset EnqueuedAt);

/// <summary>
/// Base class for a topic-subscription consumer. Each message gets its own DI scope, manual settlement,
/// and a clear split between transient failures (abandon, Service Bus redelivers up to MaxDeliveryCount,
/// then dead-letters) and permanent ones (dead-letter immediately with a reason).
/// </summary>
public abstract class ServiceBusConsumer<T>(
    ServiceBusClient client,
    IServiceScopeFactory scopes,
    ILogger logger,
    string subscription,
    int maxConcurrentCalls = 4) : BackgroundService
{
    private ServiceBusProcessor? _processor;

    protected abstract Task HandleAsync(T message, MessageContext context, IServiceProvider services, CancellationToken ct);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _processor = client.CreateProcessor(Topology.ClaimsTopic, subscription, new ServiceBusProcessorOptions
        {
            AutoCompleteMessages = false,
            MaxConcurrentCalls = maxConcurrentCalls,
            MaxAutoLockRenewalDuration = TimeSpan.FromMinutes(5),
        });
        _processor.ProcessMessageAsync += OnMessageAsync;
        _processor.ProcessErrorAsync += args =>
        {
            logger.LogError(args.Exception, "Service Bus error on {Subscription} ({Source})", subscription, args.ErrorSource);
            return Task.CompletedTask;
        };

        await _processor.StartProcessingAsync(stoppingToken);
        logger.LogInformation("Consuming {Topic}/{Subscription}", Topology.ClaimsTopic, subscription);

        try { await Task.Delay(Timeout.Infinite, stoppingToken); }
        catch (OperationCanceledException) { }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_processor is not null)
        {
            await _processor.StopProcessingAsync(cancellationToken);
            await _processor.DisposeAsync();
        }
        await base.StopAsync(cancellationToken);
    }

    private async Task OnMessageAsync(ProcessMessageEventArgs args)
    {
        var msg = args.Message;
        using var _ = logger.BeginScope(new Dictionary<string, object>
        {
            ["MessageId"] = msg.MessageId,
            ["CorrelationId"] = msg.CorrelationId ?? "",
            ["Subscription"] = subscription,
        });

        T? payload;
        try
        {
            payload = JsonSerializer.Deserialize<T>(msg.Body.ToString(), ClaimlineJson.Options);
        }
        catch (JsonException ex)
        {
            await args.DeadLetterMessageAsync(msg, "DeserializationFailed", ex.Message, args.CancellationToken);
            return;
        }

        if (payload is null)
        {
            await args.DeadLetterMessageAsync(msg, "EmptyPayload", "Message body was null.", args.CancellationToken);
            return;
        }

        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var context = new MessageContext(msg.MessageId, msg.CorrelationId ?? "", msg.DeliveryCount, msg.EnqueuedTime);
            await HandleAsync(payload, context, scope.ServiceProvider, args.CancellationToken);
            await args.CompleteMessageAsync(msg, args.CancellationToken);
        }
        catch (PermanentMessageFailure ex)
        {
            logger.LogWarning("Dead-lettering {MessageId}: {Reason} {Description}", msg.MessageId, ex.Reason, ex.Message);
            await args.DeadLetterMessageAsync(msg, ex.Reason, ex.Message, args.CancellationToken);
        }
        catch (Exception ex) when (!args.CancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Attempt {DeliveryCount} failed for {MessageId}; abandoning for redelivery", msg.DeliveryCount, msg.MessageId);
            await args.AbandonMessageAsync(msg, cancellationToken: args.CancellationToken);
        }
    }
}

public static class MessagingExtensions
{
    /// <summary>
    /// Local/emulator: connection string "ConnectionStrings:ServiceBus".
    /// Azure: "ServiceBus:Namespace" (e.g. sb-claimline.servicebus.windows.net) + managed identity, no secrets.
    /// </summary>
    public static IHostApplicationBuilder AddClaimlineServiceBus(this IHostApplicationBuilder builder)
    {
        builder.Services.AddSingleton(_ =>
        {
            var connectionString = builder.Configuration.GetConnectionString("ServiceBus");
            if (!string.IsNullOrWhiteSpace(connectionString))
                return new ServiceBusClient(connectionString);

            var ns = builder.Configuration["ServiceBus:Namespace"]
                ?? throw new InvalidOperationException("Configure ConnectionStrings:ServiceBus or ServiceBus:Namespace.");
            return new ServiceBusClient(ns, new DefaultAzureCredential());
        });
        builder.Services.AddSingleton<IEventPublisher, ServiceBusEventPublisher>();
        return builder;
    }
}
