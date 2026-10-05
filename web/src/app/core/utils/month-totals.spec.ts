import { describe, it, expect } from 'vitest';
import {
  aggregateByMonth,
  daysInMonth,
  latestClosedMonth,
  monthCells,
  monthName,
  previousMonth,
} from './month-totals';

const row = (month: string, income: number, expenses: number) => ({
  month,
  income,
  expenses,
  net: income - expenses,
});

describe('month-totals', () => {
  it('aggregates banks into one row per month, newest first', () => {
    const totals = aggregateByMonth([
      row('2026-08', 100, 40),
      row('2026-09', 10, 5),
      row('2026-08', 50, 10),
    ]);

    expect(totals).toEqual([
      { month: '2026-09', income: 10, expenses: 5, net: 5 },
      { month: '2026-08', income: 150, expenses: 50, net: 100 },
    ]);
  });

  it('builds the scrubber months oldest first', () => {
    const cells = monthCells([row('2026-09', 10, 5), row('2026-07', 1, 2), row('2026-09', 5, 5)]);

    expect(cells).toEqual([
      { key: '2026-07', income: 1, expenses: 2 },
      { key: '2026-09', income: 15, expenses: 10 },
    ]);
  });

  it('returns no months when there is no data', () => {
    expect(monthCells([])).toEqual([]);
  });

  it('steps back a month across a year boundary', () => {
    expect(previousMonth('2026-01')).toBe('2025-12');
    expect(previousMonth('2026-10')).toBe('2026-09');
  });

  it('picks the latest closed month, else the latest, else the current one', () => {
    expect(latestClosedMonth(['2026-10', '2026-09'], '2026-10')).toBe('2026-09');
    expect(latestClosedMonth(['2026-10'], '2026-10')).toBe('2026-10');
    expect(latestClosedMonth([], '2026-10')).toBe('2026-10');
  });

  it('names months and counts their days', () => {
    expect(monthName('2026-09')).toBe('September');
    expect(monthName('2026-09', 'short')).toBe('Sep');
    expect(daysInMonth('2026-02')).toBe(28);
    expect(daysInMonth('2028-02')).toBe(29);
  });
});
