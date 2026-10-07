import { Injectable, inject, signal, computed } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { buildParams } from '../utils/http-params';
import { Observable, forkJoin, of, switchMap } from 'rxjs';
import { CATEGORY_EXCLUDED } from '../constants/categories';
import { categoryNet } from '../utils/category-net';
import { MonthTotals } from '../utils/month-totals';
import {
  MonthlySummary,
  PagedTransactionsResult,
  Statement,
  StatementSummary,
  Transaction,
  UnifiedUploadItemResult,
  UploadResult,
} from '../models/statement.model';

export interface EnrichedTransaction extends Transaction {
  bank: string;
  month: string;
}

/**
 * A transaction is out of income/spending aggregates when it carries the isExcluded flag or sits in
 * the Excluded category. The backend keeps the two in sync; the category check is a safety net so a
 * transaction labelled Excluded can never be counted, whatever wrote it.
 */
function isExcluded(tx: Transaction): boolean {
  return tx.isExcluded || tx.category?.name === CATEGORY_EXCLUDED;
}

@Injectable({ providedIn: 'root' })
export class FinanceService {
  private http = inject(HttpClient);

  statements = signal<Statement[]>([]);
  loading = signal(true);
  error = signal<string | null>(null);

  constructor() {
    this.loadAll();
  }

  reload(): void {
    if (this.statements().length === 0) this.loading.set(true);
    this.error.set(null);
    this.loadAll();
  }

  upload(file: File): Observable<UploadResult> {
    const form = new FormData();
    form.append('file', file);
    return this.http.post<UploadResult>('/api/statements/upload', form);
  }

  getStatementFile(id: number): Observable<Blob> {
    return this.http.get(`/api/statements/${id}/file`, { responseType: 'blob' });
  }

  uploadUnified(files: File[]): Observable<UnifiedUploadItemResult[]> {
    const form = new FormData();
    for (const f of files) form.append('files', f);
    return this.http.post<UnifiedUploadItemResult[]>('/api/upload/batch', form);
  }

  importMealCardText(
    rawText: string,
    overrides?: { periodFrom?: string; periodTo?: string; closingBalance?: number },
  ): Observable<UploadResult> {
    return this.http.post<UploadResult>('/api/statements/import-text', {
      rawText,
      periodFrom: overrides?.periodFrom || undefined,
      periodTo: overrides?.periodTo || undefined,
      closingBalance: overrides?.closingBalance ?? undefined,
    });
  }

  getTransactions(params: {
    bank?: string;
    month?: string;
    type?: string;
    category?: string;
    search?: string;
    skip?: number;
    take?: number;
    sortBy?: string;
    sortDir?: string;
  }): Observable<PagedTransactionsResult<EnrichedTransaction>> {
    const p = buildParams({
      bank: params.bank,
      month: params.month,
      type: params.type,
      category: params.category,
      search: params.search,
      skip: params.skip,
      take: params.take,
      sortBy: params.sortBy,
      sortDir: params.sortDir,
    });
    return this.http.get<PagedTransactionsResult<EnrichedTransaction>>('/api/transactions', {
      params: p,
    });
  }

  markTransfers(txIds: number[], unmark = false): Observable<unknown> {
    return this.http.patch('/api/transactions/mark-transfers', { txIds, unmark });
  }

  removeTransactionLocally(txId: number): void {
    this.statements.update((stmts) =>
      stmts.map((s) => ({ ...s, transactions: s.transactions.filter((t) => t.id !== txId) })),
    );
  }

  updateTransactionLocally(txId: number, updates: Partial<Transaction>): void {
    this.statements.update((stmts) =>
      stmts.map((s) => ({
        ...s,
        transactions: s.transactions.map((t) => (t.id === txId ? { ...t, ...updates } : t)),
      })),
    );
  }

  deleteTransaction(id: number): Observable<unknown> {
    return this.http.delete(`/api/transactions/${id}`);
  }

  deleteTransactions(ids: number[]): Observable<unknown> {
    return this.http.delete('/api/transactions', { body: { ids } });
  }

  deleteStatement(id: number): Observable<unknown> {
    return this.http.delete(`/api/statements/${id}`);
  }

  createTransaction(body: {
    statementId: number;
    datePosting: string;
    dateValue: string;
    description: string;
    amount: number;
    type: string;
    balance: number;
    categoryId?: number | null;
  }): Observable<Transaction> {
    return this.http.post<Transaction>('/api/transactions', body);
  }

  updateTransaction(
    id: number,
    body: {
      datePosting?: string | null;
      dateValue?: string | null;
      description?: string | null;
      amount?: number | null;
      type?: string | null;
      balance?: number | null;
      categoryId?: number | null;
      categorySetManually?: boolean | null;
      unlinkTransfer?: boolean;
      unlinkCategory?: boolean;
    },
  ): Observable<Transaction> {
    return this.http.put<Transaction>(`/api/transactions/${id}`, body);
  }

  private loadAll(): void {
    this.http
      .get<StatementSummary[]>('/api/statements')
      .pipe(
        switchMap((summaries) => {
          if (summaries.length === 0) return of([]);
          return forkJoin(
            summaries.map((s) => this.http.get<Statement>(`/api/statements/${s.id}`)),
          );
        }),
      )
      .subscribe({
        next: (statements) => {
          this.statements.set(statements);
          this.loading.set(false);
        },
        error: (err) => {
          this.error.set('Failed to load financial data. Is the API running?');
          this.loading.set(false);
          console.error(err);
        },
      });
  }

  banks = computed(() => [...new Set(this.statements().map((s) => s.bank))].sort());

  latestPerBank = computed(() => {
    const map = new Map<string, Statement>();
    for (const s of this.statements()) {
      const existing = map.get(s.bank);
      if (!existing || s.periodTo > existing.periodTo) map.set(s.bank, s);
    }
    return map;
  });

  totalBalance = computed(() =>
    [...this.latestPerBank().values()].reduce((sum, s) => sum + s.closingBalance, 0),
  );

  unknownTypeCount = computed(
    () => this.allTransactions().filter((tx) => tx.type === 'unknown').length,
  );

  allTransactions = computed<EnrichedTransaction[]>(() =>
    this.statements()
      .flatMap((s) =>
        s.transactions
          .filter((tx) => !isExcluded(tx))
          .map((tx) => ({ ...tx, bank: s.bank, month: tx.datePosting.slice(0, 7) })),
      )
      .sort((a, b) => b.datePosting.localeCompare(a.datePosting)),
  );

  allTransactionsRaw = computed<EnrichedTransaction[]>(() =>
    this.statements()
      .flatMap((s) =>
        s.transactions.map((tx) => ({ ...tx, bank: s.bank, month: tx.datePosting.slice(0, 7) })),
      )
      .sort((a, b) => b.datePosting.localeCompare(a.datePosting)),
  );

  /**
   * Per month and bank, oldest first: income and spending with each category netted within the
   * bank (ADR-037), for a view of one bank. Every bank together is `monthTotals`, never these
   * rows added up: a payback into another bank nets only there.
   */
  monthlySummaries = computed<MonthlySummary[]>(() => {
    const rows = new Map<string, { month: string; bank: string; txs: Transaction[] }>();
    const latestClosing = new Map<string, { periodTo: string; closing: number }>();

    for (const s of this.statements()) {
      for (const tx of s.transactions) {
        if (isExcluded(tx)) continue;
        if (tx.type !== 'credit' && tx.type !== 'debit') continue;

        const month = tx.datePosting.slice(0, 7);
        const key = `${month}|${s.bank}`;

        const row = rows.get(key) ?? { month, bank: s.bank, txs: [] };
        row.txs.push(tx);
        rows.set(key, row);

        const cur = latestClosing.get(key);
        if (!cur || s.periodTo.localeCompare(cur.periodTo) > 0)
          latestClosing.set(key, { periodTo: s.periodTo, closing: s.closingBalance });
      }
    }

    return [...rows.entries()]
      .map(([key, { month, bank, txs }]) => {
        const totals = categoryNet(txs);
        return {
          month,
          bank,
          income: totals.income,
          expenses: totals.spending,
          net: totals.net,
          closingBalance: latestClosing.get(key)?.closing ?? 0,
        };
      })
      .sort((a, b) => a.month.localeCompare(b.month) || a.bank.localeCompare(b.bank));
  });

  /** Every bank together, newest first, each category netted across the banks (ADR-037). */
  monthTotals = computed<MonthTotals[]>(() => {
    const byMonth = new Map<string, EnrichedTransaction[]>();
    for (const tx of this.allTransactions()) {
      if (tx.type !== 'credit' && tx.type !== 'debit') continue;
      const txs = byMonth.get(tx.month) ?? [];
      txs.push(tx);
      byMonth.set(tx.month, txs);
    }
    return [...byMonth.entries()]
      .sort(([a], [b]) => b.localeCompare(a))
      .map(([month, txs]) => {
        const totals = categoryNet(txs);
        return { month, income: totals.income, expenses: totals.spending, net: totals.net };
      });
  });
}
