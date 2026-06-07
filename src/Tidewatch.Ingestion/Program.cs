using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Tidewatch.Ingestion.Configuration;
using Tidewatch.Ingestion.Consumer;
using Tidewatch.Ingestion.Evaluation;
using Tidewatch.Ingestion.State;
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

// OpenTelemetry is a later phase — not wired here.

var host = builder.Build();
host.Run();
