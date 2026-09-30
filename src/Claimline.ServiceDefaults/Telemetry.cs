using System.Diagnostics;
using System.Diagnostics.Metrics;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Claimline.ServiceDefaults;

/// <summary>Custom spans and business metrics. Shows up next to the automatic HTTP, SQL and Service Bus telemetry.</summary>
public static class ClaimlineTelemetry
{
    public const string Name = "Claimline";

    public static readonly ActivitySource Source = new(Name);
    private static readonly Meter Meter = new(Name);

    public static readonly Counter<long> ClaimsSubmitted = Meter.CreateCounter<long>("claimline.claims.submitted", description: "FNOL submissions accepted");
    public static readonly Counter<long> ClaimsTriaged = Meter.CreateCounter<long>("claimline.claims.triaged", description: "Claims triaged, tagged by route and analyzer");
    public static readonly Histogram<double> TriageLatency = Meter.CreateHistogram<double>("claimline.triage.latency", "ms", "Time from FNOL submission to triage decision");
}

public static class TelemetryExtensions
{
    /// <summary>
    /// Traces, metrics and logs go to Application Insights when APPLICATIONINSIGHTS_CONNECTION_STRING is set,
    /// otherwise to any OTLP endpoint (the Aspire dashboard in docker-compose). Service Bus spans are linked
    /// across processes, so one FNOL shows up as a single distributed trace: API → topic → worker → topic → API.
    /// </summary>
    public static IHostApplicationBuilder AddClaimlineTelemetry(this IHostApplicationBuilder builder, string serviceName)
    {
        AppContext.SetSwitch("Azure.Experimental.EnableActivitySource", true);

        var otel = builder.Services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(serviceName))
            .WithTracing(t => t
                .AddSource(ClaimlineTelemetry.Name)
                .AddSource("Azure.Messaging.ServiceBus.*")
                .AddAspNetCoreInstrumentation(o => o.Filter = ctx => !ctx.Request.Path.StartsWithSegments("/health"))
                .AddHttpClientInstrumentation())
            .WithMetrics(m => m
                .AddMeter(ClaimlineTelemetry.Name)
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation());

        builder.Logging.AddOpenTelemetry(o =>
        {
            o.IncludeScopes = true;
            o.IncludeFormattedMessage = true;
        });

        if (!string.IsNullOrWhiteSpace(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
            otel.UseAzureMonitor();
        else if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
            otel.UseOtlpExporter();

        return builder;
    }
}
