import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FinanceService } from '../../core/services/finance.service';
import { ConfirmDialogComponent } from '../../core/components/confirm-dialog/confirm-dialog';
import { bankInitials } from '../../core/utils/bank';
import { eur } from '../../core/utils/money';

/** Every uploaded statement, newest first: open its PDF or delete it. */
@Component({
  selector: 'app-statement-list',
  standalone: true,
  imports: [ConfirmDialogComponent],
  templateUrl: './statement-list.html',
  styleUrl: './statement-list.scss',
  changeDetection: ChangeDetectionStrategy.Eager,
})
export class StatementListComponent {
  finance = inject(FinanceService);

  readonly eur = eur;
  readonly bankInitials = bankInitials;

  /** Statements newest first, by bank within a date. */
  statements = computed(() =>
    [...this.finance.statements()].sort(
      (a, b) => b.periodTo.localeCompare(a.periodTo) || a.bank.localeCompare(b.bank),
    ),
  );

  showAll = signal(false);

  shown = computed(() => (this.showAll() ? this.statements() : this.statements().slice(0, 6)));

  deleting = signal<number | null>(null);
  confirmDeleteId = signal<number | null>(null);

  private static readonly periodPipe = new DatePipe('en-GB');

  formatPeriod(from: string, to: string): string {
    const f = StatementListComponent.periodPipe;
    return `${f.transform(from, 'd MMM')} – ${f.transform(to, 'd MMM yyyy')}`;
  }

  deleteStatement(id: number): void {
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
      error: () => this.deleting.set(null),
    });
  }

  openStatementFile(id: number, pdfPath: string | null): void {
    if (!pdfPath) return;
    this.finance.getStatementFile(id).subscribe((blob) => {
      const url = URL.createObjectURL(blob);
      window.open(url, '_blank');
    });
  }
}
