import { classifyStage, CONFIGURED_THRESHOLDS } from './thresholds';

describe('classifyStage', () => {
  const t = CONFIGURED_THRESHOLDS;

  it('is normal below the warning boundary', () => {
    expect(classifyStage(4.49, t)).toBe('normal');
  });

  it('is warning at the warning boundary (inclusive)', () => {
    expect(classifyStage(4.5, t)).toBe('warning');
  });

  it('is warning just below the severe boundary', () => {
    expect(classifyStage(5.49, t)).toBe('warning');
  });

  it('is severe at the severe boundary (inclusive)', () => {
    expect(classifyStage(5.5, t)).toBe('severe');
  });

  it('is severe well above the severe boundary', () => {
    expect(classifyStage(7, t)).toBe('severe');
  });

  it('honours custom (dragged) thresholds', () => {
    expect(classifyStage(2, { warning: 1, severe: 3 })).toBe('warning');
    expect(classifyStage(0.5, { warning: 1, severe: 3 })).toBe('normal');
    expect(classifyStage(3, { warning: 1, severe: 3 })).toBe('severe');
  });
});
