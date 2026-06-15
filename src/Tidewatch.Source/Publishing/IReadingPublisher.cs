using Tidewatch.Contracts;

namespace Tidewatch.Source.Publishing;

/// <summary>
/// The seam a reading source publishes through. Sources depend on this, not on the
/// transport — so a source knows nothing about RabbitMQ.
/// </summary>
public interface IReadingPublisher
{
    Task PublishAsync(Reading reading, CancellationToken cancellationToken);
}
