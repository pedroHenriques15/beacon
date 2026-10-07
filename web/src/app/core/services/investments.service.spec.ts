import { describe, it, expect, beforeEach } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { InvestmentsService, isPriceStale } from './investments.service';
import { InvestmentAsset, InvestmentLot, InvestmentPriceSnapshot } from '../models/statement.model';

let nextId = 1;

function makeLot(overrides: Partial<InvestmentLot> = {}): InvestmentLot {
  return {
    id: nextId++,
    assetId: 1,
    date: '2026-01-05',
    quantity: 10,
    pricePerUnit: 100,
    fees: null,
    notes: null,
    ...overrides,
  };
}

function makeSnap(overrides: Partial<InvestmentPriceSnapshot> = {}): InvestmentPriceSnapshot {
  return {
    id: nextId++,
    assetId: 1,
    date: '2026-07-22',
    pricePerUnit: 120,
    source: 'Synced',
    ...overrides,
  };
}

function makeAsset(
  lots: InvestmentLot[],
  priceSnapshots: InvestmentPriceSnapshot[],
  overrides: Partial<InvestmentAsset> = {},
): InvestmentAsset {
  return {
    id: 1,
    assetType: 'ETF',
    ticker: 'VWCE.DE',
    isin: null,
    name: 'Vanguard FTSE All-World',
    notes: null,
    pricesSyncedAt: null,
    priceSyncError: null,
    priceCount: priceSnapshots.length,
    lots,
    priceSnapshots,
    ...overrides,
  };
}

describe('InvestmentsService metrics', () => {
  let service: InvestmentsService;
  let controller: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [InvestmentsService, provideHttpClient(), provideHttpClientTesting()],
    });
    controller = TestBed.inject(HttpTestingController);
    service = TestBed.inject(InvestmentsService);
    controller.expectOne('/api/investments/assets').flush([]);
  });

  function load(assets: InvestmentAsset[]) {
    service.load();
    controller.expectOne('/api/investments/assets').flush(assets);
  }

  it('computes avg cost for a single buy including fees', () => {
    load([makeAsset([makeLot({ quantity: 10, pricePerUnit: 100, fees: 2.5 })], [])]);
    const m = service.assetMetrics()[0];
    expect(m.avgCostPerUnit).toBeCloseTo(100.25, 6);
    expect(m.netCostBasis).toBeCloseTo(1002.5, 6);
    expect(m.totalQuantity).toBe(10);
  });

  it('computes weighted average cost across two buys', () => {
    load([
      makeAsset(
        [
          makeLot({ date: '2026-01-05', quantity: 10, pricePerUnit: 100 }),
          makeLot({ date: '2026-02-05', quantity: 5, pricePerUnit: 110 }),
        ],
        [],
      ),
    ]);
    expect(service.assetMetrics()[0].avgCostPerUnit).toBeCloseTo(1550 / 15, 6);
  });

  it('returns null unrealized P&L without a snapshot', () => {
    load([makeAsset([makeLot()], [])]);
    const m = service.assetMetrics()[0];
    expect(m.currentPrice).toBeNull();
    expect(m.unrealizedPnl).toBeNull();
    expect(m.unrealizedPct).toBeNull();
  });

  it('computes unrealized P&L against the latest snapshot', () => {
    load([
      makeAsset([makeLot({ quantity: 10, pricePerUnit: 100 })], [makeSnap({ pricePerUnit: 120 })]),
    ]);
    const m = service.assetMetrics()[0];
    expect(m.unrealizedPnl).toBeCloseTo(200, 6);
    expect(m.unrealizedPct).toBeCloseTo(20, 6);
  });

  it('books realized gain against average cost and keeps basis for the rest', () => {
    load([
      makeAsset(
        [
          makeLot({ date: '2026-01-05', quantity: 10, pricePerUnit: 100 }),
          makeLot({ date: '2026-03-05', quantity: -5, pricePerUnit: 120 }),
        ],
        [],
      ),
    ]);
    const m = service.assetMetrics()[0];
    expect(m.realizedPnl).toBeCloseTo(100, 6);
    expect(m.netCostBasis).toBeCloseTo(500, 6);
    expect(m.totalQuantity).toBe(5);
  });

  it('books realized loss when selling below average cost', () => {
    load([
      makeAsset(
        [
          makeLot({ date: '2026-01-05', quantity: 10, pricePerUnit: 100 }),
          makeLot({ date: '2026-03-05', quantity: -5, pricePerUnit: 80 }),
        ],
        [],
      ),
    ]);
    expect(service.assetMetrics()[0].realizedPnl).toBeCloseTo(-100, 6);
  });

  it('subtracts sell fees from realized P&L', () => {
    load([
      makeAsset(
        [
          makeLot({ date: '2026-01-05', quantity: 10, pricePerUnit: 100 }),
          makeLot({ date: '2026-03-05', quantity: -5, pricePerUnit: 120, fees: 2 }),
        ],
        [],
      ),
    ]);
    expect(service.assetMetrics()[0].realizedPnl).toBeCloseTo(98, 6);
  });

  it('nulls unrealizedPct when the position is fully sold', () => {
    load([
      makeAsset(
        [
          makeLot({ date: '2026-01-05', quantity: 10, pricePerUnit: 100 }),
          makeLot({ date: '2026-03-05', quantity: -10, pricePerUnit: 120 }),
        ],
        [makeSnap({ pricePerUnit: 130 })],
      ),
    ]);
    const m = service.assetMetrics()[0];
    expect(m.totalQuantity).toBe(0);
    expect(m.unrealizedPnl).toBeCloseTo(0, 6);
    expect(m.unrealizedPct).toBeNull();
  });

  it('returns null 1-day change with a single snapshot', () => {
    load([makeAsset([makeLot()], [makeSnap()])]);
    expect(service.assetMetrics()[0].change1d).toBeNull();
  });

  it('computes the 1-day change from the two latest snapshots', () => {
    load([
      makeAsset(
        [makeLot()],
        [
          makeSnap({ date: '2026-07-22', pricePerUnit: 130 }),
          makeSnap({ date: '2026-07-21', pricePerUnit: 125 }),
        ],
      ),
    ]);
    expect(service.assetMetrics()[0].change1d).toBeCloseTo(4, 4);
  });

  it('computes 1w change against the nearest snapshot at or before 7 days prior', () => {
    load([
      makeAsset(
        [makeLot()],
        [
          makeSnap({ date: '2026-07-22', pricePerUnit: 120 }),
          makeSnap({ date: '2026-07-14', pricePerUnit: 100 }),
        ],
      ),
    ]);
    expect(service.assetMetrics()[0].change1w).toBeCloseTo(20, 4);
  });

  it('falls back to the first lot price for since-purchase when no snapshot predates it', () => {
    load([
      makeAsset(
        [makeLot({ date: '2026-01-05', quantity: 10, pricePerUnit: 100 })],
        [makeSnap({ date: '2026-07-22', pricePerUnit: 150 })],
      ),
    ]);
    expect(service.assetMetrics()[0].changeSincePurchase).toBeCloseTo(50, 4);
  });

  it('survives a backdated sell before its funding buy without NaN', () => {
    load([
      makeAsset(
        [
          makeLot({ date: '2026-01-10', quantity: 10, pricePerUnit: 100 }),
          makeLot({ date: '2026-01-01', quantity: -10, pricePerUnit: 120 }),
        ],
        [makeSnap({ pricePerUnit: 130 })],
      ),
    ]);
    const m = service.assetMetrics()[0];
    expect(Number.isFinite(m.netCostBasis)).toBe(true);
    expect(Number.isFinite(m.realizedPnl)).toBe(true);
    expect(service.totalCostBasis()).not.toBeNaN();
  });

  it('sums portfolio totals across assets', () => {
    load([
      makeAsset(
        [makeLot({ assetId: 1, quantity: 10, pricePerUnit: 100 })],
        [makeSnap({ assetId: 1, pricePerUnit: 120 })],
        { id: 1 },
      ),
      makeAsset(
        [makeLot({ assetId: 2, quantity: 50, pricePerUnit: 70 })],
        [makeSnap({ assetId: 2, pricePerUnit: 85 })],
        { id: 2, assetType: 'Gold', ticker: null, name: 'Physical Gold' },
      ),
    ]);
    expect(service.totalCurrentValue()).toBeCloseTo(10 * 120 + 50 * 85, 6);
    expect(service.totalCostBasis()).toBeCloseTo(1000 + 3500, 6);
    expect(service.totalUnrealizedPnl()).toBeCloseTo(950, 6);
  });

  describe('total return since the first buy', () => {
    it('counts every buy with its fees as money put in, and sells as realised', () => {
      load([
        makeAsset(
          [
            makeLot({ date: '2026-01-05', quantity: 10, pricePerUnit: 100, fees: 2 }),
            makeLot({ date: '2026-02-05', quantity: 5, pricePerUnit: 110, fees: 1 }),
            makeLot({ date: '2026-03-05', quantity: -6, pricePerUnit: 130, fees: 1.5 }),
          ],
          [makeSnap({ pricePerUnit: 125 })],
        ),
      ]);
      const m = service.assetMetrics()[0];
      // Average cost 1553 / 15; six sold at 130 less the fee, nine held at 125.
      expect(m.invested).toBeCloseTo(1553, 6);
      expect(m.realizedPnl).toBeCloseTo(780 - (6 * 1553) / 15 - 1.5, 6);
      expect(m.unrealizedPnl).toBeCloseTo(1125 - (9 * 1553) / 15, 6);
      // Value plus what the sell brought in, less the money put in.
      expect(m.totalReturn).toBeCloseTo(1125 + 778.5 - 1553, 6);
      expect(m.totalReturnPct).toBeCloseTo((350.5 / 1553) * 100, 6);
      expect(m.firstBuyDate).toBe('2026-01-05');
    });

    it('gives an asset never sold its unrealised return on the money put in', () => {
      load([
        makeAsset(
          [makeLot({ quantity: 10, pricePerUnit: 100, fees: 2.5 })],
          [makeSnap({ pricePerUnit: 120 })],
        ),
      ]);
      const m = service.assetMetrics()[0];
      expect(m.invested).toBeCloseTo(1002.5, 6);
      expect(m.totalReturn).toBeCloseTo(197.5, 6);
      expect(m.totalReturnPct).toBeCloseTo((197.5 / 1002.5) * 100, 6);
    });

    it('leaves the return unknown while units are held without a price', () => {
      load([makeAsset([makeLot({ quantity: 10, pricePerUnit: 100 })], [])]);
      const m = service.assetMetrics()[0];
      expect(m.invested).toBe(1000);
      expect(m.totalReturn).toBeNull();
      expect(m.totalReturnPct).toBeNull();
    });

    it('keeps an asset sold out in the totals, with its buys and its realised return', () => {
      // A round trip without fees: two buys at one price, both sold at a slightly higher one,
      // and no price for the asset since.
      const roundTrip = makeAsset(
        [
          makeLot({ assetId: 1, date: '2026-05-04', quantity: 3, pricePerUnit: 162.92 }),
          makeLot({ assetId: 1, date: '2026-05-20', quantity: 0.6459, pricePerUnit: 162.92 }),
          makeLot({ assetId: 1, date: '2026-06-10', quantity: -3, pricePerUnit: 163.36 }),
          makeLot({ assetId: 1, date: '2026-06-10', quantity: -0.6459, pricePerUnit: 163.36 }),
        ],
        [],
        { id: 1 },
      );
      const gold = makeAsset(
        [makeLot({ assetId: 2, date: '2026-01-24', quantity: 10, pricePerUnit: 120, fees: 1 })],
        [makeSnap({ assetId: 2, pricePerUnit: 130 })],
        { id: 2, assetType: 'Gold', ticker: null, name: 'Gold' },
      );
      load([roundTrip, gold]);

      const sold = service.assetMetrics()[0];
      expect(sold.totalQuantity).toBe(0);
      expect(sold.invested).toBeCloseTo(3.6459 * 162.92, 6);
      expect(sold.totalReturn).toBeCloseTo(3.6459 * 0.44, 6);
      expect(sold.totalReturnPct).toBeCloseTo((0.44 / 162.92) * 100, 6);

      expect(service.totalInvested()).toBeCloseTo(3.6459 * 162.92 + 1201, 6);
      expect(service.totalReturn()).toBeCloseTo(3.6459 * 0.44 + 99, 6);
      expect(service.totalReturnPct()).toBeCloseTo(
        ((3.6459 * 0.44 + 99) / (3.6459 * 162.92 + 1201)) * 100,
        6,
      );
      expect(service.firstBuyDate()).toBe('2026-01-24');
    });

    it('has no percentage or start without any buy', () => {
      load([]);
      expect(service.totalInvested()).toBe(0);
      expect(service.totalReturnPct()).toBeNull();
      expect(service.firstBuyDate()).toBeNull();
    });
  });

  it('computes portfolio history with net invested per date', () => {
    load([
      makeAsset(
        [
          makeLot({ date: '2026-01-05', quantity: 10, pricePerUnit: 100, fees: 2.5 }),
          makeLot({ date: '2026-03-05', quantity: -4, pricePerUnit: 120, fees: 2 }),
        ],
        [
          makeSnap({ date: '2026-03-10', pricePerUnit: 130 }),
          makeSnap({ date: '2026-02-01', pricePerUnit: 110 }),
          makeSnap({ date: '2026-01-05', pricePerUnit: 100 }),
        ],
      ),
    ]);
    const points = service.portfolioHistory();
    expect(points.map((p) => p.date)).toEqual(['2026-01-05', '2026-02-01', '2026-03-10']);
    expect(points[0].totalValue).toBeCloseTo(1000, 6);
    expect(points[0].invested).toBeCloseTo(1002.5, 6);
    expect(points[1].totalValue).toBeCloseTo(1100, 6);
    expect(points[1].invested).toBeCloseTo(1002.5, 6);
    expect(points[2].totalValue).toBeCloseTo(6 * 130, 6);
    expect(points[2].invested).toBeCloseTo(1002.5 - 4 * 120 + 2, 6);
  });

  it('computes history and allocation for a filtered asset subset', () => {
    const etf = makeAsset(
      [makeLot({ assetId: 1, date: '2026-01-05', quantity: 10, pricePerUnit: 100 })],
      [makeSnap({ assetId: 1, date: '2026-01-05', pricePerUnit: 120 })],
      { id: 1 },
    );
    const gold = makeAsset(
      [makeLot({ assetId: 2, date: '2026-01-05', quantity: 50, pricePerUnit: 70 })],
      [makeSnap({ assetId: 2, date: '2026-01-05', pricePerUnit: 85 })],
      { id: 2, assetType: 'Gold', ticker: null, name: 'Physical Gold' },
    );
    load([etf, gold]);

    const goldHistory = service.portfolioHistoryFor([gold]);
    expect(goldHistory).toHaveLength(1);
    expect(goldHistory[0].totalValue).toBeCloseTo(50 * 85, 6);
    expect(goldHistory[0].invested).toBeCloseTo(3500, 6);

    const goldAllocation = service.allocationDataFor(
      service.assetMetrics().filter((m) => m.asset.assetType === 'Gold'),
    );
    expect(goldAllocation).toHaveLength(1);
    expect(goldAllocation[0].label).toBe('Physical Gold');
    expect(goldAllocation[0].value).toBeCloseTo(50 * 85, 6);
  });

  it('drops history dates before any holdings exist', () => {
    load([
      makeAsset(
        [makeLot({ date: '2026-01-05', quantity: 10, pricePerUnit: 100 })],
        [
          makeSnap({ date: '2026-01-05', pricePerUnit: 105 }),
          makeSnap({ date: '2026-01-02', pricePerUnit: 100 }),
        ],
      ),
    ]);
    const points = service.portfolioHistory();
    expect(points.map((p) => p.date)).toEqual(['2026-01-05']);
  });

  it('gives the same history as a per-date scan over a long daily series', () => {
    // Three years of weekday closes for two assets, with buys and a sell along the way.
    const dates: string[] = [];
    for (let d = new Date('2023-01-02T00:00:00Z'); d < new Date('2026-01-01T00:00:00Z');) {
      if (d.getUTCDay() % 6 !== 0) dates.push(d.toISOString().slice(0, 10));
      d.setUTCDate(d.getUTCDate() + 1);
    }
    const snaps = (assetId: number, base: number) =>
      dates.map((date, i) => makeSnap({ assetId, date, pricePerUnit: base + (i % 17) })).reverse();
    const etf = makeAsset(
      [
        makeLot({ assetId: 1, date: '2023-06-01', quantity: 10, pricePerUnit: 100, fees: 1 }),
        makeLot({ assetId: 1, date: '2024-03-15', quantity: 5, pricePerUnit: 110 }),
        makeLot({ assetId: 1, date: '2025-02-03', quantity: -8, pricePerUnit: 120, fees: 2 }),
      ],
      snaps(1, 100),
      { id: 1 },
    );
    const gold = makeAsset(
      [makeLot({ assetId: 2, date: '2024-01-10', quantity: 30, pricePerUnit: 60 })],
      snaps(2, 60),
      { id: 2, assetType: 'Gold', ticker: null, name: 'Gold' },
    );

    const points = service.portfolioHistoryFor([etf, gold]);

    const expected = dates
      .map((date) => {
        let totalValue = 0;
        let invested = 0;
        for (const asset of [etf, gold]) {
          const lots = asset.lots.filter((l) => l.date <= date);
          invested += lots.reduce((s, l) => s + l.quantity * l.pricePerUnit + (l.fees ?? 0), 0);
          const qty = lots.reduce((s, l) => s + l.quantity, 0);
          const snap = asset.priceSnapshots.find((s) => s.date <= date);
          if (snap && qty > 0) totalValue += qty * snap.pricePerUnit;
        }
        return { date, totalValue, invested };
      })
      .filter((p) => p.totalValue > 0);
    expect(points.map((p) => p.date)).toEqual(expected.map((p) => p.date));
    points.forEach((p, i) => {
      expect(p.totalValue).toBeCloseTo(expected[i].totalValue, 6);
      expect(p.invested).toBeCloseTo(expected[i].invested, 6);
    });
  });

  it('marks a held asset stale when its latest price is too old or its sync failed', () => {
    service.today.set('2026-07-27');
    load([
      makeAsset([makeLot()], [makeSnap({ date: '2026-07-22' })], { id: 1 }),
      makeAsset([makeLot({ assetId: 2 })], [makeSnap({ assetId: 2, date: '2026-07-24' })], {
        id: 2,
      }),
      makeAsset([makeLot({ assetId: 3 })], [makeSnap({ assetId: 3, date: '2026-07-24' })], {
        id: 3,
        priceSyncError: 'No prices found for X.DE.',
      }),
      makeAsset(
        [makeLot({ assetId: 4 }), makeLot({ assetId: 4, quantity: -10 })],
        [makeSnap({ assetId: 4, date: '2026-01-02' })],
        { id: 4 },
      ),
    ]);
    expect(service.assetMetrics().map((m) => m.pricesStale)).toEqual([true, false, true, false]);
    expect(service.assetMetrics()[1].latestPriceDate).toBe('2026-07-24');
  });

  it('marks prices stale more than one trading day behind, skipping weekends', () => {
    expect(isPriceStale('2026-07-24', '2026-07-28')).toBe(false);
    expect(isPriceStale('2026-07-24', '2026-07-29')).toBe(true);
    expect(isPriceStale(null, '2026-07-29')).toBe(true);
  });

  it('does not mark prices stale over Easter or Christmas', () => {
    // Good Friday and Easter Monday: on Tuesday, Thursday's close is the latest to expect.
    expect(isPriceStale('2026-04-02', '2026-04-07')).toBe(false);
    expect(isPriceStale('2026-03-31', '2026-04-07')).toBe(true);
    // 24 to 26 December 2025 closed: on Monday the 29th, the 23rd's close is current.
    expect(isPriceStale('2025-12-23', '2025-12-29')).toBe(false);
    expect(isPriceStale('2025-12-19', '2025-12-29')).toBe(true);
  });

  it('asks whether the server syncs prices', () => {
    expect(service.priceSyncEnabled()).toBeNull();
    service.loadPriceSyncStatus();
    controller.expectOne('/api/investments/prices/status').flush({ enabled: false });
    expect(service.priceSyncEnabled()).toBe(false);
  });

  it('syncs one asset or all of them through the sync endpoint', () => {
    service.syncPrices(3).subscribe();
    const one = controller.expectOne((r) => r.url === '/api/investments/prices/sync');
    expect(one.request.method).toBe('POST');
    expect(one.request.params.get('assetId')).toBe('3');
    one.flush({ assets: [] });

    service.syncPrices().subscribe();
    const all = controller.expectOne((r) => r.url === '/api/investments/prices/sync');
    expect(all.request.params.has('assetId')).toBe(false);
    all.flush({ assets: [] });
  });

  it('values the portfolio from the loaded price history when one is given', () => {
    const asset = makeAsset(
      [makeLot({ date: '2026-01-05', quantity: 10, pricePerUnit: 100 })],
      [makeSnap({ date: '2026-07-22', pricePerUnit: 120 })],
    );
    const points = service.portfolioHistoryFor(
      [asset],
      [{ assetId: 1, dates: ['2026-01-02', '2026-01-05', '2026-01-06'], prices: [99, 100, 101] }],
    );
    expect(points.map((p) => [p.date, p.totalValue])).toEqual([
      ['2026-01-05', 1000],
      ['2026-01-06', 1010],
    ]);
  });

  it('values a sparsely priced asset from the price before the range', () => {
    // The history for a range starts each asset with its latest price before it (the API's
    // carry-in), so gold priced in January still counts on February's dates.
    const etf = makeAsset([makeLot({ assetId: 1, date: '2025-12-01', quantity: 10 })], [], {
      id: 1,
    });
    const gold = makeAsset([makeLot({ assetId: 2, date: '2025-12-01', quantity: 2 })], [], {
      id: 2,
      assetType: 'Gold',
      ticker: null,
      name: 'Gold',
    });
    const points = service.portfolioHistoryFor(
      [etf, gold],
      [
        { assetId: 1, dates: ['2026-01-30', '2026-02-02'], prices: [100, 101] },
        { assetId: 2, dates: ['2026-01-15'], prices: [50] },
      ],
    );
    expect(points.map((p) => [p.date, p.totalValue])).toEqual([
      ['2026-01-15', 100],
      ['2026-01-30', 1100],
      ['2026-02-02', 1110],
    ]);
  });

  it('loads the chart history from a date, and again on every reload', () => {
    service.loadPriceHistory('2025-10-01');
    const first = controller.expectOne((r) => r.url === '/api/investments/prices/history');
    expect(first.request.params.get('from')).toBe('2025-10-01');
    first.flush([{ assetId: 1, dates: ['2025-10-01'], prices: [100] }]);
    expect(service.priceHistory()).toHaveLength(1);

    service.load();
    const again = controller.expectOne((r) => r.url === '/api/investments/prices/history');
    expect(again.request.params.get('from')).toBe('2025-10-01');
    again.flush([]);
    controller.expectOne('/api/investments/assets').flush([]);
  });

  it('keeps the latest range when answers arrive out of order', () => {
    service.loadPriceHistory('2025-10-01');
    service.loadPriceHistory(null);
    const [older, latest] = controller.match((r) => r.url === '/api/investments/prices/history');
    latest.flush([{ assetId: 1, dates: ['2011-10-04'], prices: [30] }]);
    older.flush([{ assetId: 1, dates: ['2025-10-01'], prices: [100] }]);
    expect(service.priceHistory()![0].dates).toEqual(['2011-10-04']);
  });
});
