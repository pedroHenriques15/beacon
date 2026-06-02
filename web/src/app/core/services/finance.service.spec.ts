import { describe, it, expect, beforeEach } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { HttpClient, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { FinanceService } from './finance.service';
import { Statement, StatementSummary } from '../models/statement.model';

function makeSummary(overrides: Partial<StatementSummary> = {}): StatementSummary {
  return {
    id: 1,
    bank: 'TESTBANK',
    account: 'ACC1',
    periodFrom: '2024-01-01',
    periodTo: '2024-01-31',
    currency: 'EUR',
    openingBalance: 1000,
    closingBalance: 1500,
    sourceFile: 'stmt.pdf',
    hasFile: true,
    transactionCount: 2,
    ...overrides,
  };
}

function makeStatement(overrides: Partial<Statement> = {}): Statement {
  return {
    ...makeSummary(),
    pdfPath: null,
    importedAt: '2024-01-31T00:00:00Z',
    transactions: [],
    ...overrides,
  };
}

describe('FinanceService', () => {
  let service: FinanceService;
  let controller: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [FinanceService, provideHttpClient(), provideHttpClientTesting()],
    });

    controller = TestBed.inject(HttpTestingController);
    service = TestBed.inject(FinanceService);

    flushLoadAll(controller, []);
  });

  function flushLoadAll(ctrl: HttpTestingController, statements: Statement[]) {
    const summaryReq = ctrl.expectOne('/api/statements');
    const summaries: StatementSummary[] = statements.map((s) => ({
      id: s.id,
      bank: s.bank,
      account: s.account,
      periodFrom: s.periodFrom,
      periodTo: s.periodTo,
      currency: s.currency,
      openingBalance: s.openingBalance,
      closingBalance: s.closingBalance,
      sourceFile: s.sourceFile,
      hasFile: s.hasFile,
      transactionCount: s.transactions.length,
    }));
    summaryReq.flush(summaries);

    for (const stmt of statements) {
      ctrl.expectOne(`/api/statements/${stmt.id}`).flush(stmt);
    }
  }

  it('starts with loading=false after empty response', () => {
    expect(service.loading()).toBe(false);
  });

  it('starts with empty statements after empty response', () => {
    expect(service.statements()).toEqual([]);
  });

  it('starts with no error', () => {
    expect(service.error()).toBeNull();
  });

  it('reload() sets loading=true then fetches statements', () => {
    const stmt = makeStatement({ id: 10, bank: 'BPI' });

    service.reload();
    expect(service.loading()).toBe(true);

    flushLoadAll(controller, [stmt]);

    expect(service.loading()).toBe(false);
    expect(service.statements().length).toBe(1);
    expect(service.statements()[0].bank).toBe('BPI');
  });

  it('banks() returns sorted unique bank names', () => {
    service.reload();
    flushLoadAll(controller, [
      makeStatement({ id: 1, bank: 'REVOLUT' }),
      makeStatement({ id: 2, bank: 'BPI' }),
      makeStatement({ id: 3, bank: 'REVOLUT' }),
    ]);

    expect(service.banks()).toEqual(['BPI', 'REVOLUT']);
  });

  it('banks() returns empty array when no statements', () => {
    expect(service.banks()).toEqual([]);
  });

  it('latestPerBank() returns latest statement per bank by periodTo', () => {
    service.reload();
    flushLoadAll(controller, [
      makeStatement({ id: 1, bank: 'BPI', periodTo: '2024-01-31' }),
      makeStatement({ id: 2, bank: 'BPI', periodTo: '2024-02-29' }),
    ]);

    const latest = service.latestPerBank();
    expect(latest.get('BPI')!.id).toBe(2);
  });

  it('latestPerBank() handles multiple banks independently', () => {
    service.reload();
    flushLoadAll(controller, [
      makeStatement({ id: 1, bank: 'BPI', periodTo: '2024-01-31', closingBalance: 500 }),
      makeStatement({ id: 2, bank: 'REVOLUT', periodTo: '2024-01-31', closingBalance: 200 }),
    ]);

    const latest = service.latestPerBank();
    expect(latest.size).toBe(2);
    expect(latest.get('BPI')!.closingBalance).toBe(500);
    expect(latest.get('REVOLUT')!.closingBalance).toBe(200);
  });

  it('totalBalance() sums closingBalance of latest statement per bank', () => {
    service.reload();
    flushLoadAll(controller, [
      makeStatement({ id: 1, bank: 'BPI', periodTo: '2024-01-31', closingBalance: 1000 }),
      makeStatement({ id: 2, bank: 'REVOLUT', periodTo: '2024-01-31', closingBalance: 500 }),
    ]);

    expect(service.totalBalance()).toBe(1500);
  });

  it('totalBalance() uses only the latest statement when a bank has multiple', () => {
    service.reload();
    flushLoadAll(controller, [
      makeStatement({ id: 1, bank: 'BPI', periodTo: '2024-01-31', closingBalance: 1000 }),
      makeStatement({ id: 2, bank: 'BPI', periodTo: '2024-02-29', closingBalance: 1200 }),
    ]);

    expect(service.totalBalance()).toBe(1200);
  });

  it('allTransactions() excludes internal transfers', () => {
    service.reload();
    flushLoadAll(controller, [
      makeStatement({
        id: 1,
        bank: 'BPI',
        periodFrom: '2024-01-01',
        transactions: [
          {
            id: 1,
            statementId: 1,
            datePosting: '2024-01-05',
            dateValue: '2024-01-05',
            description: 'Normal TX',
            amount: 100,
            type: 'debit',
            balance: 900,
            categoryId: null,
            categoryRuleId: null,
            categorySetManually: false,
            isExcluded: false,
            category: null,
          },
          {
            id: 2,
            statementId: 1,
            datePosting: '2024-01-06',
            dateValue: '2024-01-06',
            description: 'Transfer TX',
            amount: 200,
            type: 'debit',
            balance: 700,
            categoryId: null,
            categoryRuleId: null,
            categorySetManually: false,
            isExcluded: true,
            category: null,
          },
        ],
      }),
    ]);

    const txs = service.allTransactions();
    expect(txs.length).toBe(1);
    expect(txs[0].description).toBe('Normal TX');
  });

  it('allTransactions() enriches with bank and month fields', () => {
    service.reload();
    flushLoadAll(controller, [
      makeStatement({
        id: 1,
        bank: 'REVOLUT',
        periodFrom: '2024-03-01',
        transactions: [
          {
            id: 10,
            statementId: 1,
            datePosting: '2024-03-10',
            dateValue: '2024-03-10',
            description: 'Coffee',
            amount: 5,
            type: 'debit',
            balance: 995,
            categoryId: null,
            categoryRuleId: null,
            categorySetManually: false,
            isExcluded: false,
            category: null,
          },
        ],
      }),
    ]);

    const tx = service.allTransactions()[0];
    expect(tx.bank).toBe('REVOLUT');
    expect(tx.month).toBe('2024-03');
  });

  it('allTransactions() is sorted descending by datePosting', () => {
    service.reload();
    flushLoadAll(controller, [
      makeStatement({
        id: 1,
        bank: 'BPI',
        periodFrom: '2024-01-01',
        transactions: [
          {
            id: 1,
            statementId: 1,
            datePosting: '2024-01-01',
            dateValue: '2024-01-01',
            description: 'Old',
            amount: 10,
            type: 'debit',
            balance: 990,
            categoryId: null,
            categoryRuleId: null,
            categorySetManually: false,
            isExcluded: false,
            category: null,
          },
          {
            id: 2,
            statementId: 1,
            datePosting: '2024-01-15',
            dateValue: '2024-01-15',
            description: 'New',
            amount: 10,
            type: 'debit',
            balance: 980,
            categoryId: null,
            categoryRuleId: null,
            categorySetManually: false,
            isExcluded: false,
            category: null,
          },
        ],
      }),
    ]);

    const txs = service.allTransactions();
    expect(txs[0].description).toBe('New');
    expect(txs[1].description).toBe('Old');
  });

  it('allTransactionsRaw() includes internal transfers', () => {
    service.reload();
    flushLoadAll(controller, [
      makeStatement({
        id: 1,
        bank: 'BPI',
        periodFrom: '2024-01-01',
        transactions: [
          {
            id: 1,
            statementId: 1,
            datePosting: '2024-01-05',
            dateValue: '2024-01-05',
            description: 'Transfer',
            amount: 200,
            type: 'debit',
            balance: 800,
            categoryId: null,
            categoryRuleId: null,
            categorySetManually: false,
            isExcluded: true,
            category: null,
          },
        ],
      }),
    ]);

    expect(service.allTransactionsRaw().length).toBe(1);
    expect(service.allTransactionsRaw()[0].isExcluded).toBe(true);
  });

  it('monthlySummaries() calculates income/expenses for non-BPI bank', () => {
    service.reload();
    flushLoadAll(controller, [
      makeStatement({
        id: 1,
        bank: 'REVOLUT',
        periodFrom: '2024-01-01',
        closingBalance: 1200,
        transactions: [
          {
            id: 1,
            statementId: 1,
            datePosting: '2024-01-10',
            dateValue: '2024-01-10',
            description: 'Salary',
            amount: 1000,
            type: 'credit',
            balance: 2000,
            categoryId: null,
            categoryRuleId: null,
            categorySetManually: false,
            isExcluded: false,
            category: null,
          },
          {
            id: 2,
            statementId: 1,
            datePosting: '2024-01-15',
            dateValue: '2024-01-15',
            description: 'Rent',
            amount: 800,
            type: 'debit',
            balance: 1200,
            categoryId: null,
            categoryRuleId: null,
            categorySetManually: false,
            isExcluded: false,
            category: null,
          },
        ],
      }),
    ]);

    const summaries = service.monthlySummaries();
    expect(summaries.length).toBe(1);
    const s = summaries[0];
    expect(s.bank).toBe('REVOLUT');
    expect(s.month).toBe('2024-01');
    expect(s.income).toBe(1000);
    expect(s.expenses).toBe(800);
    expect(s.net).toBe(200);
    expect(s.closingBalance).toBe(1200);
  });

  it('monthlySummaries() excludes internal transfers from income/expenses', () => {
    service.reload();
    flushLoadAll(controller, [
      makeStatement({
        id: 1,
        bank: 'REVOLUT',
        periodFrom: '2024-01-01',
        closingBalance: 1000,
        transactions: [
          {
            id: 1,
            statementId: 1,
            datePosting: '2024-01-05',
            dateValue: '2024-01-05',
            description: 'Transfer OUT',
            amount: 500,
            type: 'debit',
            balance: 1000,
            categoryId: null,
            categoryRuleId: null,
            categorySetManually: false,
            isExcluded: true,
            category: null,
          },
        ],
      }),
    ]);

    const s = service.monthlySummaries()[0];
    expect(s.expenses).toBe(0);
    expect(s.income).toBe(0);
  });

  it('monthlySummaries() is sorted by month then bank', () => {
    service.reload();
    flushLoadAll(controller, [
      makeStatement({
        id: 1,
        bank: 'REVOLUT',
        periodFrom: '2024-02-01',
        closingBalance: 100,
        transactions: [],
      }),
      makeStatement({
        id: 2,
        bank: 'BPI',
        periodFrom: '2024-01-01',
        openingBalance: 0,
        closingBalance: 200,
        transactions: [],
      }),
    ]);

    const summaries = service.monthlySummaries();
    expect(summaries[0].month).toBe('2024-01');
    expect(summaries[1].month).toBe('2024-02');
  });

  it('sets error signal when HTTP request fails', () => {
    service.reload();

    controller.expectOne('/api/statements').error(new ProgressEvent('error'));

    expect(service.error()).toContain('Failed to load');
    expect(service.loading()).toBe(false);
  });

  it('upload() POSTs to /api/statements/upload', () => {
    const file = new File(['pdf'], 'test.pdf', { type: 'application/pdf' });
    service.upload(file).subscribe();

    const req = controller.expectOne('/api/statements/upload');
    expect(req.request.method).toBe('POST');
    expect(req.request.body instanceof FormData).toBe(true);
    req.flush({
      imported: true,
      bank: 'BPI',
      periodFrom: '2024-01-01',
      transactionCount: 5,
      unknownCount: 0,
      message: null,
    });
  });

  it('markTransfers() PATCHes /api/transactions/mark-transfers', () => {
    service.markTransfers([1, 2], false).subscribe();

    const req = controller.expectOne('/api/transactions/mark-transfers');
    expect(req.request.method).toBe('PATCH');
    expect(req.request.body).toEqual({ txIds: [1, 2], unmark: false });
    req.flush(null);
  });

  it('createTransaction() POSTs to /api/transactions', () => {
    const body = {
      statementId: 1,
      datePosting: '2024-01-10',
      dateValue: '2024-01-10',
      description: 'Manual',
      amount: 50,
      type: 'debit',
      balance: 950,
    };
    service.createTransaction(body).subscribe();

    const req = controller.expectOne('/api/transactions');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(body);
    req.flush({
      ...body,
      id: 99,
      statementId: 1,
      categoryId: null,
      categoryRuleId: null,
      categorySetManually: false,
      isExcluded: false,
      category: null,
    });
  });

  it('updateTransaction() PUTs to /api/transactions/:id', () => {
    service.updateTransaction(42, { description: 'Updated' }).subscribe();

    const req = controller.expectOne('/api/transactions/42');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ description: 'Updated' });
    req.flush({});
  });
});
