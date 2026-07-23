import { Injectable, inject, signal, computed } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import {
  BackfillPriceHistoryResponse,
  InvestmentAsset,
  InvestmentLot,
  InvestmentPriceSnapshot,
} from '../models/statement.model';

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
  totalReturn: number | null;
  change24h: number | null;
  change1w: number | null;
  change1m: number | null;
  changeSincePurchase: number | null;
}

export interface PortfolioPoint {
  date: string;
  totalValue: number;
}

function addDays(isoDate: string, days: number): string {
  const d = new Date(isoDate + 'T00:00:00Z');
  d.setUTCDate(d.getUTCDate() + days);
  return d.toISOString().slice(0, 10);
}

function changePct(current: number | null, ref: number | null): number | null {
  if (current == null || ref == null || ref === 0) return null;
  return ((current - ref) / ref) * 100;
}

@Injectable({ providedIn: 'root' })
export class InvestmentsService {
  private http = inject(HttpClient);

  assets = signal<InvestmentAsset[]>([]);
  loading = signal(false);
  error = signal<string | null>(null);

  constructor() {
    this.load();
  }

  load(): void {
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
  fetchPrice(id: number): Observable<InvestmentPriceSnapshot> {
    return this.http.post<InvestmentPriceSnapshot>(`/api/investments/assets/${id}/fetch-price`, {});
  }
  backfillHistory(id: number): Observable<BackfillPriceHistoryResponse> {
    return this.http.post<BackfillPriceHistoryResponse>(
      `/api/investments/assets/${id}/backfill`,
      {},
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

  assetMetrics = computed<AssetMetric[]>(() =>
    this.assets().map((asset) => {
      const lotsAsc = [...asset.lots].sort((a, b) => a.date.localeCompare(b.date) || a.id - b.id);

      let avgCost = 0;
      let totalHeld = 0;
      let realizedPnl = 0;
      for (const lot of lotsAsc) {
        const fee = lot.fees ?? 0;
        if (lot.quantity > 0) {
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
      const totalReturn = unrealizedPnl != null ? realizedPnl + unrealizedPnl : realizedPnl;

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
        totalReturn,
        change24h: changePct(currentPrice, priorClose),
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

  portfolioChange24h = computed(() => this.portfolioChange(null));
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

  allocationData = computed(() => {
    let etfIndex = 0;
    return this.assetMetrics()
      .filter((m) => (m.currentValue ?? 0) > 0)
      .map((m) => ({
        label: m.asset.ticker ?? m.asset.name,
        value: m.currentValue!,
        color:
          m.asset.assetType === 'Gold'
            ? this.GOLD_COLOR
            : this.ETF_COLORS[etfIndex++ % this.ETF_COLORS.length],
      }));
  });

  portfolioHistory = computed<PortfolioPoint[]>(() => {
    const assets = this.assets();
    if (assets.length === 0) return [];

    const allDates = [
      ...new Set(assets.flatMap((a) => a.priceSnapshots.map((s) => s.date))),
    ].sort();
    if (allDates.length === 0) return [];

    return allDates
      .map((date) => {
        let totalValue = 0;
        for (const asset of assets) {
          const snap = asset.priceSnapshots.find((s) => s.date <= date);
          if (!snap) continue;
          const netQty = asset.lots
            .filter((l) => l.date <= date)
            .reduce((s, l) => s + l.quantity, 0);
          if (netQty > 0) totalValue += netQty * snap.pricePerUnit;
        }
        return { date, totalValue };
      })
      .filter((p) => p.totalValue > 0);
  });
}
