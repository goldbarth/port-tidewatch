import { Component, input, computed } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { Gauge } from '../gauge.model';

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
  // absolute "seit HH:MM:SS". Null (no alert recorded yet) reads as a calm "stable".
  readonly stageDuration = computed<string>(() => {
    const secs = this.gauge().timeInStageSeconds;
    if (secs === null) return 'stable';
    const stage = this.gauge().stage;
    return `${stage} for ${formatDuration(secs)}`;
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
