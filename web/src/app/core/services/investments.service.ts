import { Injectable, inject, signal, computed } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { InvestmentAsset, InvestmentLot, InvestmentPriceSnapshot } from '../models/statement.model';

export interface AssetMetric {
  asset: InvestmentAsset;
  totalQuantity: number;
  netCost: number;
  currentPrice: number | null;
  currentValue: number | null;
  unrealizedPnl: number | null;
  unrealizedPct: number | null;
}

export interface PortfolioPoint {
  date: string;
  totalValue: number;
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
      next: data => { this.assets.set(data); this.loading.set(false); },
      error: () => { this.error.set('Failed to load investments.'); this.loading.set(false); },
    });
  }

  // ---- Asset HTTP ----
  createAsset(body: { assetType: string; ticker?: string; name: string; notes?: string }): Observable<InvestmentAsset> {
    return this.http.post<InvestmentAsset>('/api/investments/assets', body);
  }
  updateAsset(id: number, body: { ticker?: string; name: string; notes?: string }): Observable<InvestmentAsset> {
    return this.http.put<InvestmentAsset>(`/api/investments/assets/${id}`, body);
  }
  deleteAsset(id: number): Observable<void> {
    return this.http.delete<void>(`/api/investments/assets/${id}`);
  }
  fetchPrice(id: number): Observable<InvestmentPriceSnapshot> {
    return this.http.post<InvestmentPriceSnapshot>(`/api/investments/assets/${id}/fetch-price`, {});
  }

  // ---- Lot HTTP ----
  createLot(body: { assetId: number; date: string; quantity: number; pricePerUnit: number; fees?: number | null; notes?: string | null }): Observable<InvestmentLot> {
    return this.http.post<InvestmentLot>('/api/investments/lots', body);
  }
  updateLot(id: number, body: { date: string; quantity: number; pricePerUnit: number; fees?: number | null; notes?: string | null }): Observable<InvestmentLot> {
    return this.http.put<InvestmentLot>(`/api/investments/lots/${id}`, body);
  }
  deleteLot(id: number): Observable<void> {
    return this.http.delete<void>(`/api/investments/lots/${id}`);
  }

  // ---- Price HTTP ----
  upsertPrice(body: { assetId: number; date: string; pricePerUnit: number }): Observable<InvestmentPriceSnapshot> {
    return this.http.put<InvestmentPriceSnapshot>('/api/investments/prices', body);
  }
  deletePrice(id: number): Observable<void> {
    return this.http.delete<void>(`/api/investments/prices/${id}`);
  }

  // ---- Computed metrics ----

  assetMetrics = computed<AssetMetric[]>(() =>
    this.assets().map(asset => {
      const buyLots  = asset.lots.filter(l => l.quantity > 0);
      const sellLots = asset.lots.filter(l => l.quantity < 0);

      const totalQuantity  = asset.lots.reduce((s, l) => s + l.quantity, 0);
      const totalInvested  = buyLots.reduce((s, l) => s + l.quantity * l.pricePerUnit + (l.fees ?? 0), 0);
      const totalFromSells = sellLots.reduce((s, l) => s + Math.abs(l.quantity) * l.pricePerUnit, 0);
      const netCost        = totalInvested - totalFromSells;

      // priceSnapshots already ordered desc by date from API
      const currentPrice  = asset.priceSnapshots[0]?.pricePerUnit ?? null;
      const currentValue  = currentPrice != null ? totalQuantity * currentPrice : null;
      const unrealizedPnl = currentValue != null ? currentValue - netCost : null;
      const unrealizedPct = netCost > 0 && unrealizedPnl != null ? (unrealizedPnl / netCost) * 100 : null;

      return { asset, totalQuantity, netCost, currentPrice, currentValue, unrealizedPnl, unrealizedPct };
    })
  );

  totalCurrentValue = computed(() =>
    this.assetMetrics().reduce((s, m) => s + (m.currentValue ?? 0), 0)
  );
  totalCostBasis = computed(() =>
    this.assetMetrics().reduce((s, m) => s + m.netCost, 0)
  );
  totalUnrealizedPnl = computed(() => this.totalCurrentValue() - this.totalCostBasis());
  totalUnrealizedPct = computed(() => {
    const cost = this.totalCostBasis();
    return cost > 0 ? (this.totalUnrealizedPnl() / cost) * 100 : 0;
  });

  private readonly ETF_COLORS = ['#6366f1', '#3b82f6', '#06b6d4', '#8b5cf6', '#0ea5e9', '#a78bfa', '#38bdf8'];
  private readonly GOLD_COLOR = '#f59e0b';

  allocationData = computed(() => {
    let etfIndex = 0;
    return this.assetMetrics()
      .filter(m => (m.currentValue ?? 0) > 0)
      .map(m => ({
        label: m.asset.ticker ?? m.asset.name,
        value: m.currentValue!,
        color: m.asset.assetType === 'Gold'
          ? this.GOLD_COLOR
          : this.ETF_COLORS[etfIndex++ % this.ETF_COLORS.length],
      }));
  });

  portfolioHistory = computed<PortfolioPoint[]>(() => {
    const assets = this.assets();
    if (assets.length === 0) return [];

    const allDates = [...new Set(assets.flatMap(a => a.priceSnapshots.map(s => s.date)))].sort();
    if (allDates.length === 0) return [];

    return allDates
      .map(date => {
        let totalValue = 0;
        for (const asset of assets) {
          // snapshots are desc: find first one whose date <= target date
          const snap = asset.priceSnapshots.find(s => s.date <= date);
          if (!snap) continue;
          const netQty = asset.lots.filter(l => l.date <= date).reduce((s, l) => s + l.quantity, 0);
          if (netQty > 0) totalValue += netQty * snap.pricePerUnit;
        }
        return { date, totalValue };
      })
      .filter(p => p.totalValue > 0);
  });
}
