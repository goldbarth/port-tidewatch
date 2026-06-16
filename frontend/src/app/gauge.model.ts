export interface TrendPoint {
  t: string;
  v: number;
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
  measuredAt: string | null; // ISO timestamp of the newest reading
  cadenceSeconds: number | null; // inferred source cadence (median gap); null until 2 readings
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
