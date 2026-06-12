using System.Diagnostics;
using System.Text.Json;
using Tidewatch.Contracts;
using Tidewatch.Ingestion.Telemetry;
using Tidewatch.Ingestion.Transport;

namespace Tidewatch.Ingestion.Alerting;

/// <summary>
/// Serialises an <see cref="AlertEvent"/> and publishes it to the fanout alert exchange
/// via the transport's dedicated publish channel. The publish is wrapped in a span from
/// the ingestion <see cref="ActivitySource"/>, so it nests under the active "ingest
/// reading" span — consistent with the rest of the OpenTelemetry path.
/// </summary>
public sealed class RabbitMqAlertPublisher : IAlertPublisher
{
    private readonly RabbitMqTransport _transport;

    public RabbitMqAlertPublisher(RabbitMqTransport transport) => _transport = transport;

    public async Task PublishAsync(AlertEvent alert, CancellationToken cancellationToken)
    {
        using var activity = IngestionTelemetry.Source.StartActivity(
            "publish alert", ActivityKind.Producer);
        activity?.SetTag("gauge.id", alert.GaugeId);
        activity?.SetTag("surge.stage.previous", alert.PreviousStage);
        activity?.SetTag("surge.stage.current", alert.NewStage);
        activity?.SetTag("reading.value_m", (double)alert.Level);

        var body = JsonSerializer.SerializeToUtf8Bytes(alert);
        await _transport.PublishAlertAsync(body, cancellationToken);
    }
}
