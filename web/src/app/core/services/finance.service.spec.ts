import { describe, it, expect, beforeEach } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { HttpClient, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { FinanceService } from './finance.service';
import { Statement, StatementSummary, Transaction } from '../models/statement.model';

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

  it('allTransactions() excludes flagged transactions', () => {
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

  it('allTransactionsRaw() includes excluded transactions', () => {
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

  it('unknownTypeCount() counts non-excluded unknown-type transactions', () => {
    service.reload();
    flushLoadAll(controller, [
      makeStatement({
        id: 1,
        bank: 'REVOLUT',
        periodFrom: '2024-01-01',
        transactions: [
          {
            id: 1,
            statementId: 1,
            datePosting: '2024-01-05',
            dateValue: '2024-01-05',
            description: 'Unverified',
            amount: 50,
            type: 'unknown',
            balance: 950,
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
            description: 'Excluded unknown',
            amount: 20,
            type: 'unknown',
            balance: 930,
            categoryId: null,
            categoryRuleId: null,
            categorySetManually: false,
            isExcluded: true,
            category: null,
          },
        ],
      }),
    ]);

    expect(service.unknownTypeCount()).toBe(1);
  });

  it('monthlySummaries() does not double-count BPI net movement (audit Top-10 #4)', () => {
    service.reload();
    flushLoadAll(controller, [
      makeStatement({
        id: 1,
        bank: 'BPI',
        periodFrom: '2024-01-01',
        openingBalance: 9500,
        closingBalance: 10000,
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
            amount: 500,
            type: 'debit',
            balance: 1500,
            categoryId: null,
            categoryRuleId: null,
            categorySetManually: false,
            isExcluded: false,
            category: null,
          },
          {
            id: 3,
            statementId: 1,
            datePosting: '2024-01-31',
            dateValue: '2024-01-31',
            description: 'BPI Reforma - Ganhos',
            amount: 300,
            type: 'credit',
            balance: 1800,
            categoryId: null,
            categoryRuleId: null,
            categorySetManually: false,
            isExcluded: false,
            category: null,
          },
        ],
      }),
    ]);

    const s = service.monthlySummaries()[0];
    // Income = credits only (salary + synthetic PPR gain, counted once each).
    expect(s.income).toBe(1300);
    expect(s.expenses).toBe(500);
    expect(s.net).toBe(800);
  });

  it('monthlySummaries() excludes flagged transactions from income/expenses', () => {
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

    // A statement whose only transaction is an excluded transfer contributes no
    // summary rows at all - excluded movement never reaches income or expenses.
    expect(service.monthlySummaries().length).toBe(0);
  });

  it('monthlySummaries() buckets transactions into their own months (mid-month statement)', () => {
    service.reload();
    flushLoadAll(controller, [
      makeStatement({
        id: 1,
        bank: 'REVOLUT',
        periodFrom: '2024-06-15',
        periodTo: '2024-07-14',
        closingBalance: 700,
        transactions: [
          {
            id: 1,
            statementId: 1,
            datePosting: '2024-06-20',
            dateValue: '2024-06-20',
            description: 'June spend',
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
            datePosting: '2024-07-05',
            dateValue: '2024-07-05',
            description: 'July spend',
            amount: 200,
            type: 'debit',
            balance: 700,
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
    expect(summaries.length).toBe(2);
    expect(summaries[0].month).toBe('2024-06');
    expect(summaries[0].expenses).toBe(100);
    expect(summaries[1].month).toBe('2024-07');
    expect(summaries[1].expenses).toBe(200);
  });

  it('monthlySummaries() is sorted by month then bank', () => {
    service.reload();
    const makeTx = (id: number, statementId: number, datePosting: string) => ({
      id,
      statementId,
      datePosting,
      dateValue: datePosting,
      description: 'TX',
      amount: 10,
      type: 'debit' as const,
      balance: 90,
      categoryId: null,
      categoryRuleId: null,
      categorySetManually: false,
      isExcluded: false,
      category: null,
    });
    flushLoadAll(controller, [
      makeStatement({
        id: 1,
        bank: 'REVOLUT',
        periodFrom: '2024-02-01',
        closingBalance: 100,
        transactions: [makeTx(1, 1, '2024-02-10')],
      }),
      makeStatement({
        id: 2,
        bank: 'BPI',
        periodFrom: '2024-01-01',
        openingBalance: 0,
        closingBalance: 200,
        transactions: [makeTx(2, 2, '2024-01-10')],
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

  it('allTransactions() drops transactions labelled Excluded even without the isExcluded flag', () => {
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
            description: 'Stale exclusion',
            amount: 250,
            type: 'debit',
            balance: 650,
            categoryId: 7,
            categoryRuleId: 3,
            categorySetManually: false,
            isExcluded: false,
            category: { id: 7, name: 'Excluded', color: '#64748b', isProtected: true },
          },
        ],
      }),
    ]);

    expect(service.allTransactions().map((t) => t.id)).toEqual([1]);
  });

  it('monthlySummaries() ignores transactions labelled Excluded without the isExcluded flag', () => {
    service.reload();
    flushLoadAll(controller, [
      makeStatement({
        id: 1,
        bank: 'BPI',
        periodFrom: '2024-01-01',
        periodTo: '2024-01-31',
        closingBalance: 650,
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
            description: 'Stale exclusion credit',
            amount: 500,
            type: 'credit',
            balance: 1400,
            categoryId: 7,
            categoryRuleId: 3,
            categorySetManually: false,
            isExcluded: false,
            category: { id: 7, name: 'Excluded', color: '#64748b', isProtected: true },
          },
        ],
      }),
    ]);

    const jan = service.monthlySummaries().find((s) => s.month === '2024-01');
    expect(jan?.income).toBe(0);
    expect(jan?.expenses).toBe(100);
  });
  describe('category netting (ADR-037)', () => {
    let nextId = 100;
    const eatingOut = { id: 3, name: 'Eating out', color: '#e0806b', isProtected: false };
    const salary = { id: 4, name: 'Salary', color: '#36ab7a', isProtected: false };
    function tx(
      amount: number,
      type: 'credit' | 'debit',
      category: typeof eatingOut | null,
      datePosting = '2024-03-10',
    ): Transaction {
      return {
        id: nextId++,
        statementId: 1,
        datePosting,
        dateValue: datePosting,
        description: 'TX',
        amount,
        type,
        balance: 0,
        categoryId: category?.id ?? null,
        categoryRuleId: null,
        categorySetManually: false,
        isExcluded: false,
        category,
      };
    }

    function load(byBank: Record<string, Transaction[]>) {
      service.reload();
      flushLoadAll(
        controller,
        Object.entries(byBank).map(([bank, transactions], i) =>
          makeStatement({ id: i + 1, bank, periodTo: '2024-03-31', transactions }),
        ),
      );
    }

    it('monthTotals() nets a dinner paid back into another bank to what it cost', () => {
      load({
        BPI: [tx(100, 'debit', eatingOut), tx(25, 'credit', eatingOut)],
        REVOLUT: [tx(25, 'credit', eatingOut), tx(25, 'credit', eatingOut)],
      });

      expect(service.monthTotals()).toEqual([
        { month: '2024-03', income: 0, expenses: 25, net: -25 },
      ]);
    });

    it('monthlySummaries() nets each category within its bank', () => {
      load({
        BPI: [tx(100, 'debit', eatingOut), tx(25, 'credit', eatingOut)],
        REVOLUT: [tx(50, 'credit', eatingOut)],
      });

      expect(service.monthlySummaries().map((s) => [s.bank, s.income, s.expenses])).toEqual([
        ['BPI', 0, 75],
        ['REVOLUT', 50, 0],
      ]);
    });

    it('monthTotals() keeps what a mixed month kept, one row per month, newest first', () => {
      const rows = [
        tx(100, 'debit', eatingOut),
        tx(75, 'credit', eatingOut),
        tx(2650, 'credit', salary),
        tx(12.4, 'debit', salary),
        tx(42.18, 'debit', null),
        tx(5, 'credit', null),
        tx(9, 'debit', null, '2024-02-27'),
      ];
      load({ BPI: rows });
      const march = rows.filter((r) => r.datePosting.startsWith('2024-03'));
      const kept = march.reduce((s, r) => s + (r.type === 'credit' ? r.amount : -r.amount), 0);

      const totals = service.monthTotals();

      expect(totals.map((t) => t.month)).toEqual(['2024-03', '2024-02']);
      expect(totals[0].income - totals[0].expenses).toBeCloseTo(kept, 10);
      expect(totals[0].net).toBeCloseTo(kept, 10);
      expect(totals[0].income).toBeCloseTo(2637.6 + 5, 10);
      expect(totals[0].expenses).toBeCloseTo(25 + 42.18, 10);
      expect(totals[1]).toEqual({ month: '2024-02', income: 0, expenses: 9, net: -9 });
    });
  });
});
