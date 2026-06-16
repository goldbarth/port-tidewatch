import { Component, inject, computed, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { toSignal } from '@angular/core/rxjs-interop';
import { timer } from 'rxjs';
import { GaugesService } from './gauges.service';
import { GaugeCard } from './gauge-card/gauge-card';
import { TraceWaterfall } from './trace-waterfall/trace-waterfall';

const STALE_AFTER_S = 12; // ~3 missed polls at the 4s cadence

@Component({
  selector: 'app-root',
  imports: [GaugeCard, DecimalPipe, TraceWaterfall],
  templateUrl: './app.html',
  styleUrl: './app.css',
  // Accent (set in global styles) tracks the worst stage currently present.
  host: {
    '[class.status-normal]': "overallStatus() === 'normal'",
    '[class.status-warning]': "overallStatus() === 'warning'",
    '[class.status-severe]': "overallStatus() === 'severe'",
  },
})
export class App {
  private readonly service = inject(GaugesService);
  private readonly state = this.service.state;
  readonly gauges = computed(() => this.state().gauges);

  // Domain view vs. the under-the-hood trace view. Kept apart on purpose (M8 stretch).
  readonly view = signal<'gauges' | 'trace'>('gauges');

  // 1 Hz clock so "updated Ns ago" advances between polls.
  private readonly now = toSignal(timer(0, 1000), { initialValue: 0 });

  readonly counts = computed(() => {
    const c = { normal: 0, warning: 0, severe: 0 };
    for (const g of this.gauges()) c[g.stage]++;
    return c;
  });

  readonly overallStatus = computed<'normal' | 'warning' | 'severe'>(() => {
    const c = this.counts();
    if (c.severe > 0) return 'severe';
    if (c.warning > 0) return 'warning';
    return 'normal';
  });

  readonly overallStatusLabel = computed<string>(() => {
    switch (this.overallStatus()) {
      case 'severe':
        return 'Schwere Sturmflut';
      case 'warning':
        return 'Warnung';
      default:
        return 'Normal';
    }
  });

  readonly highestLevel = computed<number | null>(() => {
    const levels = this.gauges()
      .map((g) => g.level)
      .filter((l): l is number => l !== null);
    return levels.length ? Math.max(...levels) : null;
  });

  readonly secondsSinceUpdate = computed<number | null>(() => {
    this.now(); // re-evaluate each tick
    const t = this.state().lastUpdated;
    return t === null ? null : Math.floor((Date.now() - t) / 1000);
  });

  // Liveness: live = recent success, stale = last poll failed or data aged out,
  // offline = no successful poll yet.
  readonly connection = computed<'live' | 'stale' | 'offline'>(() => {
    const s = this.state();
    if (s.lastUpdated === null) return 'offline';
    const ago = this.secondsSinceUpdate() ?? 0;
    if (!s.connected || ago > STALE_AFTER_S) return 'stale';
    return 'live';
  });

  readonly connectionLabel = computed<string>(() => {
    const ago = this.secondsSinceUpdate();
    switch (this.connection()) {
      case 'live':
        return `live · vor ${ago} s`;
      case 'stale':
        return `veraltet · vor ${ago} s`;
      default:
        return 'verbinde…';
    }
  });
}
