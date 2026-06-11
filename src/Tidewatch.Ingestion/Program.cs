using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using RabbitMQ.Client;
using Tidewatch.Ingestion.Configuration;
using Tidewatch.Ingestion.Consumer;
using Tidewatch.Ingestion.Evaluation;
using Tidewatch.Ingestion.State;
using Tidewatch.Ingestion.Telemetry;
using Tidewatch.Ingestion.Transport;

var builder = Host.CreateApplicationBuilder(args);

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

// Tracing across the ingestion path: our own spans plus RabbitMQ.Client's subscriber
// activity (which recovers the trace context published by the simulator). OTLP endpoint
// defaults to localhost:4317, overridable via OTEL_EXPORTER_OTLP_ENDPOINT.
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("tidewatch-ingestion"))
    .WithTracing(tracing => tracing
        .AddSource(IngestionTelemetry.SourceName)
        .AddSource(RabbitMQActivitySource.SubscriberSourceName)
        .AddOtlpExporter());

var host = builder.Build();
host.Run();
