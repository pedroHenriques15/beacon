import { describe, it, expect } from 'vitest';
import { CATEGORY_UNKNOWN } from '../../core/constants/categories';
import { categoryNet } from '../../core/utils/category-net';
import {
  CategoryTotal,
  categoryBars,
  categoryLines,
  categoryMonths,
  compareText,
  flowWindow,
  mostBought,
  signedPct,
  storeTotals,
  sumByCategory,
  withMonths,
} from './insights';

function cat(label: string, total: number, color = '#123456'): CategoryTotal {
  return { label, color, total };
}

describe('sumByCategory', () => {
  it('adds up each category, largest first, keeping the first colour seen', () => {
    const rows = sumByCategory([
      { label: 'Food', color: '#aaa', amount: 10 },
      { label: 'Rent', color: '#bbb', amount: 700 },
      { label: 'Food', color: '#ccc', amount: 5 },
    ]);
    expect(rows).toEqual([cat('Rent', 700, '#bbb'), cat('Food', 15, '#aaa')]);
  });

  it('is empty without rows', () => {
    expect(sumByCategory([])).toEqual([]);
  });
});

describe('categoryBars', () => {
  it('gives each category its share and bar length, in the order given', () => {
    const bars = categoryBars([cat('Rent', 750), cat('Food', 250)]);
    const [rent, food] = bars.rows;
    expect(rent.share).toBeCloseTo(75);
    expect(food.share).toBeCloseTo(25);
    expect(bars.ticks).toEqual([0, 200, 400, 600, 800]);
    expect(rent.pct).toBeCloseTo(93.75);
  });

  it('shares each row out of its own side when rows have one', () => {
    const bars = categoryBars([
      { ...cat('Salary', 2000), side: 'in' },
      { ...cat('Rent', 750), side: 'out' },
      { ...cat('Food', 250), side: 'out' },
      { ...cat('Gifts', 500), side: 'in' },
    ]);
    expect(bars.rows.map((r) => [r.label, r.share])).toEqual([
      ['Salary', 80],
      ['Rent', 75],
      ['Food', 25],
      ['Gifts', 20],
    ]);
    expect(bars.rows[0].pct).toBeCloseTo(100);
  });

  it('handles an empty period', () => {
    const bars = categoryBars([]);
    expect(bars.rows).toEqual([]);
    expect(bars.ticks[0]).toBe(0);
  });
});

describe('categoryLines', () => {
  type Type = 'credit' | 'debit';
  const tx = (name: string | null, amount: number, type: Type = 'debit') => ({
    amount,
    type,
    category: name ? { name, color: '#123456' } : null,
  });
  const totals = categoryNet([
    tx('Salary', 2000, 'credit'),
    tx('Rent', 700),
    tx('Dinner', 100),
    tx('Dinner', 75, 'credit'),
    tx('Gifts', 40),
    tx('Gifts', 40, 'credit'),
    tx(null, 30),
    tx(null, 10, 'credit'),
  ]);

  it('lists both sides by amount, each row with its side', () => {
    expect(categoryLines(totals, 'all').map((r) => [r.label, r.side, r.total])).toEqual([
      ['Salary', 'in', 2000],
      ['Rent', 'out', 700],
      [CATEGORY_UNKNOWN, 'out', 30],
      ['Dinner', 'out', 25],
      [CATEGORY_UNKNOWN, 'in', 10],
      ['Gifts', 'out', 0],
    ]);
  });

  it('lists one side alone, the category paid back in full last on the spending side', () => {
    expect(categoryLines(totals, 'in').map((r) => r.label)).toEqual(['Salary', CATEGORY_UNKNOWN]);
    expect(categoryLines(totals, 'out').map((r) => r.label)).toEqual([
      'Rent',
      CATEGORY_UNKNOWN,
      'Dinner',
      'Gifts',
    ]);
  });

  it('says what came in and went out only for a category that had both', () => {
    const lines = categoryLines(totals, 'all');
    const detail = (label: string, side: string) =>
      lines.find((r) => r.label === label && r.side === side)?.detail;
    expect(detail('Dinner', 'out')).toBe('€100.00 out, €75.00 in');
    expect(detail('Gifts', 'out')).toBe('€40.00 out, €40.00 in');
    expect(detail('Rent', 'out')).toBeNull();
    expect(detail(CATEGORY_UNKNOWN, 'out')).toBeNull();
  });

  it('divides every figure for an average month', () => {
    const [salary] = categoryLines(totals, 'in', 2);
    expect(salary.total).toBe(1000);
    const dinner = categoryLines(totals, 'out', 2).find((r) => r.label === 'Dinner')!;
    expect(dinner.total).toBe(12.5);
    expect(dinner.detail).toBe('€50.00 out, €37.50 in');
  });
});

describe('categoryMonths', () => {
  const row = (
    month: string,
    amount: number,
    type: 'credit' | 'debit' | 'unknown',
    name: string | null,
  ) => ({ month, amount, type, category: name ? { name } : null });

  it('nets the category each month on its own, keeping what came in and went out', () => {
    const months = categoryMonths(
      [
        row('2026-09', 100, 'debit', 'Eating out'),
        row('2026-09', 75, 'credit', 'Eating out'),
        row('2026-08', 40, 'debit', 'Eating out'),
        row('2026-08', 90, 'credit', 'Eating out'),
        row('2026-09', 500, 'debit', 'Rent'),
        row('2026-07', 8, 'unknown', 'Eating out'),
      ],
      'Eating out',
    );

    expect(months).toEqual([
      { month: '2026-08', net: 50, received: 90, spent: 40 },
      { month: '2026-09', net: -25, received: 75, spent: 100 },
    ]);
  });

  it('gives the rows without a category under Unknown, kept apart', () => {
    const months = categoryMonths(
      [row('2026-09', 30, 'debit', null), row('2026-09', 10, 'credit', null)],
      CATEGORY_UNKNOWN,
    );

    expect(months).toEqual([{ month: '2026-09', net: -20, received: 10, spent: 30 }]);
  });
});

describe('flowWindow', () => {
  const totals = ['2026-09', '2026-08', '2026-07', '2026-06'].map((month) => ({
    month,
    income: 1,
    expenses: 1,
    net: 0,
  }));

  it('takes the months up to the selected one, oldest first', () => {
    expect(flowWindow(totals, '2026-08', 2).map((t) => t.month)).toEqual(['2026-07', '2026-08']);
  });

  it('takes the latest months with all months selected', () => {
    expect(flowWindow(totals, '', 3).map((t) => t.month)).toEqual([
      '2026-07',
      '2026-08',
      '2026-09',
    ]);
  });
});

describe('withMonths', () => {
  const cells = [
    { key: '2026-07', income: 10, expenses: 5 },
    { key: '2026-09', income: 10, expenses: 5 },
  ];

  it('adds an empty cell for each missing month, in order', () => {
    expect(withMonths(cells, ['2026-08', '2026-09', '2026-08']).map((c) => c.key)).toEqual([
      '2026-07',
      '2026-08',
      '2026-09',
    ]);
    expect(withMonths(cells, ['2026-08'])[1]).toEqual({ key: '2026-08', income: 0, expenses: 0 });
  });

  it('returns the cells as they are when nothing is missing', () => {
    expect(withMonths(cells, ['2026-07'])).toBe(cells);
  });
});

describe('signedPct', () => {
  it('signs with a true minus', () => {
    expect(signedPct(4.8)).toBe('+4.8%');
    expect(signedPct(-54.21)).toBe('−54.2%');
    expect(signedPct(0.01)).toBe('0%');
  });
});

describe('compareText', () => {
  it('says how much more or less than the previous month', () => {
    expect(compareText(1862.45, 2104.8, 'August')).toBe('11.5% less than August');
    expect(compareText(312.4, 298.1, 'August')).toBe('4.8% more than August');
    expect(compareText(100, 100, 'August')).toBe('level with August');
  });

  it('says nothing without a previous total', () => {
    expect(compareText(100, null, 'August')).toBeNull();
    expect(compareText(100, 0, 'August')).toBeNull();
  });
});

describe('storeTotals', () => {
  it('sums each store and counts its receipts, largest first', () => {
    const stores = storeTotals([
      { storeName: 'Lidl', receiptId: 1, amount: 2, quantity: 3 },
      { storeName: 'Lidl', receiptId: 1, amount: 4, quantity: 1 },
      { storeName: 'Continente', receiptId: 2, amount: 20, quantity: 1 },
      { storeName: 'Lidl', receiptId: 3, amount: 5, quantity: 1 },
    ]);
    expect(stores).toEqual([
      { store: 'Continente', receipts: 1, total: 20, pct: 100 },
      { store: 'Lidl', receipts: 2, total: 15, pct: 75 },
    ]);
  });
});

describe('mostBought', () => {
  it('ranks items by what was spent on them, with the quantity bought', () => {
    const items = mostBought(
      [
        { description: 'Leite', amount: 0.89, quantity: 6 },
        { description: 'Café', amount: 3.49, quantity: 1 },
        { description: 'Leite', amount: 0.89, quantity: 6 },
        { description: 'Azeite', amount: 8.99, quantity: 1 },
      ],
      2,
    );
    expect(items.map((i) => i.description)).toEqual(['Leite', 'Azeite']);
    expect(items[0].quantity).toBe(12);
    expect(items[0].total).toBeCloseTo(10.68);
  });
});
