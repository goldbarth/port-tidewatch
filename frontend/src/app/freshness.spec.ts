import { freshness, staleAfterSeconds, POLL_INTERVAL_S } from './freshness';

describe('staleAfterSeconds', () => {
  it('is null when the cadence is unknown (< 2 readings)', () => {
    expect(staleAfterSeconds(null)).toBeNull();
  });

  it('uses 2x cadence for a slow source (PEGELONLINE ~60 s → 120 s)', () => {
    expect(staleAfterSeconds(60)).toBe(120);
  });

  it('falls back to cadence + poll lag for a fast source (Simulator ~2 s)', () => {
    // 2x cadence (4 s) would flap against the 4 s poll; the poll-lag term dominates.
    expect(staleAfterSeconds(2)).toBe(2 + POLL_INTERVAL_S + 2);
  });
});

describe('freshness', () => {
  it('is unknown without a measurement age', () => {
    expect(freshness(null, 60)).toBe('unknown');
  });

  it('is unknown when the cadence is not yet known', () => {
    expect(freshness(10, null)).toBe('unknown');
  });

  it('stays live for a normal 60 s PEGELONLINE interval', () => {
    expect(freshness(60, 60)).toBe('live');
  });

  it('goes stale only past 2x the PEGELONLINE cadence', () => {
    expect(freshness(120, 60)).toBe('live');
    expect(freshness(121, 60)).toBe('stale');
  });

  it('keeps the Simulator live at its few-second age', () => {
    expect(freshness(4, 2)).toBe('live');
  });
});
