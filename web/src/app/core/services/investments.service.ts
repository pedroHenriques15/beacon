import { Injectable, inject, signal, computed } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import {
  AssetPriceSeries,
  InvestmentAsset,
  InvestmentLot,
  InvestmentPriceSnapshot,
  PriceSyncStatus,
  SyncPriceHistoryResponse,
} from '../models/statement.model';
import { buildParams } from '../utils/http-params';
import { previousTradingDay } from '../utils/xetra-calendar';

export interface AssetMetric {
  asset: InvestmentAsset;
  totalQuantity: number;
  avgCostPerUnit: number;
  netCostBasis: number;
  currentPrice: number | null;
  currentValue: number | null;
  unrealizedPnl: number | null;
  unrealizedPct: number | null;
  realizedPnl: number;
  /** Money put in: every buy with its fees, sold since or not. */
  invested: number;
  /** Realised plus unrealised; null while units are held without a price. */
  totalReturn: number | null;
  /** Total return against the money put in, simple (ADR-038); null without either. */
  totalReturnPct: number | null;
  /** Date of the earliest buy. */
  firstBuyDate: string | null;
  /** Date of the latest price, the close the current price comes from. */
  latestPriceDate: string | null;
  /** Held, and its latest price is too old or missing, or its last sync failed. */
  pricesStale: boolean;
  change1d: number | null;
  change1w: number | null;
  change1m: number | null;
  changeSincePurchase: number | null;
}

export interface PortfolioPoint {
  date: string;
  totalValue: number;
  invested: number;
}

/** How the value moved between two points, split into the money moved and the price moves. */
export interface ValueChange {
  /** The value's change. */
  change: number;
  /** Money put in less money taken out. */
  putIn: number;
  /** The change less the money: what prices did. */
  growth: number;
}

export function valueChange(first: PortfolioPoint, last: PortfolioPoint): ValueChange {
  const change = last.totalValue - first.totalValue;
  const putIn = last.invested - first.invested;
  return { change, putIn, growth: change - putIn };
}

function addDays(isoDate: string, days: number): string {
  const d = new Date(isoDate + 'T00:00:00Z');
  d.setUTCDate(d.getUTCDate() + days);
  return d.toISOString().slice(0, 10);
}

/**
 * A latest price is stale when missing or more than one trading day behind: a day's close is
 * synced that evening, so before then the newest close to expect is the previous trading day's,
 * and one missed sync is allowed on top. Weekends and Xetra holidays don't count. Matches the
 * API's PriceSyncSettings.IsStale.
 */
export function isPriceStale(latestDate: string | null, today: string): boolean {
  if (latestDate == null) return true;
  return latestDate < previousTradingDay(previousTradingDay(today));
}

function changePct(current: number | null, ref: number | null): number | null {
  if (current == null || ref == null || ref === 0) return null;
  return ((current - ref) / ref) * 100;
}

/** A total return as a percentage of the money put in (ADR-038). */
export function returnPct(totalReturn: number | null, invested: number): number | null {
  return totalReturn != null && invested > 0 ? (totalReturn / invested) * 100 : null;
}

/** The earliest of the assets' first buys. */
export function earliestBuy(metrics: AssetMetric[]): string | null {
  return (
    metrics
      .map((m) => m.firstBuyDate)
      .filter((d): d is string => d != null)
      .sort()[0] ?? null
  );
}

@Injectable({ providedIn: 'root' })
export class InvestmentsService {
  private http = inject(HttpClient);

  assets = signal<InvestmentAsset[]>([]);
  loading = signal(false);
  error = signal<string | null>(null);

  /** Every asset's prices from historyFrom on, for the portfolio value chart; null until loaded. */
  priceHistory = signal<AssetPriceSeries[] | null>(null);
  private historyFrom: string | null | undefined;
  private historyRequest = 0;

  constructor() {
    this.load();
  }

  load(): void {
    if (this.historyFrom !== undefined) this.loadPriceHistory(this.historyFrom);
    this.loading.set(true);
    this.error.set(null);
    this.http.get<InvestmentAsset[]>('/api/investments/assets').subscribe({
      next: (data) => {
        this.assets.set(data);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('Failed to load investments.');
        this.loading.set(false);
      },
    });
  }

  /** Loads the portfolio chart's prices from a date (null: all of them), and keeps it for reloads. */
  loadPriceHistory(from: string | null): void {
    this.historyFrom = from;
    // Ranges can change faster than answers arrive: only the latest request's answer counts.
    const request = ++this.historyRequest;
    this.http
      .get<AssetPriceSeries[]>('/api/investments/prices/history', {
        params: buildParams({ from }),
      })
      .subscribe({
        next: (series) => {
          if (request === this.historyRequest) this.priceHistory.set(series);
        },
        error: () => {
          if (request === this.historyRequest) this.priceHistory.set(null);
        },
      });
  }

  /** Whether the server syncs prices: null until known, or when it couldn't be asked. */
  priceSyncEnabled = signal<boolean | null>(null);

  loadPriceSyncStatus(): void {
    this.http.get<PriceSyncStatus>('/api/investments/prices/status').subscribe({
      next: (status) => this.priceSyncEnabled.set(status.enabled),
      error: () => this.priceSyncEnabled.set(null),
    });
  }

  /** Every price of one asset, newest first. */
  getAssetPrices(id: number): Observable<InvestmentPriceSnapshot[]> {
    return this.http.get<InvestmentPriceSnapshot[]>(`/api/investments/assets/${id}/prices`);
  }

  createAsset(body: {
    assetType: string;
    ticker?: string;
    name: string;
    notes?: string;
  }): Observable<InvestmentAsset> {
    return this.http.post<InvestmentAsset>('/api/investments/assets', body);
  }
  updateAsset(
    id: number,
    body: { ticker?: string; name: string; notes?: string },
  ): Observable<InvestmentAsset> {
    return this.http.put<InvestmentAsset>(`/api/investments/assets/${id}`, body);
  }
  deleteAsset(id: number): Observable<void> {
    return this.http.delete<void>(`/api/investments/assets/${id}`);
  }
  /** Syncs one asset's prices, or every held asset's when no id is given. */
  syncPrices(assetId?: number): Observable<SyncPriceHistoryResponse> {
    return this.http.post<SyncPriceHistoryResponse>(
      '/api/investments/prices/sync',
      {},
      { params: buildParams({ assetId }) },
    );
  }

  createLot(body: {
    assetId: number;
    date: string;
    quantity: number;
    pricePerUnit: number;
    fees?: number | null;
    notes?: string | null;
  }): Observable<InvestmentLot> {
    return this.http.post<InvestmentLot>('/api/investments/lots', body);
  }
  updateLot(
    id: number,
    body: {
      date: string;
      quantity: number;
      pricePerUnit: number;
      fees?: number | null;
      notes?: string | null;
    },
  ): Observable<InvestmentLot> {
    return this.http.put<InvestmentLot>(`/api/investments/lots/${id}`, body);
  }
  deleteLot(id: number): Observable<void> {
    return this.http.delete<void>(`/api/investments/lots/${id}`);
  }

  upsertPrice(body: {
    assetId: number;
    date: string;
    pricePerUnit: number;
  }): Observable<InvestmentPriceSnapshot> {
    return this.http.put<InvestmentPriceSnapshot>('/api/investments/prices', body);
  }
  deletePrice(id: number): Observable<void> {
    return this.http.delete<void>(`/api/investments/prices/${id}`);
  }

  /** Today's date (UTC), the reference for stale prices. */
  today = signal(new Date().toISOString().slice(0, 10));

  assetMetrics = computed<AssetMetric[]>(() =>
    this.assets().map((asset) => {
      const lotsAsc = [...asset.lots].sort((a, b) => a.date.localeCompare(b.date) || a.id - b.id);

      let avgCost = 0;
      let totalHeld = 0;
      let realizedPnl = 0;
      let invested = 0;
      let firstBuyDate: string | null = null;
      for (const lot of lotsAsc) {
        const fee = lot.fees ?? 0;
        if (lot.quantity > 0) {
          invested += lot.quantity * lot.pricePerUnit + fee;
          firstBuyDate ??= lot.date;
          avgCost =
            totalHeld > 0
              ? (totalHeld * avgCost + lot.quantity * lot.pricePerUnit + fee) /
                (totalHeld + lot.quantity)
              : (lot.quantity * lot.pricePerUnit + fee) / lot.quantity;
          totalHeld += lot.quantity;
        } else {
          const qty = Math.min(Math.abs(lot.quantity), Math.max(totalHeld, 0));
          realizedPnl += qty * lot.pricePerUnit - qty * avgCost - fee;
          totalHeld -= Math.abs(lot.quantity);
        }
      }

      const totalQuantity = Math.abs(totalHeld) < 1e-9 ? 0 : totalHeld;
      const netCostBasis = totalQuantity * avgCost;

      const snapshots = asset.priceSnapshots;
      const currentPrice = snapshots[0]?.pricePerUnit ?? null;
      const currentValue = currentPrice != null ? totalQuantity * currentPrice : null;
      const unrealizedPnl = currentValue != null ? currentValue - netCostBasis : null;
      const unrealizedPct =
        netCostBasis > 0 && unrealizedPnl != null ? (unrealizedPnl / netCostBasis) * 100 : null;
      const totalReturn =
        unrealizedPnl == null && totalQuantity > 0 ? null : realizedPnl + (unrealizedPnl ?? 0);

      const latestDate = snapshots[0]?.date ?? null;
      const priorClose =
        latestDate != null
          ? (snapshots.find((s) => s.date < latestDate)?.pricePerUnit ?? null)
          : null;
      const prior1w =
        latestDate != null ? this.priceAtOrBefore(snapshots, addDays(latestDate, -7)) : null;
      const prior1m =
        latestDate != null ? this.priceAtOrBefore(snapshots, addDays(latestDate, -30)) : null;
      const firstLotDate = lotsAsc[0]?.date ?? null;
      const priorPurchase =
        firstLotDate != null
          ? (this.priceAtOrBefore(snapshots, firstLotDate) ?? lotsAsc[0].pricePerUnit)
          : null;

      return {
        asset,
        totalQuantity,
        avgCostPerUnit: avgCost,
        netCostBasis,
        currentPrice,
        currentValue,
        unrealizedPnl,
        unrealizedPct,
        realizedPnl,
        invested,
        totalReturn,
        totalReturnPct: returnPct(totalReturn, invested),
        firstBuyDate,
        latestPriceDate: latestDate,
        pricesStale:
          totalQuantity > 0 &&
          (asset.priceSyncError != null || isPriceStale(latestDate, this.today())),
        change1d: changePct(currentPrice, priorClose),
        change1w: changePct(currentPrice, prior1w),
        change1m: changePct(currentPrice, prior1m),
        changeSincePurchase: changePct(currentPrice, priorPurchase),
      };
    }),
  );

  private priceAtOrBefore(snapshots: InvestmentPriceSnapshot[], date: string): number | null {
    return snapshots.find((s) => s.date <= date)?.pricePerUnit ?? null;
  }

  totalCurrentValue = computed(() =>
    this.assetMetrics().reduce((s, m) => s + (m.currentValue ?? 0), 0),
  );
  totalCostBasis = computed(() => this.assetMetrics().reduce((s, m) => s + m.netCostBasis, 0));
  totalRealizedPnl = computed(() => this.assetMetrics().reduce((s, m) => s + m.realizedPnl, 0));
  totalUnrealizedPnl = computed(() => this.totalCurrentValue() - this.totalCostBasis());
  totalUnrealizedPct = computed(() => {
    const cost = this.totalCostBasis();
    return cost > 0 ? (this.totalUnrealizedPnl() / cost) * 100 : null;
  });
  totalReturn = computed(() => this.totalRealizedPnl() + this.totalUnrealizedPnl());
  /** Money put in across every asset, those sold out included. */
  totalInvested = computed(() => this.assetMetrics().reduce((s, m) => s + m.invested, 0));
  totalReturnPct = computed(() => returnPct(this.totalReturn(), this.totalInvested()));
  firstBuyDate = computed(() => earliestBuy(this.assetMetrics()));

  portfolioChange1d = computed(() => this.portfolioChange(null));
  portfolioChange1w = computed(() => this.portfolioChange(7));
  portfolioChange1m = computed(() => this.portfolioChange(30));

  private portfolioChange(daysBack: number | null): number | null {
    let current = 0;
    let reference = 0;
    for (const m of this.assetMetrics()) {
      const snapshots = m.asset.priceSnapshots;
      const latestDate = snapshots[0]?.date;
      if (latestDate == null || m.currentPrice == null || m.totalQuantity <= 0) continue;
      const ref =
        daysBack == null
          ? (snapshots.find((s) => s.date < latestDate)?.pricePerUnit ?? null)
          : this.priceAtOrBefore(snapshots, addDays(latestDate, -daysBack));
      if (ref == null) continue;
      current += m.totalQuantity * m.currentPrice;
      reference += m.totalQuantity * ref;
    }
    return reference > 0 ? ((current - reference) / reference) * 100 : null;
  }

  private readonly ETF_COLORS = [
    '#6366f1',
    '#3b82f6',
    '#06b6d4',
    '#8b5cf6',
    '#0ea5e9',
    '#a78bfa',
    '#38bdf8',
  ];
  private readonly GOLD_COLOR = '#f59e0b';

  allocationData = computed(() => this.allocationDataFor(this.assetMetrics()));

  allocationDataFor(metrics: AssetMetric[]): { label: string; value: number; color: string }[] {
    let etfIndex = 0;
    return metrics
      .filter((m) => (m.currentValue ?? 0) > 0)
      .map((m) => ({
        label: m.asset.ticker ?? m.asset.name,
        value: m.currentValue!,
        color:
          m.asset.assetType === 'Gold'
            ? this.GOLD_COLOR
            : this.ETF_COLORS[etfIndex++ % this.ETF_COLORS.length],
      }));
  }

  portfolioHistory = computed<PortfolioPoint[]>(() =>
    this.portfolioHistoryFor(this.assets(), this.priceHistory() ?? undefined),
  );

  /**
   * One point per price date from the first lot on, walking each asset's lots and prices once:
   * a 15-year daily history would make a per-date scan quadratic. Prices come from
   * priceHistory when given (the asset list carries only recent ones), else from the assets.
   */
  portfolioHistoryFor(assets: InvestmentAsset[], history?: AssetPriceSeries[]): PortfolioPoint[] {
    const byAsset = new Map(history?.map((h) => [h.assetId, h]));
    const pricesOf = (asset: InvestmentAsset): { date: string; pricePerUnit: number }[] => {
      if (history == null) {
        return [...asset.priceSnapshots].sort((a, b) => a.date.localeCompare(b.date));
      }
      const h = byAsset.get(asset.id);
      return h ? h.dates.map((date, i) => ({ date, pricePerUnit: h.prices[i] })) : [];
    };
    const series = assets.map((asset) => ({
      lots: [...asset.lots].sort((a, b) => a.date.localeCompare(b.date)),
      prices: pricesOf(asset),
      lotIndex: 0,
      priceIndex: 0,
      quantity: 0,
      invested: 0,
      price: null as number | null,
    }));

    const firstLot = series
      .map((s) => s.lots[0]?.date)
      .filter((d): d is string => d != null)
      .sort()[0];
    if (firstLot == null) return [];

    const dates = [...new Set(series.flatMap((s) => s.prices.map((p) => p.date)))]
      .filter((d) => d >= firstLot)
      .sort();

    const points: PortfolioPoint[] = [];
    for (const date of dates) {
      let totalValue = 0;
      let invested = 0;
      for (const s of series) {
        // Net money in up to this date: sells (negative quantity) reduce it, fees always add.
        while (s.lotIndex < s.lots.length && s.lots[s.lotIndex].date <= date) {
          const lot = s.lots[s.lotIndex++];
          s.invested += lot.quantity * lot.pricePerUnit + (lot.fees ?? 0);
          s.quantity += lot.quantity;
        }
        while (s.priceIndex < s.prices.length && s.prices[s.priceIndex].date <= date) {
          s.price = s.prices[s.priceIndex++].pricePerUnit;
        }
        invested += s.invested;
        if (s.price != null && s.quantity > 0) totalValue += s.quantity * s.price;
      }
      if (totalValue > 0) points.push({ date, totalValue, invested });
    }
    return points;
  }
}
