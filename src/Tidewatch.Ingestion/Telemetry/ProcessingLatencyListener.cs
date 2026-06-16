using System.Diagnostics;

namespace Tidewatch.Ingestion.Telemetry;

/// <summary>
/// Observes the existing "ingest reading" span and feeds its duration into
/// <see cref="ProcessingLatencyStore"/>. It adds an <see cref="ActivityListener"/> on the
/// ingestion <see cref="ActivitySource"/> rather than taking a new measurement, so the
/// latency figure is derived from the same trace already exported to Jaeger (M8).
/// </summary>
/// <remarks>
/// The <c>gauge.id</c> tag is set by the consumer only after a reading is deserialised
/// and validated, so dead-lettered messages carry no tag and are skipped here — the
/// pulse reflects the healthy processing path, not rejected input.
/// </remarks>
public sealed class ProcessingLatencyListener : IHostedService, IDisposable
{
    private const string GaugeIdTag = "gauge.id";

    private readonly ProcessingLatencyStore _store;
    private ActivityListener? _listener;

    public ProcessingLatencyListener(ProcessingLatencyStore store) => _store = store;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == IngestionTelemetry.SourceName,
            // The span is already recorded for OTLP; AllData just lets this listener
            // receive the Stopped callback with the duration populated.
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = OnStopped
        };
        ActivitySource.AddActivityListener(_listener);
        return Task.CompletedTask;
    }

    private void OnStopped(Activity activity)
    {
        if (activity.GetTagItem(GaugeIdTag) is not string gaugeId)
            return;
        _store.Record(gaugeId, activity.Duration.TotalMilliseconds, DateTimeOffset.UtcNow);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _listener?.Dispose();
        return Task.CompletedTask;
    }

    public void Dispose() => _listener?.Dispose();
}
