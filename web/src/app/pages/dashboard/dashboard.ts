import { Component, inject, signal, computed, effect } from '@angular/core';
import { CurrencyPipe, DatePipe, NgClass } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Router, RouterLink } from '@angular/router';
import { FinanceService } from '../../core/services/finance.service';
import { InvestmentsService } from '../../core/services/investments.service';
import { ConfirmDialogComponent } from '../../core/components/confirm-dialog/confirm-dialog';
import { CATEGORY_UNKNOWN } from '../../core/constants/categories';

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [CurrencyPipe, DatePipe, NgClass, ConfirmDialogComponent, RouterLink],
  templateUrl: './dashboard.html',
  styleUrl: './dashboard.scss',
})
export class DashboardComponent {
  finance = inject(FinanceService);
  investments = inject(InvestmentsService);
  private router = inject(Router);

  selectedBank = signal<string | null>(null);
  deleting = signal<number | null>(null);
  confirmDeleteId = signal<number | null>(null);

  private cardOrder = signal<string[]>([]);
  draggedBank = signal<string | null>(null);
  dragOverBank = signal<string | null>(null);

  bankCards = computed(() => {
    const allStatements = this.finance.statements();
    return [...this.finance.latestPerBank().entries()].map(([bank, s]) => ({
      bank,
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
  }

  allStatements = computed(() =>
    [...this.finance.statements()].sort(
      (a, b) => a.bank.localeCompare(b.bank) || b.periodFrom.localeCompare(a.periodFrom),
    ),
  );

  monthlyTotals = computed(() => {
    const selected = this.selectedBank();
    const summaries = selected
      ? this.finance.monthlySummaries().filter((s) => s.bank === selected)
      : this.finance.monthlySummaries();
    return DashboardComponent.aggregateByMonth(summaries);
  });

  private static aggregateByMonth(
    summaries: { month: string; income: number; expenses: number; net: number }[],
  ): { month: string; income: number; expenses: number; net: number }[] {
    const map = new Map<string, { income: number; expenses: number; net: number }>();
    for (const s of summaries) {
      const existing = map.get(s.month) ?? { income: 0, expenses: 0, net: 0 };
      map.set(s.month, {
        income: existing.income + s.income,
        expenses: existing.expenses + s.expenses,
        net: existing.net + s.net,
      });
    }
    return [...map.entries()]
      .sort(([a], [b]) => b.localeCompare(a))
      .map(([month, data]) => ({ month, ...data }));
  }

  // Net worth = cash across banks + current investment portfolio value
  cashTotal = computed(() => this.finance.totalBalance());
  investedTotal = computed(() => this.investments.totalCurrentValue());
  netWorth = computed(() => this.cashTotal() + this.investedTotal());

  // Snapshot always aggregates across all banks, unaffected by the bank-card filter
  private allBankTotals = computed(() =>
    DashboardComponent.aggregateByMonth(this.finance.monthlySummaries()),
  );

  monthSnapshot = computed(() => {
    const totals = this.allBankTotals();
    const nowKey = new Date().toISOString().slice(0, 7);
    // Latest closed month: most recent month strictly before the current calendar month;
    // fall back to the latest available month (totals is sorted descending)
    const key = totals.find((r) => r.month < nowKey)?.month ?? totals[0]?.month ?? nowKey;
    const cur = totals.find((r) => r.month === key) ?? {
      month: key,
      income: 0,
      expenses: 0,
      net: 0,
    };
    const [y, m] = key.split('-').map(Number);
    const prevKey = m === 1 ? `${y - 1}-12` : `${y}-${String(m - 1).padStart(2, '0')}`;
    const prev = totals.find((r) => r.month === prevKey);
    return {
      month: key,
      income: cur.income,
      expenses: cur.expenses,
      net: cur.net,
      incomeDelta:
        prev && prev.income !== 0 ? ((cur.income - prev.income) / prev.income) * 100 : null,
      expensesDelta:
        prev && prev.expenses !== 0 ? ((cur.expenses - prev.expenses) / prev.expenses) * 100 : null,
      netDelta: prev ? cur.net - prev.net : null,
    };
  });

  topCategories = computed(() => {
    const key = this.monthSnapshot().month;
    const map = new Map<string, { label: string; color: string; total: number }>();
    for (const tx of this.finance.allTransactions()) {
      if (tx.type !== 'debit' || tx.month !== key) continue;
      const label = tx.category?.name ?? CATEGORY_UNKNOWN;
      const color = tx.category?.color ?? '#475569';
      const cur = map.get(label) ?? { label, color, total: 0 };
      map.set(label, { ...cur, total: cur.total + tx.amount });
    }
    const sorted = [...map.values()].sort((a, b) => b.total - a.total).slice(0, 5);
    const max = sorted[0]?.total || 1;
    return sorted.map((c) => ({ ...c, pct: (c.total / max) * 100 }));
  });

  formatSignedPct(val: number | null): string {
    if (val === null) return '—';
    return (val >= 0 ? '+' : '') + val.toFixed(1) + '%';
  }

  navigateToInvestments(): void {
    this.router.navigate(['/investments']);
  }

  navigateToMonth(month: string): void {
    const queryParams: Record<string, string> = { month };
    const bank = this.selectedBank();
    if (bank) queryParams['bank'] = bank;
    this.router.navigate(['/transactions'], { queryParams });
  }

  toggleBank(bank: string): void {
    this.selectedBank.update((cur) => (cur === bank ? null : bank));
  }

  formatMonth(month: string): string {
    const [year, m] = month.split('-');
    return new Date(+year, +m - 1, 1).toLocaleString('default', { month: 'long', year: 'numeric' });
  }

  bankColor(bank: string): string {
    const colors: Record<string, string> = {
      ACTIVOBANK: '#00b4d8',
      REVOLUT: '#6c63ff',
      BPI: '#fb923c',
      'TRADE REPUBLIC': '#14b8a6',
    };
    return colors[bank] ?? '#94a3b8';
  }

  deleteStatement(id: number, event: MouseEvent): void {
    event.stopPropagation();
    this.confirmDeleteId.set(id);
  }

  onConfirmDelete(): void {
    const id = this.confirmDeleteId();
    if (id === null) return;
    this.confirmDeleteId.set(null);
    this.deleting.set(id);
    this.finance.deleteStatement(id).subscribe({
      next: () => {
        this.finance.reload();
        this.deleting.set(null);
      },
      error: (_err: HttpErrorResponse) => {
        this.deleting.set(null);
      },
    });
  }

  openStatementFile(id: number, pdfPath: string | null): void {
    if (!pdfPath) return;
    this.finance.getStatementFile(id).subscribe((blob) => {
      const url = URL.createObjectURL(blob);
      window.open(url, '_blank');
    });
  }

  private static readonly periodPipe = new DatePipe('en-US');

  formatPeriod(from: string, to: string): string {
    const f = DashboardComponent.periodPipe;
    return `${f.transform(from, 'dd MMM')} – ${f.transform(to, 'dd MMM yyyy')}`;
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
