import { describe, it, expect } from 'vitest';
import { AssetMetric } from '../../core/services/investments.service';
import { InvestmentAsset, InvestmentLot } from '../../core/models/statement.model';
import {
  allocationShares,
  assetColors,
  axisDate,
  dayValue,
  timeTick,
  eurPrice,
  eurTick,
  historyCaption,
  portfolioSummary,
  recentActivity,
  shortDate,
  signedPct,
  spanDays,
  weekdayDate,
} from './investments-view';

let nextId = 1;

function asset(overrides: Partial<InvestmentAsset> = {}): InvestmentAsset {
  return {
    id: nextId++,
    assetType: 'ETF',
    ticker: 'VWCE.DE',
    isin: null,
    name: 'All-World',
    notes: null,
    pricesSyncedAt: null,
    priceSyncError: null,
    priceCount: 0,
    lots: [],
    priceSnapshots: [],
    ...overrides,
  };
}

function lot(overrides: Partial<InvestmentLot> = {}): InvestmentLot {
  return {
    id: nextId++,
    assetId: 1,
    date: '2026-01-05',
    quantity: 10,
    pricePerUnit: 10,
    fees: null,
    notes: null,
    ...overrides,
  };
}

function metric(overrides: Partial<AssetMetric> = {}): AssetMetric {
  return {
    asset: asset(),
    totalQuantity: 10,
    avgCostPerUnit: 100,
    netCostBasis: 1000,
    currentPrice: 120,
    currentValue: 1200,
    unrealizedPnl: 200,
    unrealizedPct: 20,
    realizedPnl: 0,
    totalReturn: 200,
    latestPriceDate: '2026-07-22',
    pricesStale: false,
    change1d: null,
    change1w: null,
    change1m: null,
    changeSincePurchase: null,
    ...overrides,
  };
}

describe('portfolioSummary', () => {
  it('adds up value, cost and realised, and the unrealised share', () => {
    const s = portfolioSummary([
      metric({ currentValue: 1200, netCostBasis: 1000, realizedPnl: 15 }),
      metric({ currentValue: 300, netCostBasis: 500, realizedPnl: -5 }),
    ]);

    expect(s.value).toBe(1500);
    expect(s.cost).toBe(1500);
    expect(s.unrealised).toBe(0);
    expect(s.unrealisedPct).toBe(0);
    expect(s.realised).toBe(10);
  });

  it('counts an asset without a price as no value and leaves the share out without cost', () => {
    const s = portfolioSummary([
      metric({ currentPrice: null, currentValue: null, netCostBasis: 0, totalQuantity: 0 }),
    ]);

    expect(s.value).toBe(0);
    expect(s.unrealisedPct).toBeNull();
    expect(s.change1d).toBeNull();
  });

  it('weights each asset’s change by its value', () => {
    // A: 10 × 110 now, 10 × 100 before (+10%). B: 1 × 90 now, 1 × 100 before (−10%).
    const s = portfolioSummary([
      metric({ totalQuantity: 10, currentPrice: 110, currentValue: 1100, change1d: 10 }),
      metric({ totalQuantity: 1, currentPrice: 90, currentValue: 90, change1d: -10 }),
    ]);

    // (1100 + 90 − 1000 − 100) / (1000 + 100)
    expect(s.change1d).toBeCloseTo((90 / 1100) * 100, 10);
  });

  it('leaves out assets with no change for the period, or not held', () => {
    const s = portfolioSummary([
      metric({ totalQuantity: 10, currentPrice: 110, change1w: 10 }),
      metric({ totalQuantity: 5, currentPrice: 50, change1w: null }),
      metric({ totalQuantity: 0, currentPrice: 50, change1w: 50 }),
    ]);

    expect(s.change1w).toBeCloseTo(10, 10);
    expect(s.change1m).toBeNull();
  });
});

describe('signedPct', () => {
  it('signs gains with a plus and losses with a true minus', () => {
    expect(signedPct(2.871)).toBe('+2.87%');
    expect(signedPct(-0.3)).toBe('−0.30%');
    expect(signedPct(17.04, 1)).toBe('+17.0%');
  });

  it('shows zero without a sign and an unknown change as a dash', () => {
    expect(signedPct(0)).toBe('0.00%');
    expect(signedPct(-0.001)).toBe('0.00%');
    expect(signedPct(null)).toBe('—');
  });
});

describe('formatting', () => {
  it('shows unit prices with two to four decimals', () => {
    expect(eurPrice(138.42)).toBe('€138.42');
    expect(eurPrice(34.10357)).toBe('€34.1036');
    expect(eurPrice(1234.5)).toBe('€1,234.50');
  });

  it('labels axis ticks without merging close ones', () => {
    expect(eurTick(6540)).toBe('€6,540');
    expect(eurTick(137.5)).toBe('€137.5');
    expect(eurTick(0)).toBe('€0');
  });

  it('formats dates the same in every time zone', () => {
    expect(shortDate('2026-01-05')).toBe('5 Jan 2026');
    expect(axisDate('2026-03-15', false)).toBe('15 Mar');
    expect(axisDate('2026-03-15', true)).toBe('Mar 2026');
    expect(axisDate('2026-09-15', true)).toBe('Sep 2026');
    expect(shortDate('2026-09-02')).toBe('2 Sep 2026');
  });

  it('adds the year to a weekday date only when it is not this year', () => {
    expect(weekdayDate('2026-10-02', '2026-10-05')).toBe('Fri 2 Oct');
    expect(weekdayDate('2025-10-03', '2026-10-05')).toBe('Fri 3 Oct 2025');
  });

  it('counts the days a series spans', () => {
    expect(spanDays([{ date: '2026-01-01' }, { date: '2026-03-02' }])).toBe(60);
    expect(spanDays([{ date: '2026-01-01' }])).toBe(0);
  });
});

describe('historyCaption', () => {
  it('splits the change into money put in and price moves', () => {
    const caption = historyCaption([
      { date: '2026-01-05', totalValue: 1000, invested: 900 },
      { date: '2026-02-01', totalValue: 1200, invested: 1100 },
      { date: '2026-03-10', totalValue: 1500, invested: 1300 },
    ]);

    expect(caption).toEqual({
      title: 'Up €500 since 5 Jan 2026',
      detail: '€400 put in, +€100 from price moves',
    });
  });

  it('says when money was taken out and prices fell', () => {
    const caption = historyCaption([
      { date: '2026-01-05', totalValue: 1000, invested: 1000 },
      { date: '2026-03-10', totalValue: 650, invested: 700 },
    ]);

    expect(caption).toEqual({
      title: 'Down €350 since 5 Jan 2026',
      detail: '€300 taken out, −€50 from price moves',
    });
  });

  it('leaves out the money part when none moved, and needs two points', () => {
    expect(
      historyCaption([
        { date: '2026-01-05', totalValue: 1000, invested: 1000 },
        { date: '2026-03-10', totalValue: 1000, invested: 1000 },
      ]),
    ).toEqual({ title: 'No change since 5 Jan 2026', detail: '€0 from price moves' });
    expect(historyCaption([{ date: '2026-01-05', totalValue: 1, invested: 1 }])).toBeNull();
  });
});

describe('allocation', () => {
  it('gives each slice its share of the total', () => {
    const slices = allocationShares([
      { label: 'A', value: 300, color: 'a' },
      { label: 'B', value: 100, color: 'b' },
    ]);

    expect(slices.map((s) => s.pct)).toEqual([75, 25]);
  });

  it('maps the allocation colours back to the assets that have a value', () => {
    const a = metric({ currentValue: 100 });
    const empty = metric({ currentValue: null });
    const b = metric({ currentValue: 50 });

    const colors = assetColors([a, empty, b], [{ color: 'red' }, { color: 'blue' }]);

    expect(colors.get(a.asset.id)).toBe('red');
    expect(colors.get(b.asset.id)).toBe('blue');
    expect(colors.has(empty.asset.id)).toBe(false);
  });
});

describe('recentActivity', () => {
  it('lists buys with their cost and sells with their realised result, newest first', () => {
    const buy = lot({ date: '2026-01-05', quantity: 10, pricePerUnit: 10, fees: 1 });
    const sell = lot({ date: '2026-02-05', quantity: -4, pricePerUnit: 12, fees: 0.5 });
    const etf = asset({ lots: [sell, buy] });

    const items = recentActivity([etf], 10);

    expect(items.map((i) => i.kind)).toEqual(['sell', 'buy']);
    expect(items[1].amount).toBeCloseTo(101, 10);
    // Average cost 10.10: 4 × 12 − 4 × 10.10 − 0.50
    expect(items[0].amount).toBeCloseTo(7.1, 10);
    expect(items[0].asset).toBe(etf);
  });

  it('merges the assets and keeps the latest ones', () => {
    const first = asset({ lots: [lot({ date: '2026-01-01' }), lot({ date: '2026-03-01' })] });
    const second = asset({ lots: [lot({ date: '2026-02-01' })] });

    const items = recentActivity([first, second], 2);

    expect(items.map((i) => i.lot.date)).toEqual(['2026-03-01', '2026-02-01']);
  });
});

describe('timeTick', () => {
  it('labels a linear time axis and blanks repeats', () => {
    const ticks = ['2026-09-01', '2026-09-20', '2026-10-10'].map((d) => ({ value: dayValue(d) }));
    expect(timeTick(ticks, 0, true)).toBe('Sep 2026');
    expect(timeTick(ticks, 1, true)).toBe('');
    expect(timeTick(ticks, 2, true)).toBe('Oct 2026');
  });
});
