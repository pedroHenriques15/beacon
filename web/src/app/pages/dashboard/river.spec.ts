import { describe, it, expect } from 'vitest';
import { niceScale, riverSeries, smoothPath, sparkline, RiverTransaction } from './river';

let nextId = 1;
function tx(overrides: Partial<RiverTransaction>): RiverTransaction {
  return {
    id: nextId++,
    datePosting: '2026-09-01',
    description: 'x',
    amount: 10,
    type: 'debit',
    categoryId: 1,
    category: { name: 'Food' },
    bank: 'BPI',
    ...overrides,
  };
}

describe('riverSeries', () => {
  it('adds up the month’s outflows day by day', () => {
    const s = riverSeries(
      [
        tx({ datePosting: '2026-09-01', amount: 750 }),
        tx({ datePosting: '2026-09-03', amount: 50 }),
        tx({ datePosting: '2026-09-03', amount: 25 }),
      ],
      '2026-09',
      '2026-10-04',
    );

    expect(s.current).toHaveLength(30);
    expect(s.current.slice(0, 4)).toEqual([750, 750, 825, 825]);
    expect(s.total).toBe(825);
    expect(s.outflows.map((o) => o.at)).toEqual([750, 825, 825]);
  });

  it('keeps money in, other months and unknown-type rows off the line', () => {
    const s = riverSeries(
      [
        tx({
          datePosting: '2026-09-30',
          amount: 2650,
          type: 'credit',
          categoryId: 3,
          category: { name: 'Salary' },
        }),
        tx({ datePosting: '2026-08-31', amount: 99 }),
        tx({ datePosting: '2026-09-10', amount: 40, type: 'unknown' }),
        tx({ datePosting: '2026-09-10', amount: 5 }),
      ],
      '2026-09',
      '2026-10-04',
    );

    expect(s.total).toBe(5);
    expect(s.inflows).toHaveLength(1);
    expect(s.inflows[0]).toMatchObject({ day: 30, amount: 2650 });
    expect(s.outflows).toHaveLength(1);
  });

  it('takes money paid back into a spending category off the line, on its day', () => {
    const eatingOut = { categoryId: 2, category: { name: 'Eating out' } };
    const s = riverSeries(
      [
        tx({ datePosting: '2026-09-01', amount: 40 }),
        tx({ datePosting: '2026-09-02', amount: 100, ...eatingOut }),
        tx({ datePosting: '2026-09-03', amount: 25, type: 'credit', ...eatingOut }),
        tx({ datePosting: '2026-09-03', amount: 25, type: 'credit', ...eatingOut }),
        tx({ datePosting: '2026-09-04', amount: 25, type: 'credit', ...eatingOut }),
      ],
      '2026-09',
      '2026-10-04',
    );

    expect(s.current.slice(0, 4)).toEqual([40, 140, 90, 65]);
    expect(s.total).toBe(65);
    expect(s.inflows).toHaveLength(3);
    expect(s.outflows.map((o) => o.at)).toEqual([40, 140]);
  });

  it('keeps a category that nets to income off the line, its debits too', () => {
    const salary = { categoryId: 3, category: { name: 'Salary' } };
    const s = riverSeries(
      [
        tx({ datePosting: '2026-09-25', amount: 2650, type: 'credit', ...salary }),
        tx({ datePosting: '2026-09-26', amount: 12, ...salary }),
        tx({ datePosting: '2026-09-10', amount: 5 }),
        tx({
          datePosting: '2026-09-11',
          amount: 3,
          type: 'credit',
          categoryId: null,
          category: null,
        }),
      ],
      '2026-09',
      '2026-10-04',
    );

    expect(s.total).toBe(5);
    expect(s.outflows).toHaveLength(2);
  });

  it('nets the previous month on its own', () => {
    const s = riverSeries(
      [
        tx({ datePosting: '2026-08-05', amount: 100 }),
        tx({ datePosting: '2026-08-20', amount: 60, type: 'credit' }),
      ],
      '2026-09',
      '2026-10-04',
    );

    expect(s.lastTotal).toBe(40);
    expect(Math.max(...s.last)).toBe(100);
  });

  it('draws the previous month whole for comparison', () => {
    const s = riverSeries(
      [tx({ datePosting: '2026-08-31', amount: 99 }), tx({ datePosting: '2026-08-02', amount: 1 })],
      '2026-09',
      '2026-10-04',
    );

    expect(s.previous).toBe('2026-08');
    expect(s.last).toHaveLength(31);
    expect(s.lastTotal).toBe(100);
  });

  it('flags uncategorised outflows, which are still counted', () => {
    const s = riverSeries(
      [tx({ categoryId: null, category: null, amount: 12 }), tx({ amount: 8 })],
      '2026-09',
      '2026-10-04',
    );

    expect(s.outflows.map((o) => o.needsCategory)).toEqual([true, false]);
    expect(s.total).toBe(20);
  });

  it('stops the month in progress at today', () => {
    const s = riverSeries(
      [tx({ datePosting: '2026-10-02', amount: 5 }), tx({ datePosting: '2026-10-20', amount: 7 })],
      '2026-10',
      '2026-10-04',
    );

    expect(s.current).toEqual([0, 5, 5, 5]);
    expect(s.outflows).toHaveLength(1);
  });

  it('crosses the year boundary for the previous month', () => {
    const s = riverSeries([tx({ datePosting: '2025-12-15', amount: 3 })], '2026-01', '2026-03-01');

    expect(s.previous).toBe('2025-12');
    expect(s.lastTotal).toBe(3);
  });
});

describe('niceScale', () => {
  it('rounds the top up to a step of 1, 2, 2.5 or 5 times a power of ten', () => {
    expect(niceScale(2104.8)).toEqual({ top: 2500, step: 500 });
    expect(niceScale(90)).toEqual({ top: 100, step: 20 });
    expect(niceScale(0)).toEqual({ top: 100, step: 25 });
  });
});

describe('smoothPath', () => {
  it('starts at the first point and ends at the last', () => {
    const path = smoothPath([
      [0, 10],
      [5, 5],
      [10, 0],
    ]);

    expect(path.startsWith('M0.0 10.0')).toBe(true);
    expect(path.endsWith('10.0 0.0')).toBe(true);
  });

  it('is empty without points', () => {
    expect(smoothPath([])).toBe('');
  });
});

describe('sparkline', () => {
  it('needs two values', () => {
    expect(sparkline([1], 100, 40)).toBeNull();
  });

  it('puts the highest value at the top of the box', () => {
    const s = sparkline([1, 3], 100, 40)!;
    expect(s.endY).toBe(4);
  });
});
