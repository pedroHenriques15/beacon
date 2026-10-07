import { describe, it, expect } from 'vitest';
import { windowTops } from './six-months';
import { CATEGORY_UNKNOWN } from '../../core/constants/categories';

type Type = 'credit' | 'debit' | 'unknown';

function row(month: string, amount: number, type: Type, category: string | null) {
  return {
    month,
    amount,
    type,
    category: category ? { name: category, color: `var(--${category})` } : null,
    isExcluded: false,
  };
}

const months = ['2026-04', '2026-05', '2026-06'];

describe('windowTops', () => {
  it('ranks each side by its total over the window, with a monthly average', () => {
    const tops = windowTops(
      [
        row('2026-04', 300, 'debit', 'Rent'),
        row('2026-05', 300, 'debit', 'Rent'),
        row('2026-05', 90, 'debit', 'Food'),
        row('2026-06', 2000, 'credit', 'Salary'),
      ],
      months,
    );

    expect(tops.spending.map((c) => [c.label, c.total, c.average])).toEqual([
      ['Rent', 600, 200],
      ['Food', 90, 30],
    ]);
    expect(tops.income.map((c) => [c.label, c.total])).toEqual([['Salary', 2000]]);
  });

  it('nets a category over the whole window, so a payback the next month cancels', () => {
    const tops = windowTops(
      [
        row('2026-04', 120, 'debit', 'Dinner'),
        row('2026-05', 90, 'credit', 'Dinner'),
        row('2026-05', 50, 'debit', 'Food'),
      ],
      months,
    );

    expect(tops.spending.map((c) => [c.label, c.total])).toEqual([
      ['Food', 50],
      ['Dinner', 30],
    ]);
    expect(tops.income).toEqual([]);
  });

  it('gives each month its amount on the side, 0 where the month fell on the other', () => {
    const [dinner] = windowTops(
      [row('2026-04', 120, 'debit', 'Dinner'), row('2026-05', 90, 'credit', 'Dinner')],
      months,
    ).spending;

    expect(dinner.months).toEqual([
      { month: '2026-04', amount: 120, pct: 100 },
      { month: '2026-05', amount: 0, pct: 0 },
      { month: '2026-06', amount: 0, pct: 0 },
    ]);
  });

  it('keeps rows without a category gross, as Unknown on both sides', () => {
    const tops = windowTops(
      [row('2026-06', 40, 'debit', null), row('2026-06', 25, 'credit', null)],
      months,
    );

    expect(tops.spending).toMatchObject([{ label: CATEGORY_UNKNOWN, total: 40 }]);
    expect(tops.income).toMatchObject([{ label: CATEGORY_UNKNOWN, total: 25 }]);
    expect(tops.spending[0].months.map((m) => m.amount)).toEqual([0, 0, 40]);
  });

  it('keeps the top five a side and leaves out other months and uncounted rows', () => {
    const rows = ['A', 'B', 'C', 'D', 'E', 'F'].map((c, i) => row('2026-05', 10 + i, 'debit', c));
    rows.push(row('2026-03', 999, 'debit', 'Old'));
    rows.push(row('2026-05', 500, 'unknown', 'A'));
    rows.push({ ...row('2026-05', 700, 'debit', 'Moved'), isExcluded: true });

    expect(windowTops(rows, months).spending.map((c) => c.label)).toEqual([
      'F',
      'E',
      'D',
      'C',
      'B',
    ]);
  });
});
