import { Component, inject, computed } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { GaugesService } from './gauges.service';
import { GaugeCard } from './gauge-card/gauge-card';
import { Clock } from './clock';

const STALE_AFTER_S = 12; // ~3 missed polls at the 4s cadence

@Component({
  selector: 'app-root',
  imports: [GaugeCard, DecimalPipe],
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
  private readonly clock = inject(Clock);
  private readonly state = this.service.state;
  readonly gauges = computed(() => this.state().gauges);

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

  // Poll liveness only — is the API reachable? Measurement freshness (the age of each
  // reading) now lives per tile in GaugeCard (#63). live = recent successful poll,
  // stale = last poll failed or polls stopped succeeding, offline = no poll yet.
  private readonly secondsSincePoll = computed<number | null>(() => {
    const t = this.state().lastUpdated;
    return t === null ? null : Math.floor((this.clock.now() - t) / 1000);
  });

  readonly connection = computed<'live' | 'stale' | 'offline'>(() => {
    const s = this.state();
    if (s.lastUpdated === null) return 'offline';
    if (!s.connected || (this.secondsSincePoll() ?? 0) > STALE_AFTER_S) return 'stale';
    return 'live';
  });

  readonly connectionLabel = computed<string>(() => {
    switch (this.connection()) {
      case 'live':
        return 'live';
      case 'stale':
        return 'veraltet';
      default:
        return 'verbinde…';
    }
  });

  // Wall-clock time anchor in Hamburg's zone — reads together with the per-tile age:
  // current time → measurement N s old → status. Client-side only, no API call.
  private static readonly timeFormat = new Intl.DateTimeFormat('de-DE', {
    timeZone: 'Europe/Berlin',
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
    hour12: false,
  });

  readonly clockTime = computed<string>(() => App.timeFormat.format(this.clock.now()));
}
