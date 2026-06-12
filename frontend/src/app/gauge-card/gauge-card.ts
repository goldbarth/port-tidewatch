import { Component, input, computed } from '@angular/core';
import { DecimalPipe, DatePipe } from '@angular/common';
import { Gauge } from '../gauge.model';

@Component({
  selector: 'app-gauge-card',
  imports: [DecimalPipe, DatePipe],
  templateUrl: './gauge-card.html',
  styleUrl: './gauge-card.css',
})
export class GaugeCard {
  readonly gauge = input.required<Gauge>();

  // Normalised SVG polyline points (viewBox 100x30) from the trend values. The y axis
  // is inverted so a higher level draws higher on the card.
  readonly sparkline = computed(() => {
    const pts = this.gauge().trend;
    if (pts.length < 2) return '';
    const vals = pts.map((p) => p.v);
    const min = Math.min(...vals);
    const max = Math.max(...vals);
    const span = max - min || 1;
    return pts
      .map((p, i) => {
        const x = (i / (pts.length - 1)) * 100;
        const y = 30 - ((p.v - min) / span) * 30;
        return `${x.toFixed(1)},${y.toFixed(1)}`;
      })
      .join(' ');
  });

  // Rate-of-change as a direction class. A small deadband keeps near-flat readings from
  // flickering between rising/falling; the server already damps the rate (least-squares).
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

  // Magnitude in m/min for the label; sign is carried by the arrow.
  readonly rateMagnitude = computed(() => {
    const rate = this.gauge().rateMetersPerMin;
    return rate === null ? null : Math.abs(rate);
  });
}
