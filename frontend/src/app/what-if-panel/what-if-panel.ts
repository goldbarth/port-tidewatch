import { Component, inject, signal, computed } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { GaugesService } from '../gauges.service';
import { CONFIGURED_THRESHOLDS, classifyStage, Stage } from '../thresholds';

const DOMAIN_MIN = 0;
const DOMAIN_MAX = 6; // m NHN — matches the gauge-card chart domain
const STEP = 0.1;

interface WhatIfRow {
  id: string;
  level: number | null;
  whatIf: Stage | null;
  live: Stage;
  differs: boolean;
}

/**
 * A local, read-only exploration: drag the warning/severe thresholds and watch the live
 * readings reclassify in the browser. It writes nothing back — the running service keeps
 * its configured thresholds. Illustrates ADR-001's "thresholds are configuration, not code"
 * against real data.
 */
@Component({
  selector: 'app-what-if-panel',
  imports: [DecimalPipe],
  templateUrl: './what-if-panel.html',
  styleUrl: './what-if-panel.css',
})
export class WhatIfPanel {
  private readonly service = inject(GaugesService);

  readonly domainMin = DOMAIN_MIN;
  readonly domainMax = DOMAIN_MAX;
  readonly step = STEP;

  readonly warning = signal(CONFIGURED_THRESHOLDS.warning);
  readonly severe = signal(CONFIGURED_THRESHOLDS.severe);

  // Keep the two handles ordered: warning stays one step below severe, and vice versa.
  readonly warningMax = computed(() => round(this.severe() - STEP));
  readonly severeMin = computed(() => round(this.warning() + STEP));

  readonly modified = computed(
    () =>
      this.warning() !== CONFIGURED_THRESHOLDS.warning ||
      this.severe() !== CONFIGURED_THRESHOLDS.severe,
  );

  // Reclassify each live gauge against the dragged thresholds, client-side only.
  readonly rows = computed<WhatIfRow[]>(() => {
    const t = { warning: this.warning(), severe: this.severe() };
    return this.service.gauges().map((g) => {
      const whatIf = g.level === null ? null : classifyStage(g.level, t);
      return {
        id: g.gaugeId,
        level: g.level,
        whatIf,
        live: g.stage,
        differs: whatIf !== null && whatIf !== g.stage,
      };
    });
  });

  // How many of the reclassifications differ from what the service currently reports.
  readonly differingCount = computed(() => this.rows().filter((r) => r.differs).length);

  onWarning(value: number): void {
    this.warning.set(clamp(value, DOMAIN_MIN, this.warningMax()));
  }

  onSevere(value: number): void {
    this.severe.set(clamp(value, this.severeMin(), DOMAIN_MAX));
  }

  reset(): void {
    this.warning.set(CONFIGURED_THRESHOLDS.warning);
    this.severe.set(CONFIGURED_THRESHOLDS.severe);
  }
}

function clamp(value: number, min: number, max: number): number {
  return round(Math.min(Math.max(value, min), max));
}

// Avoid binary-float drift from the 0.1 step so the handles land on clean tenths.
function round(value: number): number {
  return Math.round(value * 10) / 10;
}
