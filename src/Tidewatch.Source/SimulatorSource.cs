using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tidewatch.Contracts;
using Tidewatch.Source.Configuration;
using Tidewatch.Source.Publishing;

namespace Tidewatch.Source;

/// <summary>
/// Emits a scripted <see cref="Reading"/> stream for a set of gauges. One gauge runs a
/// surge that crosses warning (4.50 m) and severe (5.50 m) then recedes; the rest stay
/// normal for contrast. Level = baseline + tide + surge + small noise. The surge is a
/// smooth raised-cosine bump so the rise is monotone and the evaluator does not flap.
/// Knows nothing about thresholds — input only.
/// </summary>
public sealed class SimulatorSource : BackgroundService, IReadingSource
{
    private const decimal TideAmplitude = 0.30m;   // gentle rolling tide, never trips warning alone
    private const double TidePeriodS = 60.0;        // seconds per tide cycle
    private const decimal NoiseAmplitude = 0.02m;   // tiny — keeps trend clean

    // Each gauge's signal role. Baseline is its calm level (m NHN); Surges marks the one
    // gauge that runs the scripted storm-surge event.
    private static readonly GaugeProfile[] Profiles =
    [
        new("CUX", Baseline: 0.80m, Surges: true),   // storm-surge gauge: normal → warning → severe → recede
        new("HEL", Baseline: 0.50m, Surges: false),  // stays normal throughout, for contrast
        new("STP", Baseline: 1.00m, Surges: false),  // mild tide, normal
        new("BHV", Baseline: 0.70m, Surges: false),  // mild tide, normal
    ];

    private readonly IReadingPublisher _publisher;
    private readonly SimulatorOptions _options;
    private readonly ILogger<SimulatorSource> _logger;
    private readonly Random _rng = new();

    public SimulatorSource(
        IReadingPublisher publisher,
        IOptions<SimulatorOptions> options,
        ILogger<SimulatorSource> logger)
    {
        _publisher = publisher;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(_options.IntervalSeconds);
        var surgePeak = _options.SurgePeakMeters;
        var surgePeriodS = _options.SurgePeriodSeconds;
        var surgeDurationS = surgePeriodS * 0.66;     // bump occupies most of the cycle, then rests
        var startedAt = DateTimeOffset.UtcNow;

        _logger.LogInformation(
            "Simulating {Count} gauges (surge peak {Peak:0.00} m, period {Period:0}s).",
            Profiles.Length, surgePeak, surgePeriodS);

        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTimeOffset.UtcNow;
            var t = (now - startedAt).TotalSeconds;

            foreach (var profile in Profiles)
            {
                var level = LevelAt(profile, t, surgePeak, surgePeriodS, surgeDurationS);
                var reading = new Reading(profile.Id, decimal.Round(level, 3), now);

                await _publisher.PublishAsync(reading, stoppingToken);

                _logger.LogInformation(
                    "{Gauge} {Value:0.000} m @ {Time:HH:mm:ss}",
                    reading.GaugeId, reading.Value, reading.Timestamp);
            }

            try { await Task.Delay(interval, stoppingToken); }
            catch (TaskCanceledException) { break; }
        }
    }

    // Composite level for a gauge at elapsed time t (seconds): tide baseline plus, for the
    // surge gauge, a smooth raised-cosine bump, plus tiny noise.
    private decimal LevelAt(
        GaugeProfile profile, double t, decimal surgePeak, double surgePeriodS, double surgeDurationS)
    {
        var tide = TideAmplitude * (decimal)Math.Sin(2 * Math.PI * t / TidePeriodS);
        var noise = (decimal)(_rng.NextDouble() - 0.5) * 2 * NoiseAmplitude;
        var surge = profile.Surges
            ? SurgeBump(t, profile.Baseline, surgePeak, surgePeriodS, surgeDurationS)
            : 0m;
        return profile.Baseline + tide + surge + noise;
    }

    // Raised-cosine bump: 0 at the start/end of its window, peaking mid-window. Active for
    // surgeDurationS out of every surgePeriodS, so the gauge rests at baseline between
    // events. Height is chosen so the peak reaches surgePeak above the gauge's baseline.
    private static decimal SurgeBump(
        double t, decimal baseline, decimal surgePeak, double surgePeriodS, double surgeDurationS)
    {
        var phase = t % surgePeriodS;
        if (phase >= surgeDurationS) return 0m;       // resting between surges
        var height = surgePeak - baseline;
        var shape = 0.5 * (1 - Math.Cos(2 * Math.PI * phase / surgeDurationS));
        return height * (decimal)shape;
    }

    private record GaugeProfile(string Id, decimal Baseline, bool Surges);
}
