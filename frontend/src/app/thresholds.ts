export type Stage = 'normal' | 'warning' | 'severe';

export interface Thresholds {
  /** Warning boundary, m NHN. */
  warning: number;
  /** Severe boundary, m NHN. */
  severe: number;
}

/**
 * The thresholds the running service is configured with (WADI: warning 4.50 m NHN,
 * severe 5.50 m NHN — see ADR-001). The single source of truth on the client for the
 * gauge-card bands and the what-if panel's "reset". Kept in sync with the service's
 * appsettings SurgeThresholds.
 */
export const CONFIGURED_THRESHOLDS: Thresholds = { warning: 4.5, severe: 5.5 };

/**
 * Plain level-against-boundaries classification. This is the illustrative "what-if" rule,
 * not the server's evaluator (which adds median smoothing, trend pre-escalation and
 * hysteresis); it is enough to show that thresholds are configuration, not code.
 */
export function classifyStage(level: number, t: Thresholds): Stage {
  if (level >= t.severe) return 'severe';
  if (level >= t.warning) return 'warning';
  return 'normal';
}
