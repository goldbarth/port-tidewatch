import { Component, inject, computed } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { toSignal } from '@angular/core/rxjs-interop';
import { timer } from 'rxjs';
import { GaugesService } from './gauges.service';
import { GaugeCard } from './gauge-card/gauge-card';
import { WhatIfPanel } from './what-if-panel/what-if-panel';

const STALE_AFTER_S = 12; // ~3 missed polls at the 4s cadence

@Component({
  selector: 'app-root',
  imports: [GaugeCard, WhatIfPanel, DecimalPipe],
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

  // 1 Hz clock so "updated Ns ago" advances between polls.
  private readonly now = toSignal(timer(0, 1000), { initialValue: 0 });

  readonly counts = computed(() => {
    const c = { normal: 0, warning: 0, severe: 0 };
    for (const g of this.gauges()) c[g.stage]++;
    return c;
  });

  // Overall status is the worst stage currently present.
  readonly overallStatus = computed<'normal' | 'warning' | 'severe'>(() => {
    const c = this.counts();
    if (c.severe > 0) return 'severe';
    if (c.warning > 0) return 'warning';
    return 'normal';
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
        return `live · ${ago}s ago`;
      case 'stale':
        return `stale · ${ago}s ago`;
      default:
        return 'connecting…';
    }
  });
}
