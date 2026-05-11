import {
  Component,
  inject,
  signal,
  computed,
  ViewChild,
  ElementRef,
  effect,
  OnDestroy,
} from '@angular/core';
import { CurrencyPipe, SlicePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import {
  Chart,
  ArcElement,
  PieController,
  Tooltip,
  Legend,
  BarController,
  BarElement,
  CategoryScale,
  LinearScale,
} from 'chart.js';
import { FinanceService } from '../../core/services/finance.service';
import { CategoriesService } from '../../core/services/categories.service';
import { availableMonths } from '../../core/utils/date-utils';
import { CATEGORY_UNKNOWN } from '../../core/constants/categories';

Chart.register(
  ArcElement,
  PieController,
  Tooltip,
  Legend,
  BarController,
  BarElement,
  CategoryScale,
  LinearScale,
);

@Component({
  selector: 'app-analytics',
  standalone: true,
  imports: [CurrencyPipe, SlicePipe, FormsModule],
  templateUrl: './analytics.html',
  styleUrl: './analytics.scss',
})
export class AnalyticsComponent implements OnDestroy {
  finance = inject(FinanceService);
  catSvc = inject(CategoriesService);
  router = inject(Router);

  @ViewChild('spendingCanvas') spendingCanvas!: ElementRef<HTMLCanvasElement>;
  @ViewChild('incomeCanvas') incomeCanvas!: ElementRef<HTMLCanvasElement>;
  @ViewChild('trendCanvas') trendCanvas!: ElementRef<HTMLCanvasElement>;
  @ViewChild('categoryTrendCanvas') categoryTrendCanvas?: ElementRef<HTMLCanvasElement>;

  filterMonth = signal('');
  filterCategory = signal('');
  selectedCategory = signal<{
    label: string;
    color: string;
    dominantType: 'credit' | 'debit';
  } | null>(null);
  private defaultApplied = false;
  spendingChart?: Chart;
  incomeChart?: Chart;
  trendChart?: Chart;
  categoryTrendChart?: Chart;
  private canvasReady = signal(false);

  availableMonths = computed(() => availableMonths(this.finance.allTransactions()));

  private isAverages = computed(() => this.filterMonth() === '__averages__');

  private monthCount = computed(() => {
    const months = new Set(this.finance.allTransactions().map((tx) => tx.month));
    return Math.max(1, months.size);
  });

  private txFiltered = computed(() => {
    const m = this.filterMonth();
    const txs = this.finance.allTransactions();
    if (!m || m === '__averages__') return txs;
    return txs.filter((tx) => tx.month === m);
  });

  spendingData = computed(() => {
    const map = new Map<string, { label: string; color: string; total: number }>();
    for (const tx of this.txFiltered().filter((tx) => tx.type === 'debit')) {
      const key = tx.category?.name ?? CATEGORY_UNKNOWN;
      const color = tx.category?.color ?? '#475569';
      const cur = map.get(key) ?? { label: key, color, total: 0 };
      map.set(key, { ...cur, total: cur.total + tx.amount });
    }
    const divisor = this.isAverages() ? this.monthCount() : 1;
    return [...map.values()]
      .map((d) => ({ ...d, total: d.total / divisor }))
      .sort((a, b) => b.total - a.total);
  });

  incomeData = computed(() => {
    const map = new Map<string, { label: string; color: string; total: number }>();
    for (const tx of this.txFiltered().filter((tx) => tx.type === 'credit')) {
      const key = tx.category?.name ?? CATEGORY_UNKNOWN;
      const color = tx.category?.color ?? '#475569';
      const cur = map.get(key) ?? { label: key, color, total: 0 };
      map.set(key, { ...cur, total: cur.total + tx.amount });
    }
    const divisor = this.isAverages() ? this.monthCount() : 1;
    return [...map.values()]
      .map((d) => ({ ...d, total: d.total / divisor }))
      .sort((a, b) => b.total - a.total);
  });

  totalSpending = computed(() => this.spendingData().reduce((s, d) => s + d.total, 0));
  totalIncome = computed(() => this.incomeData().reduce((s, d) => s + d.total, 0));

  allCategories = computed(() => {
    const map = new Map<string, string>();
    for (const tx of this.finance.allTransactions()) {
      const label = tx.category?.name ?? CATEGORY_UNKNOWN;
      const color = tx.category?.color ?? '#475569';
      if (!map.has(label)) map.set(label, color);
    }
    return [...map.entries()]
      .map(([label, color]) => ({ label, color }))
      .sort((a, b) => a.label.localeCompare(b.label));
  });

  categoryTrendData = computed(() => {
    const sel = this.selectedCategory();
    if (!sel) return [];
    const map = new Map<string, { spending: number; income: number }>();
    for (const tx of this.finance.allTransactions()) {
      const label = tx.category?.name ?? CATEGORY_UNKNOWN;
      if (label !== sel.label) continue;
      const cur = map.get(tx.month) ?? { spending: 0, income: 0 };
      if (tx.type === 'debit') cur.spending += tx.amount;
      else if (tx.type === 'credit') cur.income += tx.amount;
      map.set(tx.month, cur);
    }
    return [...map.entries()]
      .sort(([a], [b]) => a.localeCompare(b))
      .slice(-18)
      .map(([month, data]) => ({ month, ...data }));
  });

  categoryTopMerchants = computed(() => {
    const sel = this.selectedCategory();
    if (!sel) return [];
    const map = new Map<string, { total: number; type: 'credit' | 'debit' | 'mixed' }>();
    for (const tx of this.txFiltered()) {
      const label = tx.category?.name ?? CATEGORY_UNKNOWN;
      if (label !== sel.label) continue;
      const existing = map.get(tx.description);
      if (!existing) {
        map.set(tx.description, { total: tx.amount, type: tx.type as 'credit' | 'debit' });
      } else {
        map.set(tx.description, {
          total: existing.total + tx.amount,
          type: existing.type === tx.type ? existing.type : 'mixed',
        });
      }
    }
    return [...map.entries()]
      .sort(([, a], [, b]) => b.total - a.total)
      .slice(0, 8)
      .map(([description, { total, type }]) => ({ description, total, type }));
  });

  categoryPeriodTransactions = computed(() => {
    const sel = this.selectedCategory();
    if (!sel) return [];
    return this.txFiltered().filter((tx) => (tx.category?.name ?? CATEGORY_UNKNOWN) === sel.label);
  });

  categoryStats = computed(() => {
    const sel = this.selectedCategory();
    if (!sel) return null;
    const allTx = this.finance
      .allTransactions()
      .filter(
        (tx) =>
          (tx.category?.name ?? CATEGORY_UNKNOWN) === sel.label && tx.type === sel.dominantType,
      );
    const byMonth = new Map<string, number>();
    for (const tx of allTx) byMonth.set(tx.month, (byMonth.get(tx.month) ?? 0) + tx.amount);
    const months = [...byMonth.keys()].sort();
    const avgMonthly = months.length
      ? [...byMonth.values()].reduce((s, v) => s + v, 0) / months.length
      : 0;
    const currentMonth = this.filterMonth();
    const currentTotal =
      currentMonth && currentMonth !== '__averages__' ? (byMonth.get(currentMonth) ?? 0) : 0;
    const prevMonth =
      currentMonth && currentMonth !== '__averages__'
        ? (() => {
            const [y, m] = currentMonth.split('-').map(Number);
            const d = new Date(y, m - 2, 1);
            return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}`;
          })()
        : undefined;
    const prevTotal = prevMonth !== undefined ? (byMonth.get(prevMonth) ?? 0) : null;
    const delta = prevTotal !== null ? currentTotal - prevTotal : null;
    return { avgMonthly, currentTotal, delta };
  });

  trendData = computed(() => {
    const map = new Map<string, { income: number; expenses: number }>();
    for (const s of this.finance.monthlySummaries()) {
      const cur = map.get(s.month) ?? { income: 0, expenses: 0 };
      map.set(s.month, { income: cur.income + s.income, expenses: cur.expenses + s.expenses });
    }
    return [...map.entries()]
      .sort(([a], [b]) => a.localeCompare(b))
      .slice(-18)
      .map(([month, data]) => ({ month, ...data }));
  });

  formatMonth(m: string): string {
    const [y, mo] = m.split('-');
    return new Date(+y, +mo - 1, 1).toLocaleString('default', { month: 'long', year: 'numeric' });
  }

  constructor() {
    effect(() => {
      const months = this.availableMonths();
      if (months.length > 0 && !this.defaultApplied) {
        this.defaultApplied = true;
        this.filterMonth.set(months[0]);
      }
    });

    effect(() => {
      if (!this.canvasReady()) return;
      const spending = this.spendingData();
      const income = this.incomeData();
      this.selectedCategory();
      this.renderChart('spending', spending);
      this.renderChart('income', income);
      this.renderTrendChart();
    });

    effect(() => {
      const sel = this.selectedCategory();
      const data = this.categoryTrendData();
      if (!sel) {
        this.categoryTrendChart?.destroy();
        this.categoryTrendChart = undefined;
        setTimeout(() => this.renderTrendChart(), 0);
        return;
      }
      if (!data.length) {
        this.categoryTrendChart?.destroy();
        this.categoryTrendChart = undefined;
        return;
      }
      setTimeout(() => this.renderCategoryTrendChart(), 0);
    });
  }

  ngAfterViewInit(): void {
    this.canvasReady.set(true);
  }

  ngOnDestroy(): void {
    this.spendingChart?.destroy();
    this.incomeChart?.destroy();
    this.trendChart?.destroy();
    this.categoryTrendChart?.destroy();
  }

  selectCategory(label: string, color: string, dominantType: 'credit' | 'debit' = 'debit'): void {
    if (this.selectedCategory()?.label === label) {
      this.clearCategory();
      return;
    }
    this.selectedCategory.set({ label, color, dominantType });
    this.filterCategory.set(label);
  }

  clearCategory(): void {
    this.selectedCategory.set(null);
    this.filterCategory.set('');
  }

  onCategoryDropdownChange(value: string): void {
    if (!value) {
      this.clearCategory();
      return;
    }
    const cat = this.allCategories().find((c) => c.label === value);
    if (cat) {
      const txs = this.finance
        .allTransactions()
        .filter((tx) => (tx.category?.name ?? CATEGORY_UNKNOWN) === value);
      const credits = txs.filter((tx) => tx.type === 'credit').length;
      const dominantType: 'credit' | 'debit' = credits > txs.length / 2 ? 'credit' : 'debit';
      this.selectedCategory.set({ label: cat.label, color: cat.color, dominantType });
    }
  }

  navigateToCategory(label: string, txType: 'credit' | 'debit'): void {
    const month = this.filterMonth();
    const cat = this.catSvc.categories().find((c) => c.name === label);
    const params: Record<string, string> = {};
    if (cat) params['category'] = String(cat.id);
    else if (label === 'Unknown') params['category'] = 'unknown';
    if (month && month !== '__averages__') params['month'] = month;
    params['type'] = txType;
    this.router.navigate(['/transactions'], { queryParams: params });
  }

  highlightSlice(chartType: 'spending' | 'income', label: string): void {
    const chart = chartType === 'spending' ? this.spendingChart : this.incomeChart;
    if (!chart) return;
    const data = chartType === 'spending' ? this.spendingData() : this.incomeData();
    const idx = data.findIndex((d) => d.label === label);
    if (idx === -1) return;
    chart.setActiveElements([{ datasetIndex: 0, index: idx }]);
    chart.tooltip?.setActiveElements([{ datasetIndex: 0, index: idx }], { x: 0, y: 0 });
    chart.update('none');
  }

  clearHighlight(chartType: 'spending' | 'income'): void {
    const chart = chartType === 'spending' ? this.spendingChart : this.incomeChart;
    if (!chart) return;
    chart.setActiveElements([]);
    chart.tooltip?.setActiveElements([], { x: 0, y: 0 });
    chart.update('none');
  }

  private renderChart(
    type: 'spending' | 'income',
    data: { label: string; color: string; total: number }[],
  ): void {
    const canvas =
      type === 'spending' ? this.spendingCanvas?.nativeElement : this.incomeCanvas?.nativeElement;
    if (!canvas) return;

    const existing = type === 'spending' ? this.spendingChart : this.incomeChart;
    existing?.destroy();

    const txType = type === 'spending' ? 'debit' : 'credit';
    const sel = this.selectedCategory();

    const chart = new Chart(canvas, {
      type: 'pie',
      data: {
        labels: data.map((d) => d.label),
        datasets: [
          {
            data: data.map((d) => d.total),
            backgroundColor: data.map((d) => {
              if (!sel || d.label === sel.label) return d.color + 'cc';
              return d.color + '88';
            }),
            borderColor: data.map((d) => {
              if (!sel || d.label === sel.label) return d.color;
              return d.color + 'aa';
            }),
            borderWidth: 1.5,
          },
        ],
      },
      options: {
        responsive: true,
        maintainAspectRatio: true,
        onClick: (_event, elements) => {
          if (!elements.length) return;
          const item = data[elements[0].index];
          if (item) this.selectCategory(item.label, item.color, txType);
        },
        plugins: {
          legend: {
            display: false,
          },
          tooltip: {
            callbacks: {
              label: (ctx) => {
                const val = ctx.parsed as number;
                return ` ${ctx.label}: €${val.toFixed(2)}`;
              },
            },
          },
        },
      },
      plugins: [
        {
          id: 'selectionIndicator',
          afterDraw: (chart) => {
            const selected = this.selectedCategory();
            if (!selected) return;
            const idx = data.findIndex((d) => d.label === selected.label);
            if (idx === -1) return;
            const meta = chart.getDatasetMeta(0);
            const arc = meta.data[idx] as any;
            if (!arc) return;
            const { x, y, startAngle, endAngle, outerRadius } = arc;
            const ctx = chart.ctx;
            ctx.save();
            ctx.beginPath();
            ctx.moveTo(x, y);
            ctx.arc(x, y, outerRadius, startAngle, endAngle);
            ctx.closePath();
            ctx.strokeStyle = 'white';
            ctx.lineWidth = 2;
            ctx.stroke();
            ctx.restore();
          },
        },
      ],
    });

    if (type === 'spending') this.spendingChart = chart;
    else this.incomeChart = chart;
  }

  private renderTrendChart(): void {
    const canvas = this.trendCanvas?.nativeElement;
    if (!canvas) return;

    this.trendChart?.destroy();

    const data = this.trendData();
    const labels = data.map((d) => {
      const [y, m] = d.month.split('-');
      return new Date(+y, +m - 1, 1).toLocaleString('default', { month: 'short', year: '2-digit' });
    });

    this.trendChart = new Chart(canvas, {
      type: 'bar',
      data: {
        labels,
        datasets: [
          {
            label: 'Income',
            data: data.map((d) => d.income),
            backgroundColor: 'rgba(52, 211, 153, 0.5)',
            borderColor: '#34d399',
            borderWidth: 1.5,
            borderRadius: 3,
          },
          {
            label: 'Expenses',
            data: data.map((d) => d.expenses),
            backgroundColor: 'rgba(248, 113, 113, 0.5)',
            borderColor: '#f87171',
            borderWidth: 1.5,
            borderRadius: 3,
          },
        ],
      },
      options: {
        responsive: true,
        maintainAspectRatio: true,
        plugins: {
          legend: { display: false },
          tooltip: {
            callbacks: {
              label: (ctx) => ` ${ctx.dataset.label}: €${(ctx.parsed.y as number).toFixed(2)}`,
            },
          },
        },
        scales: {
          x: {
            grid: { color: 'rgba(30, 45, 66, 0.8)' },
            ticks: { color: '#64748b', font: { size: 11 } },
          },
          y: {
            grid: { color: 'rgba(30, 45, 66, 0.8)' },
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

  private renderCategoryTrendChart(): void {
    const canvas = this.categoryTrendCanvas?.nativeElement;
    if (!canvas) return;

    this.categoryTrendChart?.destroy();

    const data = this.categoryTrendData();
    const labels = data.map((d) => {
      const [y, m] = d.month.split('-');
      return new Date(+y, +m - 1, 1).toLocaleString('default', { month: 'short', year: '2-digit' });
    });

    this.categoryTrendChart = new Chart(canvas, {
      type: 'bar',
      data: {
        labels,
        datasets: [
          {
            label: 'Spending',
            data: data.map((d) => d.spending),
            backgroundColor: 'rgba(248, 113, 113, 0.5)',
            borderColor: '#f87171',
            borderWidth: 1.5,
            borderRadius: 3,
          },
          {
            label: 'Income',
            data: data.map((d) => d.income),
            backgroundColor: 'rgba(52, 211, 153, 0.5)',
            borderColor: '#34d399',
            borderWidth: 1.5,
            borderRadius: 3,
          },
        ],
      },
      options: {
        responsive: true,
        maintainAspectRatio: true,
        plugins: {
          legend: { display: false },
          tooltip: {
            callbacks: {
              label: (ctx) => ` ${ctx.dataset.label}: €${(ctx.parsed.y as number).toFixed(2)}`,
            },
          },
        },
        scales: {
          x: {
            grid: { color: 'rgba(30, 45, 66, 0.8)' },
            ticks: { color: '#64748b', font: { size: 11 } },
          },
          y: {
            grid: { color: 'rgba(30, 45, 66, 0.8)' },
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
