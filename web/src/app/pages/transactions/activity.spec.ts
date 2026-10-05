import { describe, it, expect } from 'vitest';
import {
  ActivityItem,
  ActivityTx,
  TxFilter,
  countedAmount,
  dayGroups,
  flowTotals,
  isExcludedTx,
  matchesItemFilter,
  matchesTxFilter,
  mostBought,
  popoverPosition,
  storeTotals,
  totalsByBank,
  txAmountText,
  withMonths,
} from './activity';

let nextId = 1;
function tx(overrides: Partial<ActivityTx> = {}): ActivityTx {
  return {
    id: nextId++,
    datePosting: '2026-09-10',
    description: 'PINGO DOCE',
    amount: 10,
    type: 'debit',
    categoryId: 1,
    isExcluded: false,
    category: { name: 'Groceries' },
    bank: 'BPI',
    ...overrides,
  };
}

function item(overrides: Partial<ActivityItem> = {}): ActivityItem {
  return {
    id: nextId++,
    receiptId: 1,
    storeName: 'Continente',
    receiptDate: '2026-09-10',
    description: 'Leite',
    amount: 1,
    quantity: 1,
    categoryId: 1,
    categoryName: 'Dairy',
    isExcluded: false,
    ...overrides,
  };
}

const noFilter: TxFilter = { bank: '', month: '', type: '', category: '', search: '' };

describe('matchesTxFilter', () => {
  it('matches every row when no filter is set', () => {
    expect(matchesTxFilter(tx(), noFilter)).toBe(true);
  });

  it('filters by bank (any case), month, type and search text', () => {
    const row = tx({ bank: 'ACTIVOBANK', datePosting: '2026-08-31', description: 'MB WAY Ana' });

    expect(matchesTxFilter(row, { ...noFilter, bank: 'activobank' })).toBe(true);
    expect(matchesTxFilter(row, { ...noFilter, bank: 'BPI' })).toBe(false);
    expect(matchesTxFilter(row, { ...noFilter, month: '2026-08' })).toBe(true);
    expect(matchesTxFilter(row, { ...noFilter, month: '2026-09' })).toBe(false);
    expect(matchesTxFilter(row, { ...noFilter, type: 'credit' })).toBe(false);
    expect(matchesTxFilter(row, { ...noFilter, search: 'mb way' })).toBe(true);
    expect(matchesTxFilter(row, { ...noFilter, search: 'lidl' })).toBe(false);
  });

  it("treats category 'unknown' as rows without a category", () => {
    expect(matchesTxFilter(tx({ categoryId: null }), { ...noFilter, category: 'unknown' })).toBe(
      true,
    );
    expect(matchesTxFilter(tx({ categoryId: 3 }), { ...noFilter, category: 'unknown' })).toBe(
      false,
    );
    expect(matchesTxFilter(tx({ categoryId: 3 }), { ...noFilter, category: '3' })).toBe(true);
    expect(matchesTxFilter(tx({ categoryId: 4 }), { ...noFilter, category: '3' })).toBe(false);
  });
});

describe('matchesItemFilter', () => {
  it('matches the store by part of its name, and the month of the receipt', () => {
    const row = item({ storeName: 'Pingo Doce Benfica', receiptDate: '2026-09-02' });
    const none = { store: '', month: '', category: '', search: '' };

    expect(matchesItemFilter(row, { ...none, store: 'pingo doce' })).toBe(true);
    expect(matchesItemFilter(row, { ...none, store: 'Lidl' })).toBe(false);
    expect(matchesItemFilter(row, { ...none, month: '2026-09' })).toBe(true);
    expect(matchesItemFilter(item({ categoryId: null }), { ...none, category: 'unknown' })).toBe(
      true,
    );
  });
});

describe('flowTotals', () => {
  it('counts money in and out, and rows without a category', () => {
    const totals = flowTotals([
      tx({ type: 'credit', amount: 2650 }),
      tx({ amount: 42.5 }),
      tx({ amount: 7.5, categoryId: null, category: null }),
    ]);

    expect(totals).toEqual({ income: 2650, expenses: 50, net: 2600, count: 3 });
  });

  it('leaves out excluded rows and rows of an unclassified type', () => {
    const totals = flowTotals([
      tx({ amount: 400, isExcluded: true }),
      tx({ amount: 300, category: { name: 'Excluded' } }),
      tx({ amount: 12, type: 'unknown' }),
      tx({ amount: 5 }),
    ]);

    expect(totals).toEqual({ income: 0, expenses: 5, net: -5, count: 2 });
  });
});

describe('totalsByBank', () => {
  it('sums each bank, the most money out first', () => {
    const banks = totalsByBank([
      tx({ bank: 'BPI', amount: 10 }),
      tx({ bank: 'REVOLUT', amount: 30 }),
      tx({ bank: 'BPI', amount: 5, type: 'credit' }),
      tx({ bank: 'CGD', amount: 99, isExcluded: true }),
    ]);

    expect(banks.map((b) => [b.bank, b.expenses, b.income, b.count])).toEqual([
      ['REVOLUT', 30, 0, 1],
      ['BPI', 10, 5, 2],
    ]);
  });
});

describe('countedAmount and txAmountText', () => {
  it('signs money in and out, and counts nothing for left-out rows', () => {
    expect(countedAmount(tx({ type: 'credit', amount: 5 }))).toBe(5);
    expect(countedAmount(tx({ amount: 5 }))).toBe(-5);
    expect(countedAmount(tx({ type: 'unknown' }))).toBeNull();
    expect(countedAmount(tx({ isExcluded: true }))).toBeNull();
  });

  it('writes money out with a true minus sign and money in with a plus', () => {
    expect(txAmountText({ type: 'debit', amount: 1862.45 })).toBe('−€1,862.45');
    expect(txAmountText({ type: 'credit', amount: 2980 })).toBe('+€2,980.00');
    expect(txAmountText({ type: 'unknown', amount: 12 })).toBe('€12.00');
  });
});

describe('isExcludedTx', () => {
  it('reads the flag and the Excluded category', () => {
    expect(isExcludedTx({ isExcluded: true, category: null })).toBe(true);
    expect(isExcludedTx({ isExcluded: false, category: { name: 'Excluded' } })).toBe(true);
    expect(isExcludedTx({ isExcluded: false, category: { name: 'Food' } })).toBe(false);
  });
});

describe('dayGroups', () => {
  const dateOf = (r: ActivityTx) => r.datePosting;

  it('groups runs of the same day and totals the counted rows', () => {
    const rows = [
      tx({ datePosting: '2026-09-30', type: 'credit', amount: 100 }),
      tx({ datePosting: '2026-09-30', amount: 40 }),
      tx({ datePosting: '2026-09-21', amount: 400, isExcluded: true }),
      tx({ datePosting: '2026-09-20', amount: 5, categoryId: null, category: null }),
    ];

    const groups = dayGroups(rows, true, dateOf, countedAmount);

    expect(groups.map((g) => [g.date, g.rows.length, g.total, g.counted])).toEqual([
      ['2026-09-30', 2, 60, 2],
      ['2026-09-21', 1, 0, 0],
      ['2026-09-20', 1, -5, 1],
    ]);
  });

  it('keeps one undated group when the list is not in day order', () => {
    const groups = dayGroups(
      [tx(), tx({ datePosting: '2026-01-01' })],
      false,
      dateOf,
      countedAmount,
    );

    expect(groups).toHaveLength(1);
    expect(groups[0].date).toBeNull();
    expect(groups[0].rows).toHaveLength(2);
  });

  it('keeps group keys unique if a day comes back', () => {
    const rows = [
      tx({ datePosting: '2026-09-02' }),
      tx({ datePosting: '2026-09-01' }),
      tx({ datePosting: '2026-09-02' }),
    ];

    const keys = dayGroups(rows, true, dateOf, countedAmount).map((g) => g.key);

    expect(new Set(keys).size).toBe(3);
  });
});

describe('storeTotals and mostBought', () => {
  it('sums counted items by store, with receipts and bar lengths', () => {
    const stores = storeTotals([
      item({ storeName: 'Lidl', amount: 10, receiptId: 1 }),
      item({ storeName: 'Lidl', amount: 10, receiptId: 2 }),
      item({ storeName: 'Continente', amount: 5, receiptId: 3 }),
      item({ storeName: 'Continente', amount: 50, receiptId: 3, isExcluded: true }),
    ]);

    expect(stores).toEqual([
      { store: 'Lidl', amount: 20, receipts: 2, pct: 100 },
      { store: 'Continente', amount: 5, receipts: 1, pct: 25 },
    ]);
  });

  it('ranks counted items by money spent, summed by description', () => {
    const top = mostBought(
      [
        item({ description: 'Leite', amount: 1, quantity: 2 }),
        item({ description: 'Leite ', amount: 1, quantity: 1 }),
        item({ description: 'Café', amount: 3 }),
        item({ description: 'Saco', amount: 9, categoryName: 'Excluded' }),
      ],
      2,
    );

    expect(top).toEqual([
      { description: 'Café', quantity: 1, amount: 3 },
      { description: 'Leite', quantity: 3, amount: 2 },
    ]);
  });
});

describe('withMonths', () => {
  it('adds the missing months as empty cells, oldest first', () => {
    const cells = withMonths(
      [
        { key: '2026-07', income: 1, expenses: 2 },
        { key: '2026-09', income: 3, expenses: 4 },
      ],
      ['2026-08', '2026-09', '', 'nonsense'],
    );

    expect(cells.map((c) => c.key)).toEqual(['2026-07', '2026-08', '2026-09']);
    expect(cells[1]).toEqual({ key: '2026-08', income: 0, expenses: 0 });
  });
});

describe('popoverPosition', () => {
  const viewport = { width: 1000, height: 800 };
  const size = { width: 280, height: 400 };

  it('opens under the anchor when it fits', () => {
    const pos = popoverPosition({ top: 100, bottom: 140, left: 300, right: 400 }, size, viewport);

    expect(pos).toEqual({ top: 144, left: 300 });
  });

  it('opens above when there is no room below, and stays on screen', () => {
    const pos = popoverPosition({ top: 700, bottom: 740, left: 900, right: 990 }, size, viewport);

    expect(pos).toEqual({ top: 296, left: 712 });
  });

  it('can line up with the anchor’s right edge', () => {
    const pos = popoverPosition(
      { top: 100, bottom: 144, left: 500, right: 544 },
      { width: 220, height: 200 },
      viewport,
      true,
    );

    expect(pos.left).toBe(324);
  });
});
