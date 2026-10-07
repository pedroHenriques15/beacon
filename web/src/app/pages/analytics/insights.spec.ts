import { describe, it, expect } from 'vitest';
import { CATEGORY_UNKNOWN } from '../../core/constants/categories';
import {
  CategoryTotal,
  biggestMoves,
  categoryBars,
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
  it('gives each category its share, bar length and the previous month', () => {
    const bars = categoryBars(
      [cat('Rent', 750), cat('Food', 250)],
      [cat('Rent', 750), cat('Food', 200)],
    );
    expect(bars.total).toBe(1000);
    const [rent, food] = bars.rows;
    expect(rent.share).toBeCloseTo(75);
    expect(food.share).toBeCloseTo(25);
    expect(food.previous).toBe(200);
    expect(food.change).toBeCloseTo(50);
    expect(food.changePct).toBeCloseTo(25);
    expect(rent.change).toBe(0);
    expect(rent.changePct).toBe(0);
  });

  it('scales bars and ticks to a round top above the largest amount of either month', () => {
    const bars = categoryBars([cat('Rent', 750)], [cat('Rent', 780)]);
    expect(bars.ticks).toEqual([0, 200, 400, 600, 800]);
    expect(bars.rows[0].pct).toBeCloseTo(93.75);
    expect(bars.rows[0].prevPct).toBeCloseTo(97.5);
  });

  it('keeps a category that had spending last month and none now, at zero, after the rest', () => {
    const bars = categoryBars([cat('Food', 50)], [cat('Shopping', 400), cat('Food', 60)]);
    expect(bars.rows.map((r) => [r.label, r.total, r.previous])).toEqual([
      ['Food', 50, 60],
      ['Shopping', 0, 400],
    ]);
    expect(bars.rows[1].share).toBe(0);
    expect(bars.rows[1].changePct).toBeCloseTo(-100);
  });

  it('marks a category new to this month with no percentage', () => {
    const bars = categoryBars([cat('Health', 40)], [cat('Food', 60)]);
    const health = bars.rows.find((r) => r.label === 'Health')!;
    expect(health.previous).toBe(0);
    expect(health.change).toBe(40);
    expect(health.changePct).toBeNull();
  });

  it('compares nothing without a previous month', () => {
    const bars = categoryBars([cat('Food', 50)], null);
    expect(bars.rows[0].previous).toBeNull();
    expect(bars.rows[0].prevPct).toBeNull();
    expect(bars.rows[0].change).toBeNull();
    expect(bars.rows[0].changePct).toBeNull();
  });

  it('handles an empty period', () => {
    const bars = categoryBars([], null);
    expect(bars.rows).toEqual([]);
    expect(bars.total).toBe(0);
    expect(bars.ticks[0]).toBe(0);
  });
});

describe('biggestMoves', () => {
  it('lists the largest changes first, either way, up to the limit', () => {
    const moves = biggestMoves(
      [cat('Shopping', 184.2), cat('Dining', 148.6), cat('Health', 64.5), cat('Rent', 750)],
      [cat('Shopping', 402.35), cat('Dining', 196.4), cat('Health', 22), cat('Rent', 750)],
    );
    expect(moves.map((m) => m.label)).toEqual(['Shopping', 'Dining', 'Health']);
    expect(moves[0].change).toBeCloseTo(-218.15);
    expect(moves[0].changePct).toBeCloseTo(-54.22, 1);
    expect(moves[2].change).toBeCloseTo(42.5);
  });

  it('leaves out categories that did not move', () => {
    expect(biggestMoves([cat('Rent', 750)], [cat('Rent', 750)])).toEqual([]);
  });

  it('counts a new category and one that stopped', () => {
    const moves = biggestMoves([cat('Health', 40)], [cat('Shopping', 100)], 5);
    expect(moves).toEqual([
      {
        label: 'Shopping',
        color: '#123456',
        total: 0,
        previous: 100,
        change: -100,
        changePct: -100,
      },
      { label: 'Health', color: '#123456', total: 40, previous: 0, change: 40, changePct: null },
    ]);
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
