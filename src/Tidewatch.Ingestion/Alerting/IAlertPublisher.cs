using Tidewatch.Contracts;

namespace Tidewatch.Ingestion.Alerting;

/// <summary>
/// Publishes <see cref="AlertEvent"/>s emitted at the stage-change chokepoint. An
/// abstraction so the state holder depends on the intent, not the transport.
/// </summary>
public interface IAlertPublisher
{
    Task PublishAsync(AlertEvent alert, CancellationToken cancellationToken);
}
