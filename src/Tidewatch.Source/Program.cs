using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using RabbitMQ.Client;
using Tidewatch.Source.Configuration;
using Tidewatch.Source.Publishing;
using Tidewatch.Source;

// Producer host: feeds the ingestion pipeline with Reading messages. The active source is
// run as the host's hosted service (M7 source selection adds the Simulator | Pegelonline
// switch here). For now the scripted simulator is the source.

var builder = Host.CreateApplicationBuilder(args);

// Scripted-surge tunables; the documented SURGE_PEAK_M / SURGE_PERIOD_S env vars win.
builder.Services
    .AddOptions<SimulatorOptions>()
    .Bind(builder.Configuration.GetSection(SimulatorOptions.SectionName))
    .PostConfigure(o =>
    {
        if (decimal.TryParse(Environment.GetEnvironmentVariable("SURGE_PEAK_M"), out var peak))
            o.SurgePeakMeters = peak;
        if (double.TryParse(Environment.GetEnvironmentVariable("SURGE_PERIOD_S"), out var period))
            o.SurgePeriodSeconds = period;
    });

// Publish target; the documented RABBITMQ_HOST env var wins.
builder.Services
    .AddOptions<PublisherOptions>()
    .Bind(builder.Configuration.GetSection(PublisherOptions.SectionName))
    .PostConfigure(o =>
    {
        var host = Environment.GetEnvironmentVariable("RABBITMQ_HOST");
        if (!string.IsNullOrWhiteSpace(host)) o.HostName = host;
    });

builder.Services.AddSingleton<RabbitMqReadingPublisher>();
builder.Services.AddSingleton<IReadingPublisher>(sp =>
    sp.GetRequiredService<RabbitMqReadingPublisher>());

// Our producer spans plus RabbitMQ.Client publisher activity, exported via OTLP. The
// publisher activity is only emitted while a listener on PublisherSourceName is registered.
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("tidewatch-source"))
    .WithTracing(t => t
        .AddSource(RabbitMqReadingPublisher.ActivitySourceName)
        .AddSource(RabbitMQActivitySource.PublisherSourceName)
        .AddOtlpExporter());

builder.Services.AddHostedService<SimulatorSource>();

var host = builder.Build();
await host.RunAsync();
