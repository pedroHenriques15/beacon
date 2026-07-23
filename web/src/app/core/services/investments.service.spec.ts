import { describe, it, expect, beforeEach } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { InvestmentsService } from './investments.service';
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
    ticker: 'VWCE',
    name: 'Vanguard FTSE All-World',
    notes: null,
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

  it('returns null 24h change with a single snapshot', () => {
    load([makeAsset([makeLot()], [makeSnap()])]);
    expect(service.assetMetrics()[0].change24h).toBeNull();
  });

  it('computes 24h change from the two latest snapshots', () => {
    load([
      makeAsset(
        [makeLot()],
        [
          makeSnap({ date: '2026-07-22', pricePerUnit: 130 }),
          makeSnap({ date: '2026-07-21', pricePerUnit: 125 }),
        ],
      ),
    ]);
    expect(service.assetMetrics()[0].change24h).toBeCloseTo(4, 4);
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
});
