import {
  Component,
  ElementRef,
  OnDestroy,
  signal,
  computed,
  effect,
  inject,
  viewChild,
} from '@angular/core';
import { CurrencyPipe, DecimalPipe, NgClass } from '@angular/common';
import { FormsModule } from '@angular/forms';
import {
  Chart,
  ArcElement,
  DoughnutController,
  LineController,
  LineElement,
  PointElement,
  CategoryScale,
  LinearScale,
  Tooltip,
  Legend,
  Filler,
  type Plugin,
} from 'chart.js';
import { InvestmentsService } from '../../core/services/investments.service';
import {
  InvestmentAsset,
  InvestmentLot,
  InvestmentPriceSnapshot,
} from '../../core/models/statement.model';
import { AssetHistoryChart } from './asset-history-chart';

Chart.register(
  ArcElement,
  DoughnutController,
  LineController,
  LineElement,
  PointElement,
  CategoryScale,
  LinearScale,
  Tooltip,
  Legend,
  Filler,
);

@Component({
  selector: 'app-investments',
  standalone: true,
  imports: [CurrencyPipe, DecimalPipe, NgClass, FormsModule, AssetHistoryChart],
  templateUrl: './investments.html',
  styleUrl: './investments.scss',
})
export class InvestmentsComponent implements OnDestroy {
  svc = inject(InvestmentsService);

  assets = this.svc.assets;
  loading = this.svc.loading;
  error = this.svc.error;
  assetMetrics = this.svc.assetMetrics;
  totalValue = this.svc.totalCurrentValue;
  totalCost = this.svc.totalCostBasis;
  totalPnl = this.svc.totalUnrealizedPnl;
  totalPct = this.svc.totalUnrealizedPct;
  totalRealized = this.svc.totalRealizedPnl;
  change24h = this.svc.portfolioChange24h;
  change1w = this.svc.portfolioChange1w;
  change1m = this.svc.portfolioChange1m;

  readonly Math = Math;

  activeTab = signal<'all' | 'ETF' | 'Gold'>('all');
  expandedAssetId = signal<number | null>(null);
  fetchingAssetId = signal<number | null>(null);
  fetchError = signal<string | null>(null);
  backfillingAssetId = signal<number | null>(null);
  backfillNotice = signal<string | null>(null);
  priceHistoryOpen = signal(false);

  filteredMetrics = computed(() => {
    const tab = this.activeTab();
    const metrics = this.assetMetrics();
    return tab === 'all' ? metrics : metrics.filter((m) => m.asset.assetType === tab);
  });

  // Charts follow the active tab: allocation from the tab's metrics, history from the tab's assets.
  tabAllocation = computed(() => this.svc.allocationDataFor(this.filteredMetrics()));
  tabHistory = computed(() => {
    const tab = this.activeTab();
    const assets = this.assets();
    return this.svc.portfolioHistoryFor(
      tab === 'all' ? assets : assets.filter((a) => a.assetType === tab),
    );
  });

  // ---- Asset modal ----
  showAssetModal = signal(false);
  assetModalMode = signal<'create' | 'edit'>('create');
  editingAssetId = signal<number | null>(null);
  modalAssetType = signal<'ETF' | 'Gold'>('ETF');
  modalTicker = signal('');
  modalAssetName = signal('');
  modalAssetNotes = signal('');
  assetSaving = signal(false);
  assetError = signal('');

  // ---- Lot modal ----
  showLotModal = signal(false);
  lotModalMode = signal<'buy' | 'sell' | 'edit'>('buy');
  editingLotId = signal<number | null>(null);
  lotAssetId = signal<number | null>(null);
  lotDate = signal('');
  lotQuantity = signal('');
  lotPrice = signal('');
  lotFees = signal('');
  lotNotes = signal('');
  lotSaving = signal(false);
  lotError = signal('');
  lotOriginalSign = signal<1 | -1>(1);

  lotAssetType = computed(
    () => this.assets().find((a) => a.id === this.lotAssetId())?.assetType ?? 'ETF',
  );
  lotUnitLabel = computed(() => (this.lotAssetType() === 'Gold' ? 'Grams' : 'Shares'));
  lotModalTitle = computed(() => {
    const mode = this.lotModalMode();
    if (mode === 'edit') return 'Edit Entry';
    const asset = this.assets().find((a) => a.id === this.lotAssetId());
    const label = asset?.ticker ?? asset?.name ?? '';
    return mode === 'buy' ? `Buy - ${label}` : `Sell - ${label}`;
  });

  // ---- Price modal ----
  showPriceModal = signal(false);
  priceAssetId = signal<number | null>(null);
  priceDate = signal('');
  priceValue = signal('');
  priceSaving = signal(false);
  priceError = signal('');
  priceAssetLabel = computed(() => {
    const a = this.assets().find((x) => x.id === this.priceAssetId());
    return a?.ticker ?? a?.name ?? '';
  });

  // ---- Charts ----
  allocationCanvas = viewChild<ElementRef<HTMLCanvasElement>>('allocationCanvas');
  historyCanvas = viewChild<ElementRef<HTMLCanvasElement>>('historyCanvas');
  historyRange = signal<'1M' | '3M' | '1Y' | 'All'>('1Y');
  readonly historyRanges = ['1M', '3M', '1Y', 'All'] as const;
  private allocationChart?: Chart;
  private historyChart?: Chart;

  filteredHistory = computed(() => {
    const points = this.tabHistory();
    const range = this.historyRange();
    if (range === 'All' || points.length === 0) return points;
    const days = range === '1M' ? 30 : range === '3M' ? 91 : 365;
    const cutoff = new Date();
    cutoff.setUTCDate(cutoff.getUTCDate() - days);
    const cutoffIso = cutoff.toISOString().slice(0, 10);
    return points.filter((p) => p.date >= cutoffIso);
  });

  constructor() {
    effect(() => {
      this.allocationCanvas();
      this.tabAllocation();
      this.renderAllocationChart();
    });
    effect(() => {
      this.historyCanvas();
      this.filteredHistory();
      this.renderHistoryChart();
    });
  }

  ngOnDestroy(): void {
    this.allocationChart?.destroy();
    this.historyChart?.destroy();
  }

  // ---- Asset modal ----
  openCreateAsset(type: 'ETF' | 'Gold'): void {
    this.assetModalMode.set('create');
    this.editingAssetId.set(null);
    this.modalAssetType.set(type);
    this.modalTicker.set('');
    this.modalAssetName.set('');
    this.modalAssetNotes.set('');
    this.assetError.set('');
    this.showAssetModal.set(true);
  }

  openEditAsset(asset: InvestmentAsset): void {
    this.assetModalMode.set('edit');
    this.editingAssetId.set(asset.id);
    this.modalAssetType.set(asset.assetType);
    this.modalTicker.set(asset.ticker ?? '');
    this.modalAssetName.set(asset.name);
    this.modalAssetNotes.set(asset.notes ?? '');
    this.assetError.set('');
    this.showAssetModal.set(true);
  }

  submitAsset(): void {
    if (!this.modalAssetName().trim()) {
      this.assetError.set('Name is required.');
      return;
    }
    if (this.modalAssetType() === 'ETF' && !this.modalTicker().trim()) {
      this.assetError.set('Ticker is required for ETFs.');
      return;
    }

    this.assetSaving.set(true);
    this.assetError.set('');
    const body = {
      assetType: this.modalAssetType(),
      ticker: this.modalTicker() || undefined,
      name: this.modalAssetName().trim(),
      notes: this.modalAssetNotes().trim() || undefined,
    };
    const req$ =
      this.assetModalMode() === 'create'
        ? this.svc.createAsset(body)
        : this.svc.updateAsset(this.editingAssetId()!, {
            ticker: body.ticker,
            name: body.name,
            notes: body.notes,
          });

    req$.subscribe({
      next: () => {
        this.showAssetModal.set(false);
        this.assetSaving.set(false);
        this.svc.load();
      },
      error: (err) => {
        this.assetError.set(err?.error ?? 'Failed to save asset.');
        this.assetSaving.set(false);
      },
    });
  }

  deleteAsset(asset: InvestmentAsset): void {
    if (!confirm(`Delete "${asset.name}" and all its data?`)) return;
    this.svc.deleteAsset(asset.id).subscribe({
      next: () => this.svc.load(),
      error: (err) => this.fetchError.set(err?.error ?? 'Failed to delete asset.'),
    });
  }

  // ---- Lot modal ----
  openCreateLot(assetId: number, mode: 'buy' | 'sell'): void {
    this.lotModalMode.set(mode);
    this.editingLotId.set(null);
    this.lotAssetId.set(assetId);
    this.lotOriginalSign.set(1);
    this.lotDate.set(new Date().toISOString().slice(0, 10));
    this.lotQuantity.set('');
    this.lotPrice.set('');
    this.lotFees.set('');
    this.lotNotes.set('');
    this.lotError.set('');
    this.showLotModal.set(true);
  }

  openEditLot(lot: InvestmentLot, assetId: number): void {
    this.lotModalMode.set('edit');
    this.editingLotId.set(lot.id);
    this.lotAssetId.set(assetId);
    this.lotOriginalSign.set(lot.quantity < 0 ? -1 : 1);
    this.lotDate.set(lot.date);
    this.lotQuantity.set(String(Math.abs(lot.quantity)));
    this.lotPrice.set(String(lot.pricePerUnit));
    this.lotFees.set(lot.fees != null ? String(lot.fees) : '');
    this.lotNotes.set(lot.notes ?? '');
    this.lotError.set('');
    this.showLotModal.set(true);
  }

  submitLot(): void {
    const qty = InvestmentsComponent.parseDecimal(this.lotQuantity());
    const price = InvestmentsComponent.parseDecimal(this.lotPrice());
    const fees = InvestmentsComponent.parseDecimal(this.lotFees());
    if (!qty || qty <= 0) {
      this.lotError.set('Quantity must be greater than zero.');
      return;
    }
    if (!price || price <= 0) {
      this.lotError.set('Price must be greater than zero.');
      return;
    }
    if (!this.lotDate()) {
      this.lotError.set('Date is required.');
      return;
    }

    const mode = this.lotModalMode();
    const sign = mode === 'sell' ? -1 : mode === 'edit' ? this.lotOriginalSign() : 1;
    const finalQty = sign * qty;

    if (sign < 0) {
      const assetId = this.lotAssetId()!;
      const held = this.assetMetrics().find((m) => m.asset.id === assetId)?.totalQuantity ?? 0;
      const editingId = this.editingLotId();
      const ownQty =
        editingId != null
          ? Math.abs(
              this.assets()
                .find((a) => a.id === assetId)
                ?.lots.find((l) => l.id === editingId)?.quantity ?? 0,
            )
          : 0;
      const maxSell = held + ownQty;
      if (qty > maxSell + 1e-9) {
        this.lotError.set(`Cannot sell ${qty} - only ${maxSell.toFixed(4)} held.`);
        return;
      }
    }

    this.lotSaving.set(true);
    this.lotError.set('');

    const req$ =
      mode === 'edit'
        ? this.svc.updateLot(this.editingLotId()!, {
            date: this.lotDate(),
            quantity: finalQty,
            pricePerUnit: price,
            fees,
            notes: this.lotNotes() || null,
          })
        : this.svc.createLot({
            assetId: this.lotAssetId()!,
            date: this.lotDate(),
            quantity: finalQty,
            pricePerUnit: price,
            fees,
            notes: this.lotNotes() || null,
          });

    req$.subscribe({
      next: () => {
        this.showLotModal.set(false);
        this.lotSaving.set(false);
        this.svc.load();
      },
      error: (err) => {
        this.lotError.set(err?.error ?? 'Failed to save entry.');
        this.lotSaving.set(false);
      },
    });
  }

  deleteLot(lot: InvestmentLot): void {
    if (!confirm('Delete this entry?')) return;
    this.svc.deleteLot(lot.id).subscribe({
      next: () => this.svc.load(),
      error: (err) => this.fetchError.set(err?.error ?? 'Failed to delete entry.'),
    });
  }

  // ---- Price modal ----
  openPriceModal(assetId: number): void {
    this.priceAssetId.set(assetId);
    this.priceDate.set(new Date().toISOString().slice(0, 10));
    this.priceValue.set('');
    this.priceError.set('');
    this.showPriceModal.set(true);
  }

  submitPrice(): void {
    const val = InvestmentsComponent.parseDecimal(this.priceValue());
    if (!val || val <= 0) {
      this.priceError.set('Price must be greater than zero.');
      return;
    }
    this.priceSaving.set(true);
    this.priceError.set('');
    this.svc
      .upsertPrice({ assetId: this.priceAssetId()!, date: this.priceDate(), pricePerUnit: val })
      .subscribe({
        next: () => {
          this.showPriceModal.set(false);
          this.priceSaving.set(false);
          this.svc.load();
        },
        error: () => {
          this.priceError.set('Failed to save price.');
          this.priceSaving.set(false);
        },
      });
  }

  deletePrice(snap: InvestmentPriceSnapshot): void {
    if (!confirm('Delete this price snapshot?')) return;
    this.svc.deletePrice(snap.id).subscribe({
      next: () => this.svc.load(),
      error: (err) => this.fetchError.set(err?.error ?? 'Failed to delete price snapshot.'),
    });
  }

  // ---- Fetch price (Alpha Vantage) ----
  fetchPrice(asset: InvestmentAsset): void {
    this.fetchingAssetId.set(asset.id);
    this.fetchError.set(null);
    this.svc.fetchPrice(asset.id).subscribe({
      next: () => {
        this.fetchingAssetId.set(null);
        this.svc.load();
      },
      error: (err) => {
        this.fetchingAssetId.set(null);
        this.fetchError.set(err?.error ?? 'Failed to fetch price.');
      },
    });
  }

  toggleExpand(id: number): void {
    this.expandedAssetId.update((cur) => (cur === id ? null : id));
    this.priceHistoryOpen.set(false);
  }

  togglePriceHistory(): void {
    this.priceHistoryOpen.update((open) => !open);
  }

  // ---- Backfill history ----
  needsBackfill(asset: InvestmentAsset): boolean {
    if (asset.lots.length === 0) return false;
    if (asset.priceSnapshots.length === 0) return true;
    const firstLot = asset.lots.reduce(
      (min, l) => (l.date < min ? l.date : min),
      asset.lots[0].date,
    );
    const earliestSnap = asset.priceSnapshots.reduce(
      (min, s) => (s.date < min ? s.date : min),
      asset.priceSnapshots[0].date,
    );
    const threshold = new Date(firstLot + 'T00:00:00Z');
    threshold.setUTCDate(threshold.getUTCDate() + 7);
    return earliestSnap > threshold.toISOString().slice(0, 10);
  }

  backfillHistory(asset: InvestmentAsset): void {
    this.backfillingAssetId.set(asset.id);
    this.backfillNotice.set(null);
    this.fetchError.set(null);
    this.svc.backfillHistory(asset.id).subscribe({
      next: (res) => {
        this.backfillingAssetId.set(null);
        this.backfillNotice.set(res.message);
        this.svc.load();
      },
      error: (err) => {
        this.backfillingAssetId.set(null);
        this.fetchError.set(err?.error ?? 'Backfill failed.');
      },
    });
  }

  // ---- Helpers ----
  /** Parses a decimal string accepting both '.' and ',' as separator. Returns null when empty or invalid. */
  private static parseDecimal(raw: string): number | null {
    const trimmed = raw.trim();
    if (!trimmed) return null;
    const value = Number(trimmed.replace(',', '.'));
    return Number.isFinite(value) ? value : null;
  }

  pnlClass(v: number | null): string {
    if (v == null) return '';
    return v >= 0 ? 'positive' : 'negative';
  }

  formatQty(qty: number, assetType: string): string {
    return assetType === 'Gold' ? qty.toFixed(2) + 'g' : qty.toFixed(4);
  }

  formatDate(d: string): string {
    return new Date(d).toLocaleDateString('en-GB', {
      day: '2-digit',
      month: 'short',
      year: 'numeric',
    });
  }

  // ---- Charts ----
  private renderAllocationChart(): void {
    this.allocationChart?.destroy();
    const data = this.tabAllocation();
    const canvas = this.allocationCanvas()?.nativeElement;
    if (data.length === 0 || !canvas) return;

    const total = data.reduce((s, d) => s + d.value, 0);
    const pct = (v: number) => ((v / total) * 100).toFixed(1);
    const centerTotal: Plugin<'doughnut'> = {
      id: 'centerTotal',
      afterDraw(chart) {
        const { left, right, top, bottom } = chart.chartArea;
        const ctx = chart.ctx;
        ctx.save();
        ctx.textAlign = 'center';
        ctx.textBaseline = 'middle';
        ctx.fillStyle = '#e2e8f0';
        ctx.font = "600 1rem 'Inter', sans-serif";
        ctx.fillText(
          `€${total.toLocaleString('en-GB', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`,
          (left + right) / 2,
          (top + bottom) / 2,
        );
        ctx.restore();
      },
    };

    this.allocationChart = new Chart(canvas, {
      type: 'doughnut',
      data: {
        labels: data.map((d) => `${d.label} · ${pct(d.value)}%`),
        datasets: [
          {
            data: data.map((d) => d.value),
            backgroundColor: data.map((d) => d.color),
            borderWidth: 2,
            borderColor: '#111827',
          },
        ],
      },
      options: {
        responsive: true,
        maintainAspectRatio: false,
        cutout: '62%',
        plugins: {
          legend: {
            position: 'right',
            labels: { color: '#94a3b8', padding: 14, font: { size: 11 } },
          },
          tooltip: {
            callbacks: {
              label: (ctx) =>
                ` €${(ctx.parsed as number).toLocaleString('en-GB', { minimumFractionDigits: 2, maximumFractionDigits: 2 })} (${pct(ctx.parsed as number)}%)`,
            },
          },
        },
      },
      plugins: [centerTotal],
    });
  }

  private renderHistoryChart(): void {
    this.historyChart?.destroy();
    const points = this.filteredHistory();
    const canvas = this.historyCanvas()?.nativeElement;
    if (points.length === 0 || !canvas) return;

    const ctx = canvas.getContext('2d')!;
    const gradient = ctx.createLinearGradient(0, 0, 0, 200);
    gradient.addColorStop(0, 'rgba(99,102,241,0.35)');
    gradient.addColorStop(1, 'rgba(99,102,241,0.0)');

    this.historyChart = new Chart(canvas, {
      type: 'line',
      data: {
        labels: points.map((p) => this.formatDate(p.date)),
        datasets: [
          {
            label: 'Value',
            data: points.map((p) => p.totalValue),
            borderColor: '#6366f1',
            backgroundColor: gradient,
            fill: true,
            tension: 0.35,
            pointRadius: points.length > 60 ? 0 : 3,
            pointHoverRadius: 5,
            pointBackgroundColor: '#6366f1',
          },
          {
            label: 'Invested',
            data: points.map((p) => p.invested),
            borderColor: '#64748b',
            borderDash: [6, 4],
            borderWidth: 1.5,
            fill: false,
            tension: 0,
            pointRadius: 0,
            pointHoverRadius: 0,
          },
        ],
      },
      options: {
        responsive: true,
        maintainAspectRatio: false,
        interaction: { mode: 'index', intersect: false },
        plugins: {
          legend: { display: false },
          tooltip: {
            callbacks: {
              label: (ctx) =>
                ` ${ctx.dataset.label}: €${(ctx.parsed.y as number).toLocaleString('en-GB', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`,
            },
          },
        },
        scales: {
          x: {
            grid: { color: 'rgba(30,45,66,0.8)' },
            ticks: { color: '#64748b', font: { size: 11 }, maxTicksLimit: 8 },
          },
          y: {
            grid: { color: 'rgba(30,45,66,0.8)' },
            ticks: {
              color: '#64748b',
              font: { size: 11 },
              callback: (v) => `€${(v as number).toLocaleString()}`,
            },
          },
        },
      },
    });
  }
}
