import {
  Component,
  inject,
  signal,
  computed,
  viewChild,
  ElementRef,
  effect,
  OnDestroy,
  ChangeDetectionStrategy,
  Injector,
  afterNextRender,
} from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import {
  Chart,
  ChartOptions,
  ScriptableScaleContext,
  Tooltip,
  Legend,
  BarController,
  BarElement,
  CategoryScale,
  LinearScale,
} from 'chart.js';
import { FinanceService, EnrichedTransaction } from '../../core/services/finance.service';
import { CategoriesService } from '../../core/services/categories.service';
import { GroceriesService } from '../../core/services/groceries.service';
import { GroceryCategoriesService } from '../../core/services/grocery-categories.service';
import { GroceryItem } from '../../core/models/grocery.model';
import { availableMonths } from '../../core/utils/date-utils';
import { CATEGORY_UNKNOWN } from '../../core/constants/categories';
import { MonthScrubberComponent } from '../../core/components/month-scrubber/month-scrubber';
import {
  aggregateByMonth,
  latestClosedMonth,
  monthCells,
  monthKeyOf,
  monthName,
  monthYearLabel,
  previousMonth,
} from '../../core/utils/month-totals';
import { eur, eurAxis, signedEur } from '../../core/utils/money';
import { ChartTheme, applyChartTheme, axisOptions, withAlpha } from '../../core/charts/chart-theme';
import { CategoryBarsComponent } from './category-bars';
import {
  CategoryTotal,
  biggestMoves,
  categoryBars,
  compareText,
  flowWindow,
  mostBought,
  signedPct,
  storeTotals,
  sumByCategory,
  withMonths,
} from './insights';

Chart.register(Tooltip, Legend, BarController, BarElement, CategoryScale, LinearScale);

/** A category without a colour, in templates. Canvas charts here use the theme's series colours. */
const FALLBACK = 'var(--category-fallback)';

const MONTH_WORDS = [
  'Months',
  'One month',
  'Two months',
  'Three months',
  'Four months',
  'Five months',
  'Six months',
];

function txCategory(tx: EnrichedTransaction) {
  return {
    label: tx.category?.name ?? CATEGORY_UNKNOWN,
    color: tx.category?.color || FALLBACK,
    amount: tx.amount,
  };
}

function groceryByCategory(items: GroceryItem[]): CategoryTotal[] {
  return sumByCategory(
    items.map((item) => ({
      label: item.categoryName ?? CATEGORY_UNKNOWN,
      color: item.categoryColor || FALLBACK,
      amount: item.amount * item.quantity,
    })),
  );
}

/** 'Sep 25' for a chart axis. */
function shortMonthYear(key: string): string {
  return `${monthName(key, 'short')} ${key.slice(2, 4)}`;
}

function plural(n: number, word: string): string {
  return `${n} ${word}${n === 1 ? '' : 's'}`;
}

@Component({
  selector: 'app-analytics',
  standalone: true,
  imports: [DatePipe, FormsModule, RouterLink, MonthScrubberComponent, CategoryBarsComponent],
  templateUrl: './analytics.html',
  changeDetection: ChangeDetectionStrategy.Eager,
  styleUrl: './analytics.scss',
})
export class AnalyticsComponent implements OnDestroy {
  finance = inject(FinanceService);
  catSvc = inject(CategoriesService);
  groceriesSvc = inject(GroceriesService);
  groceryCatSvc = inject(GroceryCategoriesService);
  router = inject(Router);
  private injector = inject(Injector);

  readonly eur = eur;
  readonly signedEur = signedEur;
  readonly signedPct = signedPct;
  readonly monthName = monthName;
  readonly unknownLabel = CATEGORY_UNKNOWN;

  private readonly nowKey = monthKeyOf(new Date());

  // Signal queries: effects depending on these re-run when @if branches create the
  // canvases - a synchronous decorator @ViewChild read here is undefined on the very
  // change-detection pass that creates the canvas, leaving charts blank (audit #9).
  trendCanvas = viewChild<ElementRef<HTMLCanvasElement>>('trendCanvas');
  categoryTrendCanvas = viewChild<ElementRef<HTMLCanvasElement>>('categoryTrendCanvas');
  gCategoryTrendCanvas = viewChild<ElementRef<HTMLCanvasElement>>('gCategoryTrendCanvas');
  private detailPanel = viewChild<ElementRef<HTMLElement>>('detailPanel');

  activeTab = signal<'transactions' | 'groceries'>('transactions');

  /** The Spending tab's month, 'YYYY-MM'; '' means all months. */
  filterMonth = signal('');
  /** With all months selected, show an average month instead of the totals. */
  showAverages = signal(false);
  filterCategory = signal('');
  selectedCategory = signal<{
    label: string;
    color: string;
    dominantType: 'credit' | 'debit';
  } | null>(null);
  private defaultApplied = false;
  /** "Six months of flow": money in and out per month. */
  trendChart?: Chart;
  categoryTrendChart?: Chart;

  availableMonths = computed(() => availableMonths(this.finance.allTransactions()));

  /** The scrubber's months, oldest first: every month with money, statements or receipts. */
  months = computed(() =>
    withMonths(monthCells(this.finance.monthlySummaries()), [
      ...this.availableMonths(),
      ...this.gAvailableMonths(),
    ]),
  );

  /** The month the active tab shows; '' for all months. */
  shownMonth = computed(() =>
    this.activeTab() === 'groceries' ? this.gFilterMonth() : this.filterMonth(),
  );

  pageTitle = computed(() => {
    const month = this.shownMonth();
    return month ? `${monthName(month)} insights` : 'Insights';
  });

  isAverages = computed(() => !this.filterMonth() && this.showAverages());

  private monthCount = computed(() => {
    const months = new Set(this.finance.allTransactions().map((tx) => tx.month));
    return Math.max(1, months.size);
  });

  private txFiltered = computed(() => {
    const m = this.filterMonth();
    const txs = this.finance.allTransactions();
    return m ? txs.filter((tx) => tx.month === m) : txs;
  });

  private byCategory(
    type: 'credit' | 'debit',
    txs: EnrichedTransaction[],
    divisor = 1,
  ): CategoryTotal[] {
    return sumByCategory(txs.filter((tx) => tx.type === type).map(txCategory)).map((d) =>
      divisor === 1 ? d : { ...d, total: d.total / divisor },
    );
  }

  spendingData = computed(() =>
    this.byCategory('debit', this.txFiltered(), this.isAverages() ? this.monthCount() : 1),
  );

  incomeData = computed(() =>
    this.byCategory('credit', this.txFiltered(), this.isAverages() ? this.monthCount() : 1),
  );

  totalSpending = computed(() => this.spendingData().reduce((s, d) => s + d.total, 0));
  totalIncome = computed(() => this.incomeData().reduce((s, d) => s + d.total, 0));
  kept = computed(() => this.totalIncome() - this.totalSpending());

  /** The month the selected one is compared with; null for all months. */
  compareMonth = computed(() => (this.filterMonth() ? previousMonth(this.filterMonth()) : null));
  compareName = computed(() => {
    const prev = this.compareMonth();
    return prev ? monthName(prev) : '';
  });

  /** The previous month's spending by category; null with all months or when it had none. */
  private prevSpendingData = computed(() => {
    const prev = this.compareMonth();
    if (!prev) return null;
    const rows = this.byCategory(
      'debit',
      this.finance.allTransactions().filter((tx) => tx.month === prev),
    );
    return rows.length ? rows : null;
  });

  /** Whether "Where it went" compares with the previous month: a month is shown and it had spending. */
  spendingCompared = computed(() => this.prevSpendingData() !== null);

  /** "Where it went, and how it moved": sorted bars with the previous month's tick. */
  spendingBars = computed(() => categoryBars(this.spendingData(), this.prevSpendingData()));

  /** "Biggest moves since <previous month>"; empty with all months. */
  moves = computed(() => {
    const prev = this.prevSpendingData();
    return prev ? biggestMoves(this.spendingData(), prev) : [];
  });

  heroLabel = computed(() => {
    const month = this.filterMonth();
    if (month) return `Out in ${monthName(month)}`;
    return this.isAverages()
      ? 'Out in an average month'
      : `Out over ${plural(this.monthCount(), 'month')}`;
  });

  /** The sentence before "You kept …" under the hero figure. */
  spendingNote = computed(() => {
    const n = this.monthCount();
    if (!this.filterMonth()) {
      return this.isAverages()
        ? `Averaged over ${plural(n, 'month')}.`
        : `About ${eur(this.totalSpending() / n)} a month.`;
    }
    const prev = this.prevSpendingData();
    const text = compareText(
      this.totalSpending(),
      prev ? prev.reduce((s, d) => s + d.total, 0) : null,
      this.compareName(),
    );
    return text ? `${text[0].toUpperCase()}${text.slice(1)}.` : null;
  });

  /** "Where it came from": income by category with its share. */
  incomeSplit = computed(() => {
    const total = this.totalIncome();
    return this.incomeData().map((d) => ({ ...d, share: total > 0 ? (d.total / total) * 100 : 0 }));
  });

  incomeSplitLabel = computed(
    () =>
      'Income by category: ' +
      this.incomeSplit()
        .map((d) => `${d.label} ${d.share.toFixed(1)}%`)
        .join(', '),
  );

  /** Every month, all banks, newest first. */
  private monthTotals = computed(() => aggregateByMonth(this.finance.monthlySummaries()));

  /** Six months up to the selected one, or the last 18 with all months. */
  flowMonths = computed(() =>
    flowWindow(this.monthTotals(), this.filterMonth(), this.filterMonth() ? 6 : 18),
  );

  flowTitle = computed(() => {
    const n = this.flowMonths().length;
    return `${MONTH_WORDS[n] ?? `${n} months`} of flow`;
  });

  flowLabel = computed(
    () =>
      'Money in above the line and money out below it, by month. ' +
      this.flowMonths()
        .map((m) => `${monthYearLabel(m.month)}: in ${eur(m.income)}, out ${eur(m.expenses)}`)
        .join('; '),
  );

  allCategories = computed(() => {
    const map = new Map<string, string>();
    for (const tx of this.finance.allTransactions()) {
      const label = tx.category?.name ?? CATEGORY_UNKNOWN;
      const color = tx.category?.color || FALLBACK;
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
    const currentTotal = currentMonth ? (byMonth.get(currentMonth) ?? 0) : 0;
    const prevTotal = currentMonth ? (byMonth.get(previousMonth(currentMonth)) ?? 0) : null;
    const delta = prevTotal !== null ? currentTotal - prevTotal : null;
    return { avgMonthly, currentTotal, delta };
  });

  gFilterMonth = signal('');
  gFilterCategory = signal('');
  gSelectedCategory = signal<{ label: string; color: string } | null>(null);
  private gDefaultApplied = false;
  gCategoryTrendChart?: Chart;
  gAvailableMonths = computed(() => {
    const months = this.groceriesSvc.countedItems().map((i) => i.receiptDate.slice(0, 7));
    return [...new Set(months)].sort().reverse();
  });

  private gItemsFiltered = computed(() => {
    const m = this.gFilterMonth();
    const items = this.groceriesSvc.countedItems();
    if (!m) return items;
    return items.filter((i) => i.receiptDate.slice(0, 7) === m);
  });

  gSpendingData = computed(() => groceryByCategory(this.gItemsFiltered()));

  gTotalSpending = computed(() => this.gSpendingData().reduce((s, d) => s + d.total, 0));

  gCompareName = computed(() => {
    const m = this.gFilterMonth();
    return m ? monthName(previousMonth(m)) : '';
  });

  /** The previous month's groceries by category; null with all months or when it had none. */
  private gPrevSpendingData = computed(() => {
    const m = this.gFilterMonth();
    if (!m) return null;
    const prev = previousMonth(m);
    const rows = groceryByCategory(
      this.groceriesSvc.countedItems().filter((i) => i.receiptDate.slice(0, 7) === prev),
    );
    return rows.length ? rows : null;
  });

  gCompared = computed(() => this.gPrevSpendingData() !== null);

  gSpendingBars = computed(() => categoryBars(this.gSpendingData(), this.gPrevSpendingData()));

  gStores = computed(() => storeTotals(this.gItemsFiltered()));
  gMostBought = computed(() => mostBought(this.gItemsFiltered()));

  /** "4.8% more than August, read from 12 receipts." */
  gNote = computed(() => {
    const month = this.gFilterMonth();
    const receipts = new Set(this.gItemsFiltered().map((i) => i.receiptId)).size;
    if (receipts === 0) return month ? `No receipts in ${monthName(month)}.` : 'No receipts yet.';
    const read = `read from ${plural(receipts, 'receipt')}`;
    if (!month) {
      const months = this.gAvailableMonths().length;
      return `Over ${plural(months, 'month')}, ${read}.`;
    }
    const prev = this.gPrevSpendingData();
    const text = compareText(
      this.gTotalSpending(),
      prev ? prev.reduce((s, d) => s + d.total, 0) : null,
      this.gCompareName(),
    );
    const sentence = text ? `${text}, ${read}.` : `${read}.`;
    return sentence[0].toUpperCase() + sentence.slice(1);
  });

  gAllCategories = computed(() => {
    const map = new Map<string, string>();
    for (const item of this.groceriesSvc.countedItems()) {
      const label = item.categoryName ?? CATEGORY_UNKNOWN;
      const color = item.categoryColor || FALLBACK;
      if (!map.has(label)) map.set(label, color);
    }
    return [...map.entries()]
      .map(([label, color]) => ({ label, color }))
      .sort((a, b) => a.label.localeCompare(b.label));
  });

  gCategoryTrendData = computed(() => {
    const sel = this.gSelectedCategory();
    if (!sel) return [];
    const map = new Map<string, number>();
    for (const item of this.groceriesSvc.countedItems()) {
      const label = item.categoryName ?? CATEGORY_UNKNOWN;
      if (label !== sel.label) continue;
      const month = item.receiptDate.slice(0, 7);
      map.set(month, (map.get(month) ?? 0) + item.amount * item.quantity);
    }
    return [...map.entries()]
      .sort(([a], [b]) => a.localeCompare(b))
      .slice(-18)
      .map(([month, spending]) => ({ month, spending }));
  });

  gCategoryTopItems = computed(() => {
    const sel = this.gSelectedCategory();
    if (!sel) return [];
    const map = new Map<string, number>();
    for (const item of this.gItemsFiltered()) {
      const label = item.categoryName ?? CATEGORY_UNKNOWN;
      if (label !== sel.label) continue;
      map.set(item.description, (map.get(item.description) ?? 0) + item.amount * item.quantity);
    }
    return [...map.entries()]
      .sort(([, a], [, b]) => b - a)
      .slice(0, 8)
      .map(([description, total]) => ({ description, total }));
  });

  gCategoryPeriodItems = computed(() => {
    const sel = this.gSelectedCategory();
    if (!sel) return [];
    return this.gItemsFiltered().filter(
      (item) => (item.categoryName ?? CATEGORY_UNKNOWN) === sel.label,
    );
  });

  gCategoryStats = computed(() => {
    const sel = this.gSelectedCategory();
    if (!sel) return null;
    const allItems = this.groceriesSvc
      .countedItems()
      .filter((item) => (item.categoryName ?? CATEGORY_UNKNOWN) === sel.label);
    const byMonth = new Map<string, number>();
    for (const item of allItems) {
      const month = item.receiptDate.slice(0, 7);
      byMonth.set(month, (byMonth.get(month) ?? 0) + item.amount * item.quantity);
    }
    const months = [...byMonth.keys()].sort();
    const avgMonthly = months.length
      ? [...byMonth.values()].reduce((s, v) => s + v, 0) / months.length
      : 0;
    const currentMonth = this.gFilterMonth();
    const currentTotal = currentMonth ? (byMonth.get(currentMonth) ?? 0) : 0;
    const prevTotal = currentMonth ? (byMonth.get(previousMonth(currentMonth)) ?? 0) : null;
    const delta = prevTotal !== null ? currentTotal - prevTotal : null;
    return { avgMonthly, currentTotal, delta };
  });

  /** ' in September' or ', all months', after a list's title. */
  periodSuffix(month: string): string {
    return month ? ` in ${monthName(month)}` : ', all months';
  }

  txAmount(tx: EnrichedTransaction): string {
    if (tx.type === 'credit') return signedEur(tx.amount);
    if (tx.type === 'debit') return signedEur(-tx.amount);
    return eur(tx.amount);
  }

  merchantAmount(m: { total: number; type: 'credit' | 'debit' | 'mixed' }): string {
    if (m.type === 'credit') return signedEur(m.total);
    if (m.type === 'debit') return signedEur(-m.total);
    return eur(m.total);
  }

  formatQty(quantity: number): string {
    return Number.isInteger(quantity) ? String(quantity) : quantity.toFixed(2).replace(/0$/, '');
  }

  constructor() {
    effect(() => {
      const months = this.availableMonths();
      if (months.length > 0 && !this.defaultApplied) {
        this.defaultApplied = true;
        this.filterMonth.set(latestClosedMonth(months, this.nowKey));
      }
    });

    effect(() => {
      const months = this.gAvailableMonths();
      if (months.length > 0 && !this.gDefaultApplied) {
        this.gDefaultApplied = true;
        this.gFilterMonth.set(latestClosedMonth(months, this.nowKey));
      }
    });

    effect(() => {
      if (this.activeTab() !== 'transactions') return;
      this.trendCanvas();
      this.flowMonths();
      this.filterMonth();
      this.renderTrendChart();
    });

    effect(() => {
      if (this.activeTab() !== 'transactions') return;
      const sel = this.selectedCategory();
      const data = this.categoryTrendData();
      const canvas = this.categoryTrendCanvas();
      if (!sel || !data.length || !canvas) {
        this.categoryTrendChart?.destroy();
        this.categoryTrendChart = undefined;
        return;
      }
      setTimeout(() => this.renderCategoryTrendChart(), 0);
    });

    effect(() => {
      if (this.activeTab() !== 'groceries') return;
      const sel = this.gSelectedCategory();
      const data = this.gCategoryTrendData();
      const canvas = this.gCategoryTrendCanvas();
      if (!sel || !data.length || !canvas) {
        this.gCategoryTrendChart?.destroy();
        this.gCategoryTrendChart = undefined;
        return;
      }
      setTimeout(() => this.renderGroceryCategoryTrendChart(), 0);
    });
  }

  ngOnDestroy(): void {
    this.trendChart?.destroy();
    this.categoryTrendChart?.destroy();
    this.gCategoryTrendChart?.destroy();
  }

  /** The scrubber (or a month in the flow chart) picks the month for both tabs. */
  selectMonth(key: string): void {
    this.defaultApplied = true;
    this.gDefaultApplied = true;
    this.filterMonth.set(key);
    this.gFilterMonth.set(key);
  }

  selectCategory(label: string, color: string, dominantType: 'credit' | 'debit' = 'debit'): void {
    if (this.selectedCategory()?.label === label) {
      this.clearCategory();
      return;
    }
    this.selectedCategory.set({ label, color, dominantType });
    this.filterCategory.set(label);
    this.revealDetail();
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
      this.filterCategory.set(value);
      this.revealDetail();
    }
  }

  navigateToCategory(label: string, txType: 'credit' | 'debit'): void {
    const month = this.filterMonth();
    const cat = this.catSvc.categories().find((c) => c.name === label);
    const params: Record<string, string> = {};
    if (cat) params['category'] = String(cat.id);
    else if (label === CATEGORY_UNKNOWN) params['category'] = 'unknown';
    if (month) params['month'] = month;
    params['type'] = txType;
    this.router.navigate(['/transactions'], { queryParams: params });
  }

  selectGroceryCategory(label: string, color: string): void {
    if (this.gSelectedCategory()?.label === label) {
      this.clearGroceryCategory();
      return;
    }
    this.gSelectedCategory.set({ label, color });
    this.gFilterCategory.set(label);
    this.revealDetail();
  }

  clearGroceryCategory(): void {
    this.gSelectedCategory.set(null);
    this.gFilterCategory.set('');
  }

  onGroceryCategoryDropdownChange(value: string): void {
    if (!value) {
      this.clearGroceryCategory();
      return;
    }
    const cat = this.gAllCategories().find((c) => c.label === value);
    if (cat) {
      this.gSelectedCategory.set({ label: cat.label, color: cat.color });
      this.gFilterCategory.set(value);
      this.revealDetail();
    }
  }

  navigateToGroceryCategory(label: string): void {
    const month = this.gFilterMonth();
    const cat = this.groceryCatSvc.categories().find((c) => c.name === label);
    const params: Record<string, string> = {};
    if (cat) params['categoryId'] = String(cat.id);
    else if (label === CATEGORY_UNKNOWN) params['categoryId'] = 'unknown';
    if (month) params['month'] = month;
    this.router.navigate(['/transactions'], { queryParams: { ...params, tab: 'groceries' } });
  }

  /** The detail panel may sit below the fold (under the list on phones): bring it into view. */
  private revealDetail(): void {
    afterNextRender(
      () =>
        this.detailPanel()?.nativeElement.scrollIntoView?.({
          behavior: 'smooth',
          block: 'nearest',
        }),
      { injector: this.injector },
    );
  }

  /** Bar chart options shared by the trend charts: themed axes, euro ticks, no legend. */
  private barOptions(theme: ChartTheme): ChartOptions<'bar'> {
    const y = axisOptions(theme);
    return {
      responsive: true,
      maintainAspectRatio: false,
      plugins: {
        legend: { display: false },
        tooltip: {
          callbacks: {
            label: (ctx) => ` ${ctx.dataset.label}: ${eur(ctx.parsed.y as number)}`,
          },
        },
      },
      scales: {
        x: axisOptions(theme, false),
        y: { ...y, ticks: { ...y.ticks, maxTicksLimit: 5, callback: (v) => eurAxis(Number(v)) } },
      },
    };
  }

  /** "Six months of flow": money in above the line, money out below, the selected month bright. */
  private renderTrendChart(): void {
    const canvas = this.trendCanvas()?.nativeElement;
    this.trendChart?.destroy();
    this.trendChart = undefined;
    const rows = this.flowMonths();
    if (!canvas || rows.length === 0) return;

    const theme = applyChartTheme();
    const selected = this.filterMonth();
    const tint = (color: string, month: string) =>
      !selected || month === selected ? color : withAlpha(color, 0.38);
    const years = new Set(rows.map((r) => r.month.slice(0, 4))).size;
    const labels = rows.map((r) => [
      years > 1 ? shortMonthYear(r.month) : monthName(r.month, 'short'),
      signedEur(r.net, true),
    ]);
    const base = this.barOptions(theme);
    const y = axisOptions(theme);

    this.trendChart = new Chart(canvas, {
      type: 'bar',
      data: {
        labels,
        datasets: [
          {
            label: 'Money in',
            data: rows.map((r) => r.income),
            backgroundColor: rows.map((r) => tint(theme.credit, r.month)),
            borderRadius: 6,
            barPercentage: 0.7,
          },
          {
            label: 'Money out',
            data: rows.map((r) => -r.expenses),
            backgroundColor: rows.map((r) => tint(theme.debit, r.month)),
            borderRadius: 6,
            barPercentage: 0.7,
          },
        ],
      },
      options: {
        ...base,
        onClick: (_event, elements) => {
          const row = elements.length ? rows[elements[0].index] : undefined;
          if (row) this.selectMonth(row.month);
        },
        onHover: (event, elements) => {
          const target = event.native?.target as HTMLElement | undefined;
          if (target) target.style.cursor = elements.length ? 'pointer' : 'default';
        },
        plugins: {
          ...base.plugins,
          tooltip: {
            callbacks: {
              title: (items) => monthYearLabel(rows[items[0].dataIndex].month),
              label: (ctx) => ` ${ctx.dataset.label}: ${eur(ctx.parsed.y as number)}`,
            },
          },
        },
        scales: {
          x: { ...axisOptions(theme, false), stacked: true },
          y: {
            ...y,
            stacked: true,
            grid: {
              ...y.grid,
              color: (c: ScriptableScaleContext) =>
                c.tick?.value === 0 ? theme.neutral : theme.grid,
            },
            ticks: {
              ...y.ticks,
              maxTicksLimit: 6,
              callback: (v) => eurAxis(Math.abs(Number(v))),
            },
          },
        },
      },
    });
  }

  private renderCategoryTrendChart(): void {
    const canvas = this.categoryTrendCanvas()?.nativeElement;
    if (!canvas) return;

    this.categoryTrendChart?.destroy();

    const data = this.categoryTrendData();
    const theme = applyChartTheme();

    this.categoryTrendChart = new Chart(canvas, {
      type: 'bar',
      data: {
        labels: data.map((d) => shortMonthYear(d.month)),
        datasets: [
          {
            label: 'Spending',
            data: data.map((d) => d.spending),
            backgroundColor: theme.debit,
            borderRadius: 4,
          },
          {
            label: 'Income',
            data: data.map((d) => d.income),
            backgroundColor: theme.credit,
            borderRadius: 4,
          },
        ],
      },
      options: this.barOptions(theme),
    });
  }

  private renderGroceryCategoryTrendChart(): void {
    const canvas = this.gCategoryTrendCanvas()?.nativeElement;
    if (!canvas) return;

    this.gCategoryTrendChart?.destroy();

    const data = this.gCategoryTrendData();
    const theme = applyChartTheme();

    this.gCategoryTrendChart = new Chart(canvas, {
      type: 'bar',
      data: {
        labels: data.map((d) => shortMonthYear(d.month)),
        datasets: [
          {
            label: 'Spending',
            data: data.map((d) => d.spending),
            backgroundColor: theme.debit,
            borderRadius: 4,
          },
        ],
      },
      options: this.barOptions(theme),
    });
  }
}
