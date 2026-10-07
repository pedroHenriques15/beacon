import {
  Component,
  ElementRef,
  OnDestroy,
  signal,
  computed,
  effect,
  inject,
  viewChild,
  ChangeDetectionStrategy,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import {
  Chart,
  LineController,
  LineElement,
  PointElement,
  CategoryScale,
  LinearScale,
  Tooltip,
  Legend,
  Filler,
} from 'chart.js';
import { InvestmentsService } from '../../core/services/investments.service';
import {
  InvestmentAsset,
  InvestmentLot,
  InvestmentPriceSnapshot,
  SyncPriceHistoryResponse,
} from '../../core/models/statement.model';
import { ConfirmDialogComponent } from '../../core/components/confirm-dialog/confirm-dialog';
import { applyChartTheme, axisOptions, withAlpha } from '../../core/charts/chart-theme';
import { eur, signedEur } from '../../core/utils/money';
import {
  AssetHistoryChart,
  HISTORY_RANGES,
  HistoryRange,
  rangeCutoff,
} from './asset-history-chart';
import {
  allocationShares,
  assetColors,
  dayValue,
  timeTick,
  eurPrice,
  eurTick,
  historyCaption,
  monthYear,
  portfolioSummary,
  recentActivity,
  shortDate,
  signedPct,
  spanDays,
  weekdayDate,
} from './investments-view';

/** Price history rows shown at a time: years of daily closes would make a very long list. */
const PRICE_ROWS_STEP = 30;

/** Buys and sells shown under "Recent activity"; every entry stays in its holding's details. */
const ACTIVITY_SHOWN = 6;

// Legend is registered for its defaults, which applyChartTheme() sets; the legend stays hidden.
Chart.register(
  LineController,
  LineElement,
  PointElement,
  CategoryScale,
  LinearScale,
  Tooltip,
  Legend,
  Filler,
);

interface PendingDelete {
  title: string;
  message: string;
  run: () => void;
}

@Component({
  selector: 'app-investments',
  standalone: true,
  imports: [FormsModule, AssetHistoryChart, ConfirmDialogComponent],
  templateUrl: './investments.html',
  changeDetection: ChangeDetectionStrategy.Eager,
  styleUrl: './investments.scss',
})
export class InvestmentsComponent implements OnDestroy {
  svc = inject(InvestmentsService);

  assets = this.svc.assets;
  loading = this.svc.loading;
  error = this.svc.error;
  assetMetrics = this.svc.assetMetrics;

  readonly Math = Math;
  readonly eur = eur;
  readonly signedEur = signedEur;
  readonly signedPct = signedPct;
  readonly eurPrice = eurPrice;
  readonly monthYear = monthYear;

  activeTab = signal<'all' | 'ETF' | 'Gold'>('all');
  expandedAssetId = signal<number | null>(null);
  actionError = signal<string | null>(null);
  syncing = signal(false);
  syncingAssetId = signal<number | null>(null);
  syncNotice = signal<string | null>(null);
  priceHistoryOpen = signal(false);
  priceRowsShown = signal(PRICE_ROWS_STEP);
  /** Every price of the expanded asset, newest first, loaded when it opens. */
  expandedPrices = signal<InvestmentPriceSnapshot[] | null>(null);
  /** A delete waiting for its confirmation. */
  pendingDelete = signal<PendingDelete | null>(null);

  /** The oldest latest price among held assets: every held position is valued at least this recently. */
  pricesAsOf = computed(() => {
    const dates = this.assetMetrics()
      .filter((m) => m.totalQuantity > 0 && m.latestPriceDate != null)
      .map((m) => m.latestPriceDate!)
      .sort();
    return dates[0] ?? null;
  });
  pricesAsOfLabel = computed(() => {
    const asOf = this.pricesAsOf();
    return asOf ? weekdayDate(asOf, this.svc.today()) : null;
  });
  stalePrices = computed(() => this.assetMetrics().some((m) => m.pricesStale));
  /** Sync controls show only where the server syncs prices (not in the demo). */
  syncEnabled = this.svc.priceSyncEnabled;

  filteredMetrics = computed(() => {
    const tab = this.activeTab();
    const metrics = this.assetMetrics();
    return tab === 'all' ? metrics : metrics.filter((m) => m.asset.assetType === tab);
  });

  /** The figures at the top follow the tab, like the charts and the holdings below them. */
  summary = computed(() => portfolioSummary(this.filteredMetrics()));
  valueLabel = computed(() => {
    const tab = this.activeTab();
    return tab === 'all' ? 'Portfolio value' : tab === 'ETF' ? 'ETF value' : 'Gold value';
  });

  // Charts follow the active tab: allocation from the tab's metrics, history from the tab's assets.
  tabAllocation = computed(() => this.svc.allocationDataFor(this.filteredMetrics()));
  allocation = computed(() => allocationShares(this.tabAllocation()));
  allocationLabel = computed(() =>
    this.allocation()
      .map((s) => `${s.label} ${s.pct.toFixed(1)} percent`)
      .join(', '),
  );
  /** Each asset's allocation colour, the same on every tab. */
  colors = computed(() => assetColors(this.assetMetrics(), this.svc.allocationData()));
  activity = computed(() =>
    recentActivity(
      this.filteredMetrics().map((m) => m.asset),
      ACTIVITY_SHOWN,
    ),
  );
  tabHistory = computed(() => {
    const tab = this.activeTab();
    const assets = this.assets();
    return this.svc.portfolioHistoryFor(
      tab === 'all' ? assets : assets.filter((a) => a.assetType === tab),
      this.svc.priceHistory() ?? undefined,
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

  // ---- Value chart ----
  historyCanvas = viewChild<ElementRef<HTMLCanvasElement>>('historyCanvas');
  /** Opens on the whole history, like the total return above it (ADR-038). */
  historyRange = signal<HistoryRange>('All');
  readonly historyRanges = HISTORY_RANGES;
  private historyChart?: Chart;

  filteredHistory = computed(() => {
    const points = this.tabHistory();
    const cutoff = rangeCutoff(this.historyRange());
    return cutoff == null ? points : points.filter((p) => p.date >= cutoff);
  });
  historyCaption = computed(() => historyCaption(this.filteredHistory()));
  historyLabel = computed(() => {
    const points = this.filteredHistory();
    if (points.length < 2) return '';
    const last = points[points.length - 1];
    return (
      `Value against money put in, ${shortDate(points[0].date)} to ${shortDate(last.date)}, ` +
      `ending at ${eur(last.totalValue)}`
    );
  });

  constructor() {
    this.svc.loadPriceSyncStatus();
    this.svc.loadPriceHistory(rangeCutoff(this.historyRange()));
    effect(() => {
      this.historyCanvas();
      this.filteredHistory();
      this.renderHistoryChart();
    });
  }

  ngOnDestroy(): void {
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

    const previousTicker = this.assets().find((a) => a.id === this.editingAssetId())?.ticker;
    req$.subscribe({
      next: (saved) => {
        this.showAssetModal.set(false);
        this.assetSaving.set(false);
        // A new asset or ticker: fetch its history now, so the page shows it straight away.
        const newSymbol = this.assetModalMode() === 'create' || saved.ticker !== previousTicker;
        if (newSymbol && this.syncEnabled()) {
          this.syncPrices(saved);
        } else {
          this.reload();
        }
      },
      error: (err) => {
        this.assetError.set(err?.error ?? 'Failed to save asset.');
        this.assetSaving.set(false);
      },
    });
  }

  deleteAsset(asset: InvestmentAsset): void {
    this.pendingDelete.set({
      title: 'Delete holding',
      message: `Delete "${asset.name}" and all its data?`,
      run: () =>
        this.svc.deleteAsset(asset.id).subscribe({
          next: () => this.reload(),
          error: (err) => this.actionError.set(err?.error ?? 'Failed to delete asset.'),
        }),
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
        this.reload();
      },
      error: (err) => {
        this.lotError.set(err?.error ?? 'Failed to save entry.');
        this.lotSaving.set(false);
      },
    });
  }

  deleteLot(lot: InvestmentLot): void {
    this.pendingDelete.set({
      title: 'Delete entry',
      message: 'Delete this entry?',
      run: () =>
        this.svc.deleteLot(lot.id).subscribe({
          next: () => this.reload(),
          error: (err) => this.actionError.set(err?.error ?? 'Failed to delete entry.'),
        }),
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
          this.reload();
        },
        error: () => {
          this.priceError.set('Failed to save price.');
          this.priceSaving.set(false);
        },
      });
  }

  deletePrice(snap: InvestmentPriceSnapshot): void {
    this.pendingDelete.set({
      title: 'Delete price',
      message: 'Delete this price snapshot?',
      run: () =>
        this.svc.deletePrice(snap.id).subscribe({
          next: () => this.reload(),
          error: (err) => this.actionError.set(err?.error ?? 'Failed to delete price snapshot.'),
        }),
    });
  }

  confirmPendingDelete(): void {
    const pending = this.pendingDelete();
    this.pendingDelete.set(null);
    pending?.run();
  }

  // ---- Price sync ----
  /** Syncs one asset's daily closes, or every held asset's when none is given. */
  syncPrices(asset?: InvestmentAsset): void {
    this.syncing.set(true);
    this.syncingAssetId.set(asset?.id ?? null);
    this.syncNotice.set(null);
    this.actionError.set(null);
    this.svc.syncPrices(asset?.id).subscribe({
      next: (res) => {
        this.syncing.set(false);
        this.syncingAssetId.set(null);
        this.syncNotice.set(InvestmentsComponent.syncSummary(res));
        this.reload();
      },
      error: (err) => {
        this.syncing.set(false);
        this.syncingAssetId.set(null);
        this.actionError.set(err?.error ?? 'Price sync failed.');
        this.reload();
      },
    });
  }

  private static syncSummary(res: SyncPriceHistoryResponse): string {
    if (res.assets.length === 0) return 'No held assets to sync.';
    return res.assets
      .map((a) => {
        if (a.error) return `${a.name}: ${a.error}`;
        const changed = a.added + a.replaced;
        const removed = a.removed > 0 ? ` ${a.removed} from the previous ticker removed.` : '';
        return changed === 0 && a.removed === 0
          ? `${a.name}: up to date.`
          : `${a.name}: ${changed} price${changed === 1 ? '' : 's'} updated.${removed}`;
      })
      .join(' ');
  }

  staleTitle(m: { asset: InvestmentAsset; latestPriceDate: string | null }): string {
    if (m.asset.priceSyncError) return m.asset.priceSyncError;
    return m.latestPriceDate
      ? `Latest price is from ${this.formatDate(m.latestPriceDate)}.`
      : 'No price yet.';
  }

  toggleExpand(id: number): void {
    this.expandedAssetId.update((cur) => (cur === id ? null : id));
    this.priceHistoryOpen.set(false);
    this.priceRowsShown.set(PRICE_ROWS_STEP);
    this.expandedPrices.set(null);
    this.loadExpandedPrices();
  }

  setHistoryRange(range: HistoryRange): void {
    this.historyRange.set(range);
    this.svc.loadPriceHistory(rangeCutoff(range));
  }

  /** Reloads the assets (and the chart's history), and the expanded asset's prices. */
  private reload(): void {
    this.svc.load();
    this.loadExpandedPrices();
  }

  private loadExpandedPrices(): void {
    const id = this.expandedAssetId();
    if (id == null) return;
    this.svc.getAssetPrices(id).subscribe({
      next: (prices) => {
        if (this.expandedAssetId() === id) this.expandedPrices.set(prices);
      },
      error: () => this.actionError.set('Failed to load the price history.'),
    });
  }

  togglePriceHistory(): void {
    this.priceHistoryOpen.update((open) => !open);
  }

  showMorePrices(): void {
    this.priceRowsShown.update((n) => n + PRICE_ROWS_STEP);
  }

  // ---- Helpers ----
  /** Parses a decimal string accepting both '.' and ',' as separator. Returns null when empty or invalid. */
  private static parseDecimal(raw: string): number | null {
    const trimmed = raw.trim();
    if (!trimmed) return null;
    const value = Number(trimmed.replace(',', '.'));
    return Number.isFinite(value) ? value : null;
  }

  /** The name a holding goes by: its ticker, or its name when it has none. */
  label(asset: InvestmentAsset): string {
    return asset.ticker ?? asset.name;
  }

  colorOf(assetId: number): string | null {
    return this.colors().get(assetId) ?? null;
  }

  /** '24.5310' shares, '28.00g' of gold. */
  formatQty(qty: number, assetType: string): string {
    return assetType === 'Gold' ? qty.toFixed(2) + 'g' : qty.toFixed(4);
  }

  formatDate(d: string): string {
    return shortDate(d);
  }

  // ---- Value chart ----
  private renderHistoryChart(): void {
    this.historyChart?.destroy();
    const points = this.filteredHistory();
    const canvas = this.historyCanvas()?.nativeElement;
    if (points.length === 0 || !canvas) return;

    const theme = applyChartTheme();
    const axis = axisOptions(theme);
    const xAxis = axisOptions(theme, false);
    const longSpan = spanDays(points) > 120;
    const lastIndex = points.length - 1;

    this.historyChart = new Chart(canvas, {
      type: 'line',
      data: {
        datasets: [
          {
            label: 'Value',
            data: points.map((p) => ({ x: dayValue(p.date), y: p.totalValue })),
            borderColor: theme.credit,
            borderWidth: 3,
            borderCapStyle: 'round',
            backgroundColor: (ctx) => {
              const area = ctx.chart.chartArea;
              if (!area) return withAlpha(theme.credit, 0.1);
              const gradient = ctx.chart.ctx.createLinearGradient(0, area.top, 0, area.bottom);
              gradient.addColorStop(0, withAlpha(theme.credit, 0.25));
              gradient.addColorStop(1, withAlpha(theme.credit, 0));
              return gradient;
            },
            fill: true,
            tension: 0.35,
            pointRadius: points.map((_, i) => (i === lastIndex ? 5 : 0)),
            pointHoverRadius: 5,
            pointBackgroundColor: theme.credit,
            pointBorderColor: theme.surface,
            pointBorderWidth: 2,
          },
          {
            label: 'Money put in',
            data: points.map((p) => ({ x: dayValue(p.date), y: p.invested })),
            borderColor: theme.neutral,
            borderDash: [5, 5],
            borderWidth: 2,
            fill: false,
            stepped: 'after',
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
              title: (items) => shortDate(points[items[0].dataIndex].date),
              label: (ctx) => ` ${ctx.dataset.label}: ${eur(ctx.parsed.y as number)}`,
            },
          },
        },
        scales: {
          // Time-proportional: daily closes and sparse early prices keep their spacing.
          x: {
            ...xAxis,
            type: 'linear',
            min: dayValue(points[0].date),
            max: dayValue(points[points.length - 1].date),
            ticks: {
              ...xAxis.ticks,
              maxTicksLimit: 6,
              maxRotation: 0,
              callback: (_value, index, ticks) => timeTick(ticks, index, longSpan),
            },
          },
          y: {
            ...axis,
            ticks: {
              ...axis.ticks,
              maxTicksLimit: 6,
              callback: (value) => eurTick(Number(value)),
            },
          },
        },
      },
    });
  }
}
