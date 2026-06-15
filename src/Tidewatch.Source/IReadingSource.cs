namespace Tidewatch.Source;

/// <summary>
/// Marker for a reading source. A source produces <c>Reading</c>s and publishes them
/// through <see cref="Publishing.IReadingPublisher"/>; it knows nothing about thresholds or
/// transport. The active source is selected at startup (M7 source selection) and run as
/// the host's hosted service.
/// </summary>
public interface IReadingSource;
