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
}
