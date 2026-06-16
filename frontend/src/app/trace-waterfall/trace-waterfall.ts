import { Component, inject, signal, computed } from '@angular/core';
import { GaugesService } from '../gauges.service';
import { JaegerService, TraceSpan } from '../jaeger.service';

type Status = 'idle' | 'loading' | 'ok' | 'empty' | 'error';

interface Bar {
  spanID: string;
  label: string;
  depth: number;
  durMs: string;
  x: number; // % offset of the bar's start across the trace span
  w: number; // % width
  y: number; // row top, in viewBox units
}

const ROW = 10; // viewBox units per span row
const ROW_PX = 26; // rendered row height

/**
 * "Under the hood" view (M8 stretch): fetches one ingest trace for a gauge from the
 * Jaeger query API and draws its spans as time-offset bars. Kept deliberately separate
 * from the gauge dashboard — this explains the instrumentation, it is not part of the
 * domain monitoring. Degrades gracefully when Jaeger isn't reachable.
 */
@Component({
  selector: 'app-trace-waterfall',
  templateUrl: './trace-waterfall.html',
  styleUrl: './trace-waterfall.css',
})
export class TraceWaterfall {
  private readonly gaugesService = inject(GaugesService);
  private readonly jaeger = inject(JaegerService);

  readonly rowPx = ROW_PX;

  readonly gaugeIds = computed(() => this.gaugesService.gauges().map((g) => g.gaugeId));

  readonly selected = signal<string | null>(null);
  readonly status = signal<Status>('idle');
  private readonly spans = signal<TraceSpan[]>([]);

  readonly statusText = computed<string>(() => {
    switch (this.status()) {
      case 'loading':
        return 'Lade Trace…';
      case 'empty':
        return 'Kein Trace gefunden — läuft Jaeger und wurde gerade ingestiert?';
      case 'error':
        return 'Jaeger-Query nicht erreichbar (ist die /jaeger-api-Route vorhanden?).';
      case 'ok':
        return '';
      default:
        return 'Pegel wählen, um den jüngsten Trace zu laden.';
    }
  });

  readonly viewBox = computed(() => `0 0 100 ${Math.max(this.spans().length, 1) * ROW}`);
  readonly heightPx = computed(() => this.spans().length * ROW_PX);

  readonly bars = computed<Bar[]>(() => {
    const spans = this.spans();
    if (spans.length === 0) return [];

    const start = Math.min(...spans.map((s) => s.startTime));
    const end = Math.max(...spans.map((s) => s.startTime + s.duration));
    const total = end - start || 1;
    const parents = new Map(spans.map((s) => [s.spanID, s.parentSpanID]));

    return spans.map((s, i) => ({
      spanID: s.spanID,
      label: s.operationName,
      depth: depthOf(s.spanID, parents),
      durMs: (s.duration / 1000).toFixed(2),
      x: ((s.startTime - start) / total) * 100,
      w: Math.max((s.duration / total) * 100, 0.8),
      y: i * ROW + 1.5,
    }));
  });

  readonly barHeight = ROW - 3;

  onSelect(gaugeId: string): void {
    this.selected.set(gaugeId);
    void this.load();
  }

  async load(): Promise<void> {
    const gaugeId = this.selected();
    if (!gaugeId) return;
    this.status.set('loading');
    this.spans.set([]);
    try {
      const spans = await this.jaeger.latestTrace(gaugeId);
      this.spans.set(spans);
      this.status.set(spans.length ? 'ok' : 'empty');
    } catch {
      this.status.set('error');
    }
  }
}

/** Nesting depth of a span by walking its parent chain, capped against cycles. */
function depthOf(spanId: string, parents: Map<string, string | null>): number {
  let depth = 0;
  let current = parents.get(spanId) ?? null;
  while (current && depth < 8) {
    depth++;
    current = parents.get(current) ?? null;
  }
  return depth;
}
