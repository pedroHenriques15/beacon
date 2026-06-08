import { Component, inject, signal, computed, effect } from '@angular/core';
import { CurrencyPipe, DatePipe, NgClass } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Router } from '@angular/router';
import { FinanceService } from '../../core/services/finance.service';
import { ConfirmDialogComponent } from '../../core/components/confirm-dialog/confirm-dialog';

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [CurrencyPipe, DatePipe, NgClass, ConfirmDialogComponent],
  templateUrl: './dashboard.html',
  styleUrl: './dashboard.scss',
})
export class DashboardComponent {
  finance = inject(FinanceService);
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
  });

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
      BPI: '#f59e0b',
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

  formatPeriod(from: string, to: string): string {
    const f = new DatePipe('en-US');
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
