using Azure.AI.OpenAI;
using Azure.Identity;
using Claimline.ServiceDefaults;
using Claimline.Triage;
using Claimline.Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.AddClaimlineTelemetry("claimline-worker");
builder.AddClaimlineServiceBus();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<HeuristicClaimAnalyzer>();

// The LLM analyzer is optional: without an endpoint the worker triages with the heuristic analyzer alone.
var endpoint = builder.Configuration["AzureOpenAI:Endpoint"];
var deployment = builder.Configuration["AzureOpenAI:Deployment"] ?? "gpt-4.1-mini";
if (!string.IsNullOrWhiteSpace(endpoint))
{
    builder.Services.AddSingleton<IClaimAnalyzer>(_ =>
        new AzureOpenAIClaimAnalyzer(new AzureOpenAIClient(new Uri(endpoint), new DefaultAzureCredential()).GetChatClient(deployment)));
}

builder.Services.AddSingleton(sp => new ResilientClaimAnalyzer(
    sp.GetService<IClaimAnalyzer>(),
    sp.GetRequiredService<HeuristicClaimAnalyzer>(),
    sp.GetRequiredService<ILogger<ResilientClaimAnalyzer>>()));

builder.Services.AddScoped<TriageHandler>();
builder.Services.AddHostedService<TriageConsumer>();
builder.Services.AddHostedService<NotificationConsumer>();

builder.Build().Run();
