using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using RabbitMQ.Client;
using Tidewatch.Source;
using Tidewatch.Source.Configuration;
using Tidewatch.Source.Pegelonline;
using Tidewatch.Source.Publishing;

// Producer host: feeds the ingestion pipeline with Reading messages. Exactly one source is
// active per run, chosen by the ReadingSource config switch (Simulator | Pegelonline), so
// the same build serves the scripted demo or the real Elbe feed without recompiling.

var builder = Host.CreateApplicationBuilder(args);

// Source switch — validated at startup; a bad/empty value fails fast.
builder.Services
    .AddOptions<ReadingSourceOptions>()
    .Configure(o => o.Active = builder.Configuration[ReadingSourceOptions.Key])
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<ReadingSourceOptions>, ReadingSourceOptionsValidator>();

// Publish target is common to every source; the documented RABBITMQ_HOST env var wins.
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

// Register only the selected source and its dependencies. An invalid value registers
// nothing — ValidateOnStart then fails the host with a clear message.
if (ReadingSourceParser.TryParse(builder.Configuration[ReadingSourceOptions.Key], out var kind))
{
    switch (kind)
    {
        case ReadingSourceKind.Simulator:
            // Scripted-surge tunables; the documented SURGE_PEAK_M / SURGE_PERIOD_S win.
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
            builder.Services.AddHostedService<SimulatorSource>();
            break;

        case ReadingSourceKind.Pegelonline:
            builder.Services
                .AddOptions<PegelonlineOptions>()
                .Bind(builder.Configuration.GetSection(PegelonlineOptions.SectionName));
            builder.Services.AddHttpClient();
            builder.Services.AddSingleton<PegelonlineClient>();
            builder.Services.AddHostedService<PegelonlineSource>();
            break;
    }
}

var host = builder.Build();
await host.RunAsync();
