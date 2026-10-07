import { describe, it, expect } from 'vitest';
import { CATEGORY_UNKNOWN } from '../constants/categories';
import { NetRow, categoryNet, incomeByCategory, spendingByCategory } from './category-net';

function row(
  amount: number,
  type: NetRow['type'],
  category: string | null,
  overrides: Partial<NetRow> = {},
): NetRow {
  return {
    amount,
    type,
    category: category ? { name: category, color: '#e0806b' } : null,
    ...overrides,
  };
}

/** A group dinner of 100 and three friends paying back 25 each. */
const dinner = [
  row(100, 'debit', 'Eating out'),
  row(25, 'credit', 'Eating out'),
  row(25, 'credit', 'Eating out'),
  row(25, 'credit', 'Eating out'),
];

describe('categoryNet', () => {
  it('nets a dinner paid back by friends to what it cost, with nothing as income', () => {
    const totals = categoryNet(dinner);

    expect(totals.spending).toBe(25);
    expect(totals.income).toBe(0);
    expect(totals.net).toBe(-25);
    expect(totals.categories).toEqual([
      { label: 'Eating out', color: '#e0806b', net: -25, received: 75, spent: 100 },
    ]);
    expect(spendingByCategory(totals)).toEqual([
      { label: 'Eating out', color: '#e0806b', total: 25 },
    ]);
    expect(incomeByCategory(totals)).toEqual([]);
  });

  it('counts a category as income when more came back than went out', () => {
    const totals = categoryNet([row(40, 'debit', 'Gifts'), row(100, 'credit', 'Gifts')]);

    expect(totals.income).toBe(60);
    expect(totals.spending).toBe(0);
    expect(incomeByCategory(totals)).toEqual([{ label: 'Gifts', color: '#e0806b', total: 60 }]);
    expect(spendingByCategory(totals, true)).toEqual([]);
  });

  it('keeps rows without a category gross by type', () => {
    const totals = categoryNet([row(30, 'debit', null), row(10, 'credit', null)]);

    expect(totals.spending).toBe(30);
    expect(totals.income).toBe(10);
    expect(totals.uncategorised).toEqual({ income: 10, spending: 30 });
    expect(spendingByCategory(totals)).toEqual([
      { label: CATEGORY_UNKNOWN, color: 'var(--category-fallback)', total: 30 },
    ]);
    expect(incomeByCategory(totals)[0]).toMatchObject({ label: CATEGORY_UNKNOWN, total: 10 });
  });

  it('nets a category across banks', () => {
    const rows: (NetRow & { bank: string })[] = [
      { ...row(100, 'debit', 'Eating out'), bank: 'BPI' },
      { ...row(75, 'credit', 'Eating out'), bank: 'REVOLUT' },
    ];

    const totals = categoryNet(rows);

    expect(totals.spending).toBe(25);
    expect(totals.income).toBe(0);
  });

  it('leaves out excluded rows and rows of an unclassified type', () => {
    const totals = categoryNet([
      row(100, 'debit', 'Eating out'),
      row(100, 'credit', 'Eating out', { isExcluded: true }),
      row(500, 'credit', 'Excluded'),
      row(9, 'unknown', 'Eating out'),
    ]);

    expect(totals.spending).toBe(100);
    expect(totals.income).toBe(0);
  });

  it('keeps income − spending equal to credits less debits', () => {
    const rows = [
      ...dinner,
      row(2650, 'credit', 'Salary'),
      row(12, 'debit', 'Salary'),
      row(800, 'debit', 'Rent'),
      row(42.18, 'debit', null),
      row(5, 'credit', null),
      row(60, 'debit', 'Gifts'),
      row(60, 'credit', 'Gifts'),
    ];
    const gross = rows.reduce((s, r) => s + (r.type === 'credit' ? r.amount : -r.amount), 0);

    const totals = categoryNet(rows);

    expect(totals.income - totals.spending).toBeCloseTo(gross, 10);
    expect(totals.income).toBeCloseTo(2638 + 5, 10);
    expect(totals.spending).toBeCloseTo(25 + 800 + 42.18, 10);
  });

  it('puts a category paid back in full at zero, last, only when asked', () => {
    const totals = categoryNet([
      row(33.33, 'debit', 'Gifts'),
      row(11.11, 'credit', 'Gifts'),
      row(22.22, 'credit', 'Gifts'),
      row(5, 'debit', 'Rent'),
    ]);

    expect(totals.categories.find((c) => c.label === 'Gifts')?.net).toBe(0);
    expect(spendingByCategory(totals).map((c) => c.label)).toEqual(['Rent']);
    expect(spendingByCategory(totals, true)).toEqual([
      { label: 'Rent', color: '#e0806b', total: 5 },
      { label: 'Gifts', color: '#e0806b', total: 0 },
    ]);
    expect(incomeByCategory(totals)).toEqual([]);
  });

  it('gives a category without a colour the fallback token', () => {
    const totals = categoryNet([{ amount: 5, type: 'debit', category: { name: 'Misc' } }]);

    expect(totals.categories[0].color).toBe('var(--category-fallback)');
  });
});
