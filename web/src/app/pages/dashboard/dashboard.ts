import {
  Component,
  inject,
  signal,
  computed,
  effect,
  linkedSignal,
  ChangeDetectionStrategy,
} from '@angular/core';
import { DatePipe } from '@angular/common';
import { Router, RouterLink } from '@angular/router';
import { FinanceService } from '../../core/services/finance.service';
import { InvestmentsService, valueChange } from '../../core/services/investments.service';
import { MonthScrubberComponent } from '../../core/components/month-scrubber/month-scrubber';
import { CATEGORY_UNKNOWN } from '../../core/constants/categories';
import { categoryNet, spendingByCategory } from '../../core/utils/category-net';
import {
  aggregateByMonth,
  daysInMonth,
  keptShare,
  keptShareText,
  latestClosedMonth,
  monthCells,
  monthKeyOf,
  monthName,
  monthYearLabel,
  monthsUpTo,
  previousMonth,
} from '../../core/utils/month-totals';
import { eur, signedEur } from '../../core/utils/money';
import { bankInitials } from '../../core/utils/bank';
import { RiverChartComponent } from './river-chart';
import { riverSeries, sparkline } from './river';
import { windowTops } from './six-months';

function lastDayOf(month: string): string {
  return `${month}-${String(daysInMonth(month)).padStart(2, '0')}`;
}

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [DatePipe, RouterLink, MonthScrubberComponent, RiverChartComponent],
  templateUrl: './dashboard.html',
  changeDetection: ChangeDetectionStrategy.Eager,
  styleUrl: './dashboard.scss',
})
export class DashboardComponent {
  finance = inject(FinanceService);
  investments = inject(InvestmentsService);
  private router = inject(Router);

  readonly eur = eur;
  readonly signedEur = signedEur;
  readonly monthName = monthName;
  readonly bankInitials = bankInitials;
  readonly keptShareText = keptShareText;
  readonly unknownLabel = CATEGORY_UNKNOWN;

  private readonly today = new Date();
  private readonly todayIso = `${monthKeyOf(this.today)}-${String(this.today.getDate()).padStart(2, '0')}`;

  selectedBank = signal<string | null>(null);

  private cardOrder = signal<string[]>([]);
  draggedBank = signal<string | null>(null);
  dragOverBank = signal<string | null>(null);

  bankCards = computed(() => {
    const allStatements = this.finance.statements();
    return [...this.finance.latestPerBank().entries()].map(([bank, s]) => ({
      bank,
      initials: bankInitials(bank),
      balance: s.closingBalance,
      periodTo: s.periodTo,
      txCount: allStatements
        .filter((stmt) => stmt.bank === bank)
        .reduce((sum, stmt) => sum + stmt.transactions.length, 0),
    }));
  });

  orderedCards = computed(() => {
    const order = this.cardOrder();
    const cards = this.bankCards();
    if (order.length === 0) return cards;
    const cardMap = new Map(cards.map((c) => [c.bank, c]));
    const ordered = order.map((b) => cardMap.get(b)).filter(Boolean) as typeof cards;
    for (const card of cards) {
      if (!order.includes(card.bank)) ordered.push(card);
    }
    return ordered;
  });

  constructor() {
    effect(() => {
      const cards = this.bankCards();
      if (cards.length > 0 && this.cardOrder().length === 0) {
        this.cardOrder.set(cards.map((c) => c.bank));
      }
    });
    // A year of prices for the Investments sparkline, plus the month before for net worth.
    const from = new Date(this.today.getFullYear() - 1, this.today.getMonth() - 1, 1);
    this.investments.loadPriceHistory(`${monthKeyOf(from)}-01`);
  }

  /** Every month with counted money, all banks, newest first. */
  private allBankTotals = computed(() => this.finance.monthTotals());

  /** The month scrubber's cells, oldest first. */
  months = computed(() => monthCells(this.finance.monthTotals()));

  private defaultMonth = computed(() =>
    latestClosedMonth(
      this.allBankTotals().map((r) => r.month),
      monthKeyOf(this.today),
    ),
  );

  /**
   * The month the page shows; starts at the latest closed month, the scrubber changes it. A
   * reload keeps the choice unless the default month itself moves.
   */
  selectedMonth = linkedSignal(() => this.defaultMonth());

  /** Monthly rows for "Last six months", only the selected account's when one is picked. */
  monthlyTotals = computed(() => {
    const selected = this.selectedBank();
    if (!selected) return this.allBankTotals();
    return aggregateByMonth(this.finance.monthlySummaries().filter((s) => s.bank === selected));
  });

  /**
   * The six calendar months up to the selected one, newest first, from the first month with
   * counted money on; a month without any shows as an empty row.
   */
  private sixKeys = computed(() => {
    const rows = this.monthlyTotals();
    const first = rows[rows.length - 1]?.month;
    if (!first) return [];
    return monthsUpTo(this.selectedMonth(), 6).filter((m) => m >= first);
  });

  sixMonths = computed(() => {
    const byMonth = new Map(this.monthlyTotals().map((r) => [r.month, r]));
    const rows = this.sixKeys().map(
      (month) => byMonth.get(month) ?? { month, income: 0, expenses: 0, net: 0 },
    );
    const max = Math.max(1, ...rows.flatMap((r) => [r.income, r.expenses]));
    return rows.map((r) => ({
      ...r,
      label: monthYearLabel(r.month),
      short: monthName(r.month, 'short'),
      keptShare: keptShare(r.income, r.net),
      inPct: (r.income / max) * 100,
      outPct: (r.expenses / max) * 100,
    }));
  });

  /** The counted rows of the six months, only the picked account's when one is picked. */
  private sixRows = computed(() => {
    const bank = this.selectedBank();
    const months = new Set(this.sixKeys());
    return this.finance
      .allTransactions()
      .filter((tx) => months.has(tx.month) && (!bank || tx.bank === bank));
  });

  /**
   * The six months together, and as an average month; null without months. Each category nets
   * over all six (ADR-037), as on Insights, so a payback a month after its expense cancels it
   * here, though each month's row counts it on its own.
   */
  sixSummary = computed(() => {
    const n = this.sixMonths().length;
    if (n === 0) return null;
    const { income, spending: expenses, net } = categoryNet(this.sixRows());
    return {
      income,
      expenses,
      net,
      keptShare: keptShare(income, net),
      average: { income: income / n, expenses: expenses / n, net: net / n },
    };
  });

  /**
   * "Top spending" and "Top income": the categories that cost and brought in the most over the
   * six months, netted over all of them (ADR-037); the picked account's rows only, if any.
   */
  sixTops = computed(() => windowTops(this.sixRows(), [...this.sixKeys()].reverse()));

  // Net worth = cash across banks + current investment portfolio value
  cashTotal = computed(() => this.finance.totalBalance());
  investedTotal = computed(() => this.investments.totalCurrentValue());
  netWorth = computed(() => this.cashTotal() + this.investedTotal());
  cashShare = computed(() => {
    const total = this.netWorth();
    return total > 0 ? Math.max(0, Math.min(100, (this.cashTotal() / total) * 100)) : 100;
  });

  /**
   * Net worth's change since the end of the month before the selected one; null if unknown.
   * `growth` is what investment prices added (money put in left out), `rest` the change less it:
   * what the accounts kept. Both null without investments.
   */
  netWorthChange = computed(() => {
    const cutoff = lastDayOf(previousMonth(this.selectedMonth()));
    const latest = new Map<string, { periodTo: string; closing: number }>();
    for (const s of this.finance.statements()) {
      if (s.periodTo > cutoff) continue;
      const cur = latest.get(s.bank);
      if (!cur || s.periodTo > cur.periodTo)
        latest.set(s.bank, { periodTo: s.periodTo, closing: s.closingBalance });
    }
    if (latest.size === 0) return null;
    const cash = [...latest.values()].reduce((sum, b) => sum + b.closing, 0);

    let invested = 0;
    let growth: number | null = null;
    if (this.investments.assets().length > 0) {
      const history = this.investments.portfolioHistory();
      const point = [...history].reverse().find((p) => p.date <= cutoff);
      const heldBefore = this.investments
        .assets()
        .some((a) => a.lots.some((l) => l.date <= cutoff));
      if (point) invested = point.totalValue;
      else if (heldBefore) return null;
      const last = history[history.length - 1];
      if (last) {
        const start = point ?? { date: cutoff, totalValue: 0, invested: 0 };
        growth = valueChange(start, last).growth;
      }
    }
    const amount = this.netWorth() - cash - invested;
    return { amount, since: cutoff, growth, rest: growth === null ? null : amount - growth };
  });

  monthSnapshot = computed(() => {
    const totals = this.allBankTotals();
    const key = this.selectedMonth();
    const cur = totals.find((r) => r.month === key) ?? {
      month: key,
      income: 0,
      expenses: 0,
      net: 0,
    };
    const prevKey = previousMonth(key);
    const prev = totals.find((r) => r.month === prevKey);
    return {
      month: key,
      prevMonth: prevKey,
      income: cur.income,
      expenses: cur.expenses,
      net: cur.net,
      prev: prev ?? null,
      incomeDelta:
        prev && prev.income !== 0 ? ((cur.income - prev.income) / prev.income) * 100 : null,
      expensesDelta:
        prev && prev.expenses !== 0 ? ((cur.expenses - prev.expenses) / prev.expenses) * 100 : null,
      netDelta: prev ? cur.net - prev.net : null,
      /** The month's savings rate: Kept as a share of In. */
      keptShare: keptShare(cur.income, cur.net),
    };
  });

  /**
   * "Where it went": the selected month's categories that net to spending, by that net, largest
   * first (ADR-037); one paid back in full is left out.
   */
  topCategories = computed(() => {
    const key = this.selectedMonth();
    const rows = this.finance.allTransactions().filter((tx) => tx.month === key);
    const sorted = spendingByCategory(categoryNet(rows));
    const max = sorted[0]?.total || 1;
    const sum = sorted.reduce((s, c) => s + c.total, 0) || 1;
    return sorted.map((c) => ({
      ...c,
      pct: (c.total / max) * 100,
      share: Math.round((c.total / sum) * 100),
    }));
  });

  river = computed(() =>
    riverSeries(this.finance.allTransactions(), this.selectedMonth(), this.todayIso),
  );

  /** How the selected month compares with the previous one by month end. */
  riverNote = computed(() => {
    const s = this.river();
    if (s.lastTotal === 0) return null;
    const diff = s.total - s.lastTotal;
    const pct = Math.abs((diff / s.lastTotal) * 100).toFixed(1);
    const prev = monthName(s.previous);
    if (Math.abs(diff) < 0.005) return `Level with ${prev}.`;
    return diff < 0
      ? `${eur(diff)} less than ${prev}, a ${pct}% drop.`
      : `${eur(diff)} more than ${prev}, ${pct}% up.`;
  });

  /** Counted rows of the selected month that have no category yet. */
  uncategorised = computed(() => {
    const key = this.selectedMonth();
    const rows = this.finance
      .allTransactions()
      .filter((tx) => tx.month === key && tx.categoryId === null && tx.type !== 'unknown');
    return { count: rows.length, amount: rows.reduce((sum, tx) => sum + tx.amount, 0) };
  });

  latestPriceDate = computed(() =>
    this.investments
      .assetMetrics()
      .map((m) => m.latestPriceDate)
      .filter((d): d is string => d != null)
      .sort()
      .pop(),
  );

  /** Portfolio value over the last 12 months, about one point a week. */
  investSpark = computed(() => {
    const since = new Date(
      this.today.getFullYear() - 1,
      this.today.getMonth(),
      this.today.getDate(),
    );
    const from = `${monthKeyOf(since)}-${String(since.getDate()).padStart(2, '0')}`;
    const points = this.investments.portfolioHistory().filter((p) => p.date >= from);
    const step = Math.max(1, Math.floor(points.length / 52));
    const values = points.filter((_, i) => i % step === 0 || i === points.length - 1);
    const path = sparkline(
      values.map((p) => p.totalValue),
      112,
      48,
    );
    return (
      path && { ...path, first: values[0].totalValue, last: values[values.length - 1].totalValue }
    );
  });

  /** '+1.6%', '−2.3%' (a true minus sign), or '—' when unknown. */
  formatSignedPct(val: number | null): string {
    if (val === null) return '—';
    return (val >= 0 ? '+' : '−') + Math.abs(val).toFixed(1) + '%';
  }

  /** Category ids by name, from the rows, for links into Insights. */
  private categoryIds = computed(() => {
    const ids = new Map<string, number>();
    for (const tx of this.finance.allTransactions())
      if (tx.category) ids.set(tx.category.name, tx.category.id);
    return ids;
  });

  /** Where the six months' links go: nowhere with an account picked, as Insights has no bank filter. */
  sixLink = computed(() => (this.selectedBank() ? null : '/analytics'));

  /**
   * Insights on the selected month, or on the `months` up to it, on one side, and on one category
   * when given.
   */
  insightsQuery(label?: string, side: 'in' | 'out' = 'out', months = 1): Record<string, string> {
    const query: Record<string, string> = { month: this.selectedMonth(), side };
    if (months > 1) query['months'] = String(months);
    if (label === CATEGORY_UNKNOWN) query['category'] = 'unknown';
    else if (label) {
      const id = this.categoryIds().get(label);
      if (id !== undefined) query['category'] = String(id);
    }
    return query;
  }

  monthQuery(month: string): Record<string, string> {
    const queryParams: Record<string, string> = { month };
    const bank = this.selectedBank();
    if (bank) queryParams['bank'] = bank;
    return queryParams;
  }

  navigateToMonth(month: string): void {
    this.router.navigate(['/transactions'], { queryParams: this.monthQuery(month) });
  }

  toggleBank(bank: string): void {
    this.selectedBank.update((cur) => (cur === bank ? null : bank));
  }

  formatMonth(month: string): string {
    return monthYearLabel(month);
  }

  onDragStart(bank: string): void {
    this.draggedBank.set(bank);
  }

  onDragOver(event: DragEvent, bank: string): void {
    event.preventDefault();
    if (this.draggedBank() !== bank) {
      this.dragOverBank.set(bank);
    }
  }

  onDrop(targetBank: string): void {
    const dragged = this.draggedBank();
    if (!dragged || dragged === targetBank) {
      this.dragOverBank.set(null);
      return;
    }
    const order = [...this.cardOrder()];
    const fromIdx = order.indexOf(dragged);
    const toIdx = order.indexOf(targetBank);
    if (fromIdx !== -1 && toIdx !== -1) {
      order.splice(fromIdx, 1);
      order.splice(toIdx, 0, dragged);
      this.cardOrder.set(order);
    }
    this.draggedBank.set(null);
    this.dragOverBank.set(null);
  }

  onDragEnd(): void {
    this.draggedBank.set(null);
    this.dragOverBank.set(null);
  }
}
