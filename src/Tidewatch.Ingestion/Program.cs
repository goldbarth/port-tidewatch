using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using RabbitMQ.Client;
using Tidewatch.Ingestion.Api;
using Tidewatch.Ingestion.Configuration;
using Tidewatch.Ingestion.Consumer;
using Tidewatch.Ingestion.Evaluation;
using Tidewatch.Ingestion.State;
using Tidewatch.Ingestion.Telemetry;
using Tidewatch.Ingestion.Transport;

var builder = WebApplication.CreateBuilder(args);

// Options binding with startup validation.
builder.Services
    .AddOptions<SurgeThresholdOptions>()
    .Bind(builder.Configuration.GetSection(SurgeThresholdOptions.SectionName))
    .ValidateOnStart();
builder.Services
    .AddSingleton<IValidateOptions<SurgeThresholdOptions>, SurgeThresholdOptionsValidator>();

builder.Services
    .AddOptions<RabbitMqOptions>()
    .Bind(builder.Configuration.GetSection(RabbitMqOptions.SectionName));

// Transport infrastructure and live state are singletons.
builder.Services.AddSingleton<RabbitMqTransport>();
builder.Services.AddSingleton<GaugeStateHolder>();

// Evaluator (internals are the next focused session).
builder.Services.AddSingleton<ISurgeEvaluator, SurgeEvaluator>();

// Consumer as a hosted service.
builder.Services.AddHostedService<ReadingConsumer>();

// CORS for the dashboard when it is served from a different origin (Static Web Apps in
// the Container Apps stack). The origin comes from Cors:AllowedOrigin; left unset in the
// Kubernetes/dev stacks, where the dashboard is same-origin (Ingress / dev proxy) and
// no policy is needed.
var corsOrigin = builder.Configuration["Cors:AllowedOrigin"];
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
{
    if (!string.IsNullOrWhiteSpace(corsOrigin))
        policy.WithOrigins(corsOrigin).AllowAnyHeader().AllowAnyMethod();
}));

// Tracing across the ingestion path: our own spans plus RabbitMQ.Client's subscriber
// activity (which recovers the trace context published by the simulator). OTLP endpoint
// defaults to localhost:4317, overridable via OTEL_EXPORTER_OTLP_ENDPOINT.
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("tidewatch-ingestion"))
    .WithTracing(tracing => tracing
        .AddSource(IngestionTelemetry.SourceName)
        .AddSource(RabbitMQActivitySource.SubscriberSourceName)
        .AddOtlpExporter());

var app = builder.Build();

app.UseCors();

// Read-only HTTP surface for the dashboard. The reading consumer keeps running as a
// hosted service alongside it.
app.MapGet("/healthz", () => Results.Ok("ok"));
app.MapGet("/api/gauges", (GaugeStateHolder state) =>
    state.Snapshot().Select(GaugeMapper.ToDto));

app.Run();
