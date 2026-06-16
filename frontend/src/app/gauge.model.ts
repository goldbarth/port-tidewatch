export interface TrendPoint {
  t: string;
  v: number;
}

/**
 * Processing-latency pulse for a gauge, derived from the ingest span's duration (M8).
 * Figures are null and `trend` empty until telemetry has been observed; `lastAt` (ISO
 * timestamp of the most recent sample) lets the view mark stale telemetry degraded.
 */
export interface Latency {
  lastMs: number | null;
  p50Ms: number | null;
  p95Ms: number | null;
  lastAt: string | null;
  trend: number[];
}

export interface Gauge {
  gaugeId: string;
  level: number | null;
  stage: 'normal' | 'warning' | 'severe';
  changedAt: string | null;
  trend: TrendPoint[];
  rateMetersPerMin: number | null;
  timeInStageSeconds: number | null;
  windowMin: number | null;
  windowMax: number | null;
  latency: Latency;
}

/**
 * One polled view of the service plus liveness metadata. The last successful gauge
 * snapshot is held across a failed poll (so a brief API blip does not blank the view);
 * `connected` reflects the most recent poll and `lastUpdated` the last success, which
 * together drive the stale / connection indicator. A single held snapshot is not
 * client-side history — see ADR-002.
 */
export interface GaugesState {
  gauges: Gauge[];
  lastUpdated: number | null; // epoch ms of the last successful poll
  connected: boolean; // was the most recent poll successful
}
