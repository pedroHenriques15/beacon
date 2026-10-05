import { describe, it, expect } from 'vitest';
import { easterSunday, isTradingDay, previousTradingDay } from './xetra-calendar';

describe('Xetra calendar', () => {
  it('finds Easter Sunday', () => {
    expect(easterSunday(2024)).toBe('2024-03-31');
    expect(easterSunday(2025)).toBe('2025-04-20');
    expect(easterSunday(2026)).toBe('2026-04-05');
  });

  it('skips weekends and Xetra holidays', () => {
    for (const day of ['2026-01-01', '2026-04-03', '2026-04-06', '2026-05-01', '2025-12-26']) {
      expect(isTradingDay(day)).toBe(false);
    }
    expect(isTradingDay('2026-09-26')).toBe(false); // Saturday
    expect(isTradingDay('2026-05-25')).toBe(true); // Whit Monday: Xetra trades
    expect(isTradingDay('2026-09-30')).toBe(true);
  });

  it('steps back over weekends and holidays', () => {
    expect(previousTradingDay('2026-07-27')).toBe('2026-07-24');
    expect(previousTradingDay('2026-04-07')).toBe('2026-04-02');
    expect(previousTradingDay('2025-12-29')).toBe('2025-12-23');
  });
});
