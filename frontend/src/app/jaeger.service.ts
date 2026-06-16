import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';

/** A flattened span from a Jaeger trace, in the shape the waterfall draws. */
export interface TraceSpan {
  spanID: string;
  operationName: string;
  startTime: number; // microseconds since epoch
  duration: number; // microseconds
  parentSpanID: string | null;
}

const SERVICE = 'tidewatch-ingestion';

// Same-origin path to the Jaeger query API, mirroring how /api reaches the ingestion
// service: the dev proxy (and any deployed ingress) forwards it to Jaeger, so the browser
// never makes a cross-origin (CORS) call. The waterfall is a stretch "under the hood"
// view — where this route is absent it simply fails gracefully.
const QUERY_BASE = '/jaeger-api';

/**
 * Reads a single ingest trace for a gauge from the Jaeger query API (M8 stretch). Not a
 * polling stream — fetched on demand for the under-the-hood waterfall.
 */
@Injectable({ providedIn: 'root' })
export class JaegerService {
  private readonly http = inject(HttpClient);

  /** Most recent trace for a gauge as spans ordered by start time; empty when none. */
  async latestTrace(gaugeId: string): Promise<TraceSpan[]> {
    const tags = encodeURIComponent(JSON.stringify({ 'gauge.id': gaugeId }));
    const url = `${QUERY_BASE}/api/traces?service=${SERVICE}&tags=${tags}&limit=1&lookback=1h`;
    const res = await firstValueFrom(this.http.get<JaegerTracesResponse>(url));
    const trace = res.data?.[0];
    if (!trace) return [];
    return trace.spans
      .map((s) => ({
        spanID: s.spanID,
        operationName: s.operationName,
        startTime: s.startTime,
        duration: s.duration,
        parentSpanID: s.references?.find((r) => r.refType === 'CHILD_OF')?.spanID ?? null,
      }))
      .sort((a, b) => a.startTime - b.startTime);
  }
}

interface JaegerTracesResponse {
  data?: { traceID: string; spans: JaegerSpan[] }[];
}

interface JaegerSpan {
  spanID: string;
  operationName: string;
  startTime: number;
  duration: number;
  references?: { refType: string; spanID: string }[];
}
