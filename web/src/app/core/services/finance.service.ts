import { Injectable, inject, signal, computed } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { buildParams } from '../utils/http-params';
import { Observable, forkJoin, of, switchMap } from 'rxjs';
import {
  BatchUploadItemResult,
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
    this.loading.set(true);
    this.error.set(null);
    this.loadAll();
  }

  upload(file: File): Observable<UploadResult> {
    const form = new FormData();
    form.append('file', file);
    return this.http.post<UploadResult>('/api/statements/upload', form);
  }

  uploadBatch(files: File[]): Observable<BatchUploadItemResult[]> {
    const form = new FormData();
    for (const f of files) form.append('files', f);
    return this.http.post<BatchUploadItemResult[]>('/api/statements/upload-batch', form);
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

  allTransactions = computed<EnrichedTransaction[]>(() =>
    this.statements()
      .flatMap((s) =>
        s.transactions
          .filter((tx) => !tx.isExcluded)
          .map((tx) => ({ ...tx, bank: s.bank, month: s.periodFrom.slice(0, 7) })),
      )
      .sort((a, b) => b.datePosting.localeCompare(a.datePosting)),
  );

  allTransactionsRaw = computed<EnrichedTransaction[]>(() =>
    this.statements()
      .flatMap((s) =>
        s.transactions.map((tx) => ({ ...tx, bank: s.bank, month: s.periodFrom.slice(0, 7) })),
      )
      .sort((a, b) => b.datePosting.localeCompare(a.datePosting)),
  );

  monthlySummaries = computed<MonthlySummary[]>(() => {
    const bpiByMonth = this.statements()
      .filter((s) => s.bank === 'BPI')
      .sort((a, b) => a.periodFrom.localeCompare(b.periodFrom));

    const result: MonthlySummary[] = [];
    for (const s of this.statements()) {
      const month = s.periodFrom.slice(0, 7);
      const credits = s.transactions
        .filter((tx) => tx.type === 'credit' && !tx.isExcluded)
        .reduce((sum, tx) => sum + tx.amount, 0);
      const debits = s.transactions
        .filter((tx) => tx.type === 'debit' && !tx.isExcluded)
        .reduce((sum, tx) => sum + tx.amount, 0);

      let income: number, expenses: number;

      if (s.bank === 'BPI') {
        const idx = bpiByMonth.findIndex((b) => b.id === s.id);
        const prevClosing =
          s.openingBalance !== 0
            ? s.openingBalance
            : idx > 0
              ? bpiByMonth[idx - 1].closingBalance
              : 0;
        const pprChange = s.closingBalance - prevClosing;
        income = Math.max(0, pprChange) + credits;
        expenses = Math.max(0, -pprChange) + debits;
      } else {
        income = credits;
        expenses = debits;
      }

      result.push({
        month,
        bank: s.bank,
        income,
        expenses,
        net: income - expenses,
        closingBalance: s.closingBalance,
      });
    }
    return result.sort((a, b) => a.month.localeCompare(b.month) || a.bank.localeCompare(b.bank));
  });
}
