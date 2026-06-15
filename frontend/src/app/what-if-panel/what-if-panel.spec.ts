import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { WhatIfPanel } from './what-if-panel';
import { GaugesService } from '../gauges.service';
import { CONFIGURED_THRESHOLDS } from '../thresholds';
import { Gauge } from '../gauge.model';

function gauge(gaugeId: string, level: number | null, stage: Gauge['stage']): Gauge {
  return {
    gaugeId,
    level,
    stage,
    changedAt: null,
    trend: [],
    rateMetersPerMin: null,
    timeInStageSeconds: null,
    windowMin: null,
    windowMax: null,
  };
}

describe('WhatIfPanel', () => {
  const gauges = signal<Gauge[]>([]);

  function create(): WhatIfPanel {
    TestBed.configureTestingModule({
      providers: [{ provide: GaugesService, useValue: { gauges } }],
    });
    return TestBed.createComponent(WhatIfPanel).componentInstance;
  }

  it('starts at the configured thresholds, unmodified', () => {
    gauges.set([]);
    const panel = create();
    expect(panel.warning()).toBe(CONFIGURED_THRESHOLDS.warning);
    expect(panel.severe()).toBe(CONFIGURED_THRESHOLDS.severe);
    expect(panel.modified()).toBe(false);
  });

  it('keeps warning below severe when dragged up', () => {
    gauges.set([]);
    const panel = create();
    panel.onWarning(6); // above severe — must clamp to severe - step
    expect(panel.warning()).toBe(5.4);
    expect(panel.modified()).toBe(true);
  });

  it('keeps severe above warning when dragged down', () => {
    gauges.set([]);
    const panel = create();
    panel.onSevere(0); // below warning — must clamp to warning + step
    expect(panel.severe()).toBe(4.6);
  });

  it('reset restores the configured thresholds', () => {
    gauges.set([]);
    const panel = create();
    panel.onWarning(2);
    panel.reset();
    expect(panel.warning()).toBe(CONFIGURED_THRESHOLDS.warning);
    expect(panel.severe()).toBe(CONFIGURED_THRESHOLDS.severe);
    expect(panel.modified()).toBe(false);
  });

  it('reclassifies live gauges against the dragged thresholds', () => {
    gauges.set([
      gauge('A', 4.6, 'normal'), // server normal, but 4.6 ≥ 4.5 → what-if warning
      gauge('B', 0.5, 'normal'),
      gauge('C', null, 'normal'),
    ]);
    const panel = create();

    const rows = panel.rows();
    expect(rows.find((r) => r.id === 'A')?.whatIf).toBe('warning');
    expect(rows.find((r) => r.id === 'A')?.differs).toBe(true);
    expect(rows.find((r) => r.id === 'B')?.whatIf).toBe('normal');
    expect(rows.find((r) => r.id === 'C')?.whatIf).toBeNull();
    expect(panel.differingCount()).toBe(1);
  });
});
