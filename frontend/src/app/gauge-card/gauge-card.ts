import { Component, input, computed, inject } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { toSignal } from '@angular/core/rxjs-interop';
import { timer } from 'rxjs';
import { Gauge } from '../gauge.model';
import { AppConfig } from '../app-config';

// Chart geometry. The y-axis is a FIXED level domain (0–6 m NHN) so the stage bands and
// the peak marker sit at true heights — unlike an auto-scaled sparkline. The bands are
// the threshold reference: their shared edges are the warning (4.50) and severe (5.50)
// boundaries.
const VB_W = 100;
const VB_H = 60;
const DOMAIN_MAX = 6; // m NHN
const WARNING_M = 4.5;
const SEVERE_M = 5.5;

/** Maps a level (m NHN) to a y coordinate; higher level → smaller y (drawn higher). */
function yOf(level: number): number {
  const clamped = Math.min(Math.max(level, 0), DOMAIN_MAX);
  return VB_H * (1 - clamped / DOMAIN_MAX);
}

interface Band {
  stage: 'normal' | 'warning' | 'severe';
  y: number;
  h: number;
}

const STAGE_LABELS: Record<Gauge['stage'], string> = {
  normal: 'Normal',
  warning: 'Warnung',
  severe: 'Schwere Sturmflut',
};

@Component({
  selector: 'app-gauge-card',
  imports: [DecimalPipe],
  templateUrl: './gauge-card.html',
  styleUrl: './gauge-card.css',
})
export class GaugeCard {
  readonly gauge = input.required<Gauge>();

  readonly vbWidth = VB_W;
  readonly vbHeight = VB_H;

  readonly stageLabel = computed(() => STAGE_LABELS[this.gauge().stage]);

  // Stage bands as filled background areas (top → bottom): severe, warning, normal.
  // Their edges are the thresholds, so no separate reference lines are needed.
  readonly bands: readonly Band[] = [
    { stage: 'severe', y: yOf(DOMAIN_MAX), h: yOf(SEVERE_M) - yOf(DOMAIN_MAX) },
    { stage: 'warning', y: yOf(SEVERE_M), h: yOf(WARNING_M) - yOf(SEVERE_M) },
    { stage: 'normal', y: yOf(WARNING_M), h: yOf(0) - yOf(WARNING_M) },
  ];

  // Trend points projected onto the fixed domain.
  private readonly points = computed(() => {
    const pts = this.gauge().trend;
    if (pts.length < 2) return [] as { x: number; y: number; v: number }[];
    return pts.map((p, i) => ({
      x: (i / (pts.length - 1)) * VB_W,
      y: yOf(p.v),
      v: p.v,
    }));
  });

  // Filled area under the level line, down to the baseline.
  readonly areaPath = computed(() => {
    const pts = this.points();
    if (pts.length === 0) return '';
    const line = pts.map((p) => `${p.x.toFixed(1)},${p.y.toFixed(1)}`).join(' L ');
    return `M ${line} L ${VB_W},${VB_H} L 0,${VB_H} Z`;
  });

  // The level line itself (drawn over the area for a crisp edge).
  readonly linePath = computed(() => {
    const pts = this.points();
    if (pts.length === 0) return '';
    return 'M ' + pts.map((p) => `${p.x.toFixed(1)},${p.y.toFixed(1)}`).join(' L ');
  });

  // Peak marker at the window maximum — shows how close the gauge came to the next
  // threshold. Positioned at the highest trend point.
  readonly peak = computed(() => {
    const pts = this.points();
    if (pts.length === 0) return null;
    let max = pts[0];
    for (const p of pts) if (p.v > max.v) max = p;
    return max;
  });

  // ── #28 trend signals, restyled ──
  private static readonly DEADBAND = 0.01; // m/min

  readonly trendDirection = computed<'rising' | 'falling' | 'steady' | 'none'>(() => {
    const rate = this.gauge().rateMetersPerMin;
    if (rate === null) return 'none';
    if (rate > GaugeCard.DEADBAND) return 'rising';
    if (rate < -GaugeCard.DEADBAND) return 'falling';
    return 'steady';
  });

  readonly trendArrow = computed(() => {
    switch (this.trendDirection()) {
      case 'rising':
        return '▲';
      case 'falling':
        return '▼';
      case 'steady':
        return '▬';
      default:
        return '';
    }
  });

  readonly rateMagnitude = computed(() => {
    const rate = this.gauge().rateMetersPerMin;
    return rate === null ? null : Math.abs(rate);
  });

  // Relative time in the current stage, from #28's timeInStageSeconds. Replaces the
  // absolute "seit HH:MM:SS". Null (no alert recorded yet) reads as a calm "stabil".
  readonly stageDuration = computed<string>(() => {
    const secs = this.gauge().timeInStageSeconds;
    if (secs === null) return 'stabil';
    return `${STAGE_LABELS[this.gauge().stage]} seit ${formatDuration(secs)}`;
  });

  // ── M8: pipeline latency pulse ──
  // p95 above this is "degraded" — a clear line for an in-process ingest span (a few ms
  // healthy; the RabbitMQ ack round-trip dominates). Tunable, deliberately a constant.
  private static readonly DEGRADED_MS = 250;
  // Telemetry older than this counts as "no recent data", mirroring the masthead's stale
  // indicator (#27): a stalled pipeline stops producing spans, so latency stops updating.
  private static readonly TELEMETRY_STALE_S = 15;

  // Auto-scaled mini sparkline (unlike the fixed-domain level chart, latency has no
  // natural ceiling — scale each gauge to its own recent range).
  private static readonly LAT_VB_W = 100;
  private static readonly LAT_VB_H = 20;
  readonly latVbWidth = GaugeCard.LAT_VB_W;
  readonly latVbHeight = GaugeCard.LAT_VB_H;

  // 1 Hz clock so telemetry ageing advances between the 4 s polls.
  private readonly now = toSignal(timer(0, 1000), { initialValue: 0 });

  private readonly telemetryAgeS = computed<number | null>(() => {
    this.now(); // re-evaluate each tick
    const at = this.gauge().latency.lastAt;
    return at === null ? null : Math.floor((Date.now() - Date.parse(at)) / 1000);
  });

  private readonly telemetryStale = computed<boolean>(() => {
    const age = this.telemetryAgeS();
    return age !== null && age > GaugeCard.TELEMETRY_STALE_S;
  });

  readonly latencyHealth = computed<'healthy' | 'degraded' | 'none'>(() => {
    const lat = this.gauge().latency;
    if (lat.lastMs === null) return 'none';
    if (this.telemetryStale()) return 'degraded';
    return (lat.p95Ms ?? 0) > GaugeCard.DEGRADED_MS ? 'degraded' : 'healthy';
  });

  readonly latencyText = computed<string>(() => {
    const lat = this.gauge().latency;
    if (lat.lastMs === null) return 'keine Telemetrie';
    if (this.telemetryStale()) return 'Telemetrie veraltet';
    return `${lat.lastMs} ms · p95 ${lat.p95Ms} ms`;
  });

  readonly latSparkPath = computed<string>(() => {
    const t = this.gauge().latency.trend;
    if (t.length < 2) return '';
    const min = Math.min(...t);
    const span = Math.max(...t) - min || 1;
    return (
      'M ' +
      t
        .map((v, i) => {
          const x = (i / (t.length - 1)) * GaugeCard.LAT_VB_W;
          const y = GaugeCard.LAT_VB_H * (1 - (v - min) / span);
          return `${x.toFixed(1)},${y.toFixed(1)}`;
        })
        .join(' L ')
    );
  });

  // ── M8: Jaeger deep-link ──
  // Service name from the ingestion OTLP resource (Program.cs AddService).
  private static readonly JAEGER_SERVICE = 'tidewatch-ingestion';

  private readonly config = inject(AppConfig);

  // Search this gauge's ingest traces in Jaeger, filtered by the gauge.id span tag.
  // Null when no Jaeger base URL is configured, so the link simply isn't shown.
  readonly jaegerUrl = computed<string | null>(() => {
    const base = this.config.jaegerBaseUrl;
    if (!base) return null;
    const tags = encodeURIComponent(JSON.stringify({ 'gauge.id': this.gauge().gaugeId }));
    return `${base}/search?service=${GaugeCard.JAEGER_SERVICE}&tags=${tags}&lookback=1h`;
  });
}

/** "45 s", "3 min", "1 h 5 min". */
function formatDuration(totalSeconds: number): string {
  if (totalSeconds < 60) return `${totalSeconds} s`;
  const minutes = Math.floor(totalSeconds / 60);
  if (minutes < 60) return `${minutes} min`;
  const hours = Math.floor(minutes / 60);
  return `${hours} h ${minutes % 60} min`;
}
