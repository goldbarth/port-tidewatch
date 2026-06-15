using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tidewatch.Contracts;
using Tidewatch.Source.Configuration;
using Tidewatch.Source.Pegelonline;
using Tidewatch.Source.Publishing;

namespace Tidewatch.Source;

/// <summary>
/// Polls the live PEGELONLINE Elbe feed and emits the same <see cref="Reading"/> records the
/// simulator does — downstream cannot tell them apart. On start it resolves each station's
/// PNP elevation and backfills the trend window; then it polls the latest value per station
/// on a cadence. Raw cm-above-PNP values are converted to metres NHN in <see cref="PegelMapper"/>.
/// Transient API failures are logged and retried on the next poll; they never crash the host.
/// </summary>
public sealed class PegelonlineSource : BackgroundService, IReadingSource
{
    private readonly PegelonlineClient _client;
    private readonly IReadingPublisher _publisher;
    private readonly PegelonlineOptions _options;
    private readonly ILogger<PegelonlineSource> _logger;

    // Resolved PNP elevation (m above NHN) per station UUID.
    private readonly Dictionary<string, decimal> _pnpOffsets = new();

    public PegelonlineSource(
        PegelonlineClient client,
        IReadingPublisher publisher,
        IOptions<PegelonlineOptions> options,
        ILogger<PegelonlineSource> logger)
    {
        _client = client;
        _publisher = publisher;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_options.Stations.Count == 0)
        {
            _logger.LogWarning("Pegelonline source active but no stations configured; nothing to poll.");
            return;
        }

        await ResolveOffsetsAndBackfillAsync(stoppingToken);

        _logger.LogInformation(
            "Polling {Count} Pegelonline station(s) every {Interval}.",
            _options.Stations.Count, _options.PollInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await Task.Delay(_options.PollInterval, stoppingToken); }
            catch (TaskCanceledException) { break; }

            foreach (var station in _options.Stations)
            {
                if (!_pnpOffsets.TryGetValue(station.Uuid, out var offset))
                    continue; // offset unresolved (start-up failure); retried below

                try
                {
                    var current = await _client.GetCurrentAsync(station.Uuid, stoppingToken);
                    if (current is null) continue; // 304 Not Modified — no duplicate emitted

                    await PublishAsync(station, current, offset, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Poll failed for station {GaugeId} ({Uuid}); retrying next interval.",
                        station.GaugeId, station.Uuid);
                }
            }
        }
    }

    // Resolve each station's PNP offset (config override or live gaugeZero) and backfill the
    // window. A station that fails here is skipped; its offset stays unresolved and the poll
    // loop ignores it until a later start. Backfill failure is non-fatal.
    private async Task ResolveOffsetsAndBackfillAsync(CancellationToken cancellationToken)
    {
        var since = DateTimeOffset.UtcNow - _options.Backfill;

        foreach (var station in _options.Stations)
        {
            try
            {
                var offset = station.PnpOffsetMeters
                    ?? await _client.GetGaugeZeroAsync(station.Uuid, cancellationToken);

                if (offset is null)
                {
                    _logger.LogWarning(
                        "No PNP offset for station {GaugeId} ({Uuid}); skipping until next start.",
                        station.GaugeId, station.Uuid);
                    continue;
                }

                _pnpOffsets[station.Uuid] = offset.Value;

                var history = await _client.GetMeasurementsAsync(station.Uuid, since, cancellationToken);
                foreach (var measurement in history)
                    await PublishAsync(station, measurement, offset.Value, cancellationToken);

                _logger.LogInformation(
                    "Station {GaugeId} ready (PNP {Offset:0.000} m NHN, {Count} backfilled).",
                    station.GaugeId, offset.Value, history.Count);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Start-up resolve/backfill failed for station {GaugeId} ({Uuid}).",
                    station.GaugeId, station.Uuid);
            }
        }
    }

    private Task PublishAsync(
        PegelStationOptions station, PegelMeasurement measurement, decimal offset, CancellationToken cancellationToken)
    {
        var level = PegelMapper.ToNhnMeters(measurement.Value, offset);
        var reading = new Reading(station.GaugeId, level, measurement.Timestamp);
        return _publisher.PublishAsync(reading, cancellationToken);
    }
}
