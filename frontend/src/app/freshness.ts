/** Dashboard poll cadence (s); the GaugesService polls /api/gauges at this interval. */
export const POLL_INTERVAL_S = 4;

/** Slack on top of the poll cadence for the 1 Hz client tick and broker hop. */
const TICK_MARGIN_S = 2;

export type Freshness = 'live' | 'stale' | 'unknown';

/**
 * Per-tile stale threshold (s), derived from the source cadence reported by the server.
 * Two terms: `2 × cadence` flags a genuinely missed source interval (PEGELONLINE 60 s →
 * 120 s, per the issue's > 2× rule); `cadence + pollLag + margin` keeps a fast source
 * (Simulator ~2 s) from flapping stale on the dashboard's own 4 s poll lag. The cadence
 * itself is server-inferred, so the threshold adapts to the active source rather than a
 * hard-coded value. Null when the server has no cadence yet (< 2 readings).
 */
export function staleAfterSeconds(cadenceSeconds: number | null): number | null {
  if (cadenceSeconds === null) return null;
  return Math.max(2 * cadenceSeconds, cadenceSeconds + POLL_INTERVAL_S + TICK_MARGIN_S);
}

/** Classifies a measurement's age against its source-derived stale threshold. */
export function freshness(ageSeconds: number | null, cadenceSeconds: number | null): Freshness {
  if (ageSeconds === null) return 'unknown';
  const threshold = staleAfterSeconds(cadenceSeconds);
  if (threshold === null) return 'unknown';
  return ageSeconds > threshold ? 'stale' : 'live';
}
