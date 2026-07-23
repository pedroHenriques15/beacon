import {
  Component,
  ElementRef,
  ViewChild,
  AfterViewInit,
  OnDestroy,
  signal,
  computed,
  effect,
  inject,
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
} from 'chart.js';
import { InvestmentsService } from '../../core/services/investments.service';
import { InvestmentAsset, InvestmentLot, InvestmentPriceSnapshot } from '../../core/models/statement.model';

Chart.register(ArcElement, DoughnutController, LineController, LineElement, PointElement, CategoryScale, LinearScale, Tooltip, Legend, Filler);

@Component({
  selector: 'app-investments',
  standalone: true,
  imports: [CurrencyPipe, DecimalPipe, NgClass, FormsModule],
  templateUrl: './investments.html',
  styleUrl: './investments.scss',
})
export class InvestmentsComponent implements AfterViewInit, OnDestroy {
  svc = inject(InvestmentsService);

  assets       = this.svc.assets;
  loading      = this.svc.loading;
  error        = this.svc.error;
  assetMetrics = this.svc.assetMetrics;
  totalValue   = this.svc.totalCurrentValue;
  totalCost    = this.svc.totalCostBasis;
  totalPnl     = this.svc.totalUnrealizedPnl;
  totalPct     = this.svc.totalUnrealizedPct;

  readonly Math = Math;

  activeTab = signal<'all' | 'ETF' | 'Gold'>('all');
  expandedAssetId = signal<number | null>(null);
  fetchingAssetId = signal<number | null>(null);
  fetchError = signal<string | null>(null);

  filteredMetrics = computed(() => {
    const tab = this.activeTab();
    const metrics = this.assetMetrics();
    return tab === 'all' ? metrics : metrics.filter(m => m.asset.assetType === tab);
  });

  // ---- Asset modal ----
  showAssetModal = signal(false);
  assetModalMode = signal<'create' | 'edit'>('create');
  editingAssetId = signal<number | null>(null);
  modalAssetType = signal<'ETF' | 'Gold'>('ETF');
  modalTicker    = signal('');
  modalAssetName = signal('');
  modalAssetNotes = signal('');
  assetSaving    = signal(false);
  assetError     = signal('');

  // ---- Lot modal ----
  showLotModal   = signal(false);
  lotModalMode   = signal<'buy' | 'sell' | 'edit'>('buy');
  editingLotId   = signal<number | null>(null);
  lotAssetId     = signal<number | null>(null);
  lotDate        = signal('');
  lotQuantity    = signal<number | null>(null);
  lotPrice       = signal<number | null>(null);
  lotFees        = signal<number | null>(null);
  lotNotes       = signal('');
  lotSaving      = signal(false);
  lotError       = signal('');

  lotAssetType = computed(() =>
    this.assets().find(a => a.id === this.lotAssetId())?.assetType ?? 'ETF'
  );
  lotUnitLabel = computed(() => this.lotAssetType() === 'Gold' ? 'Grams' : 'Shares');
  lotModalTitle = computed(() => {
    const mode = this.lotModalMode();
    if (mode === 'edit') return 'Edit Entry';
    const asset = this.assets().find(a => a.id === this.lotAssetId());
    const label = asset?.ticker ?? asset?.name ?? '';
    return mode === 'buy' ? `Buy — ${label}` : `Sell — ${label}`;
  });

  // ---- Price modal ----
  showPriceModal = signal(false);
  priceAssetId   = signal<number | null>(null);
  priceDate      = signal('');
  priceValue     = signal<number | null>(null);
  priceSaving    = signal(false);
  priceError     = signal('');
  priceAssetLabel = computed(() => {
    const a = this.assets().find(x => x.id === this.priceAssetId());
    return a?.ticker ?? a?.name ?? '';
  });

  // ---- Charts ----
  @ViewChild('allocationCanvas') allocationCanvas!: ElementRef<HTMLCanvasElement>;
  @ViewChild('historyCanvas')    historyCanvas!:    ElementRef<HTMLCanvasElement>;
  private allocationChart?: Chart;
  private historyChart?: Chart;
  private canvasReady = signal(false);

  constructor() {
    effect(() => {
      if (!this.canvasReady()) return;
      this.svc.allocationData();
      this.renderAllocationChart();
    });
    effect(() => {
      if (!this.canvasReady()) return;
      this.svc.portfolioHistory();
      this.renderHistoryChart();
    });
  }

  ngAfterViewInit(): void { this.canvasReady.set(true); }

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
    if (!this.modalAssetName().trim()) { this.assetError.set('Name is required.'); return; }
    if (this.modalAssetType() === 'ETF' && !this.modalTicker().trim()) { this.assetError.set('Ticker is required for ETFs.'); return; }

    this.assetSaving.set(true);
    this.assetError.set('');
    const body = { assetType: this.modalAssetType(), ticker: this.modalTicker() || undefined, name: this.modalAssetName().trim(), notes: this.modalAssetNotes().trim() || undefined };
    const req$ = this.assetModalMode() === 'create'
      ? this.svc.createAsset(body)
      : this.svc.updateAsset(this.editingAssetId()!, { ticker: body.ticker, name: body.name, notes: body.notes });

    req$.subscribe({
      next: () => { this.showAssetModal.set(false); this.assetSaving.set(false); this.svc.load(); },
      error: (err) => { this.assetError.set(err?.error ?? 'Failed to save asset.'); this.assetSaving.set(false); },
    });
  }

  deleteAsset(asset: InvestmentAsset): void {
    if (!confirm(`Delete "${asset.name}" and all its data?`)) return;
    this.svc.deleteAsset(asset.id).subscribe({ next: () => this.svc.load() });
  }

  // ---- Lot modal ----
  openCreateLot(assetId: number, mode: 'buy' | 'sell'): void {
    this.lotModalMode.set(mode);
    this.editingLotId.set(null);
    this.lotAssetId.set(assetId);
    this.lotDate.set(new Date().toISOString().slice(0, 10));
    this.lotQuantity.set(null);
    this.lotPrice.set(null);
    this.lotFees.set(null);
    this.lotNotes.set('');
    this.lotError.set('');
    this.showLotModal.set(true);
  }

  openEditLot(lot: InvestmentLot, assetId: number): void {
    this.lotModalMode.set('edit');
    this.editingLotId.set(lot.id);
    this.lotAssetId.set(assetId);
    this.lotDate.set(lot.date);
    this.lotQuantity.set(Math.abs(lot.quantity));
    this.lotPrice.set(lot.pricePerUnit);
    this.lotFees.set(lot.fees);
    this.lotNotes.set(lot.notes ?? '');
    this.lotError.set('');
    this.showLotModal.set(true);
  }

  submitLot(): void {
    const qty = this.lotQuantity();
    const price = this.lotPrice();
    if (!qty || qty <= 0) { this.lotError.set('Quantity must be greater than zero.'); return; }
    if (!price || price <= 0) { this.lotError.set('Price must be greater than zero.'); return; }
    if (!this.lotDate()) { this.lotError.set('Date is required.'); return; }

    const mode = this.lotModalMode();
    const finalQty = mode === 'sell' ? -qty : qty;

    this.lotSaving.set(true);
    this.lotError.set('');

    const req$ = mode === 'edit'
      ? this.svc.updateLot(this.editingLotId()!, { date: this.lotDate(), quantity: finalQty, pricePerUnit: price, fees: this.lotFees(), notes: this.lotNotes() || null })
      : this.svc.createLot({ assetId: this.lotAssetId()!, date: this.lotDate(), quantity: finalQty, pricePerUnit: price, fees: this.lotFees(), notes: this.lotNotes() || null });

    req$.subscribe({
      next: () => { this.showLotModal.set(false); this.lotSaving.set(false); this.svc.load(); },
      error: (err) => { this.lotError.set(err?.error ?? 'Failed to save entry.'); this.lotSaving.set(false); },
    });
  }

  deleteLot(lot: InvestmentLot): void {
    if (!confirm('Delete this entry?')) return;
    this.svc.deleteLot(lot.id).subscribe({ next: () => this.svc.load() });
  }

  // ---- Price modal ----
  openPriceModal(assetId: number): void {
    this.priceAssetId.set(assetId);
    this.priceDate.set(new Date().toISOString().slice(0, 10));
    this.priceValue.set(null);
    this.priceError.set('');
    this.showPriceModal.set(true);
  }

  submitPrice(): void {
    const val = this.priceValue();
    if (!val || val <= 0) { this.priceError.set('Price must be greater than zero.'); return; }
    this.priceSaving.set(true);
    this.priceError.set('');
    this.svc.upsertPrice({ assetId: this.priceAssetId()!, date: this.priceDate(), pricePerUnit: val }).subscribe({
      next: () => { this.showPriceModal.set(false); this.priceSaving.set(false); this.svc.load(); },
      error: () => { this.priceError.set('Failed to save price.'); this.priceSaving.set(false); },
    });
  }

  deletePrice(snap: InvestmentPriceSnapshot): void {
    if (!confirm('Delete this price snapshot?')) return;
    this.svc.deletePrice(snap.id).subscribe({ next: () => this.svc.load() });
  }

  // ---- Fetch price (Alpha Vantage) ----
  fetchPrice(asset: InvestmentAsset): void {
    this.fetchingAssetId.set(asset.id);
    this.fetchError.set(null);
    this.svc.fetchPrice(asset.id).subscribe({
      next: () => { this.fetchingAssetId.set(null); this.svc.load(); },
      error: (err) => { this.fetchingAssetId.set(null); this.fetchError.set(err?.error ?? 'Failed to fetch price.'); },
    });
  }

  toggleExpand(id: number): void {
    this.expandedAssetId.update(cur => cur === id ? null : id);
  }

  // ---- Helpers ----
  pnlClass(v: number | null): string {
    if (v == null) return '';
    return v >= 0 ? 'positive' : 'negative';
  }

  formatQty(qty: number, assetType: string): string {
    return assetType === 'Gold' ? qty.toFixed(2) + 'g' : qty.toFixed(4);
  }

  formatDate(d: string): string {
    return new Date(d).toLocaleDateString('en-GB', { day: '2-digit', month: 'short', year: 'numeric' });
  }

  // ---- Charts ----
  private renderAllocationChart(): void {
    this.allocationChart?.destroy();
    const data = this.svc.allocationData();
    if (data.length === 0 || !this.allocationCanvas?.nativeElement) return;

    this.allocationChart = new Chart(this.allocationCanvas.nativeElement, {
      type: 'doughnut',
      data: {
        labels: data.map(d => d.label),
        datasets: [{ data: data.map(d => d.value), backgroundColor: data.map(d => d.color), borderWidth: 2, borderColor: '#111827' }],
      },
      options: {
        responsive: true,
        cutout: '62%',
        plugins: {
          legend: { position: 'right', labels: { color: '#94a3b8', padding: 14, font: { size: 11 } } },
          tooltip: { callbacks: { label: ctx => ` ${ctx.label}: €${(ctx.parsed as number).toLocaleString('en-GB', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}` } },
        },
      },
    });
  }

  private renderHistoryChart(): void {
    this.historyChart?.destroy();
    const points = this.svc.portfolioHistory();
    if (points.length === 0 || !this.historyCanvas?.nativeElement) return;

    const canvas = this.historyCanvas.nativeElement;
    const ctx = canvas.getContext('2d')!;
    const gradient = ctx.createLinearGradient(0, 0, 0, 200);
    gradient.addColorStop(0, 'rgba(99,102,241,0.35)');
    gradient.addColorStop(1, 'rgba(99,102,241,0.0)');

    this.historyChart = new Chart(canvas, {
      type: 'line',
      data: {
        labels: points.map(p => this.formatDate(p.date)),
        datasets: [{
          data: points.map(p => p.totalValue),
          borderColor: '#6366f1',
          backgroundColor: gradient,
          fill: true,
          tension: 0.35,
          pointRadius: 3,
          pointHoverRadius: 5,
          pointBackgroundColor: '#6366f1',
        }],
      },
      options: {
        responsive: true,
        plugins: {
          legend: { display: false },
          tooltip: { callbacks: { label: ctx => ` €${(ctx.parsed.y as number).toLocaleString('en-GB', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}` } },
        },
        scales: {
          x: { grid: { color: 'rgba(30,45,66,0.8)' }, ticks: { color: '#64748b', font: { size: 11 } } },
          y: { grid: { color: 'rgba(30,45,66,0.8)' }, ticks: { color: '#64748b', font: { size: 11 }, callback: v => `€${(v as number).toLocaleString()}` } },
        },
      },
    });
  }
}
