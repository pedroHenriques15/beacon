import { describe, it, expect, beforeEach, vi } from 'vitest';
import { TestBed, ComponentFixture } from '@angular/core/testing';
import { signal } from '@angular/core';
import { provideRouter } from '@angular/router';
import { DashboardComponent, bankInitials } from './dashboard';
import { FinanceService } from '../../core/services/finance.service';
import { InvestmentsService } from '../../core/services/investments.service';
import { CATEGORY_UNKNOWN } from '../../core/constants/categories';

interface MonthRow {
  month: string;
  bank: string;
  income: number;
  expenses: number;
  net: number;
  closingBalance: number;
}

function makeSummary(overrides: Partial<MonthRow> = {}): MonthRow {
  return {
    month: '2025-01',
    bank: 'BPI',
    income: 0,
    expenses: 0,
    net: 0,
    closingBalance: 0,
    ...overrides,
  };
}

function makeTx(overrides: Record<string, unknown> = {}) {
  return {
    id: 1,
    statementId: 1,
    datePosting: '2025-01-10',
    dateValue: '2025-01-10',
    description: 'test',
    amount: 10,
    type: 'debit',
    balance: 0,
    categoryId: null,
    categoryRuleId: null,
    categorySetManually: false,
    isExcluded: false,
    category: null,
    bank: 'BPI',
    month: '2025-01',
    ...overrides,
  };
}

function monthKey(offset = 0): string {
  const d = new Date();
  d.setUTCDate(1);
  d.setUTCMonth(d.getUTCMonth() + offset);
  return d.toISOString().slice(0, 7);
}

describe('DashboardComponent', () => {
  let fixture: ComponentFixture<DashboardComponent>;
  let component: DashboardComponent;

  const statementsSignal = signal<unknown[]>([]);
  const latestPerBankSignal = signal(new Map());
  const totalBalanceSignal = signal(0);
  const monthlySummariesSignal = signal<MonthRow[]>([]);
  const allTransactionsSignal = signal<ReturnType<typeof makeTx>[]>([]);

  const investmentAssetsSignal = signal<unknown[]>([]);
  const totalCurrentValueSignal = signal(0);
  const totalUnrealizedPnlSignal = signal(0);
  const totalUnrealizedPctSignal = signal<number | null>(null);
  const portfolioChange1dSignal = signal<number | null>(null);
  const loadPriceHistory = vi.fn();

  beforeEach(() => {
    statementsSignal.set([]);
    latestPerBankSignal.set(new Map());
    totalBalanceSignal.set(0);
    monthlySummariesSignal.set([]);
    allTransactionsSignal.set([]);
    investmentAssetsSignal.set([]);
    totalCurrentValueSignal.set(0);
    totalUnrealizedPnlSignal.set(0);
    totalUnrealizedPctSignal.set(null);
    portfolioChange1dSignal.set(null);
    loadPriceHistory.mockClear();

    TestBed.configureTestingModule({
      imports: [DashboardComponent],
      providers: [
        provideRouter([]),
        {
          provide: FinanceService,
          useValue: {
            loading: signal(false),
            error: signal(null),
            statements: statementsSignal,
            latestPerBank: latestPerBankSignal,
            totalBalance: totalBalanceSignal,
            monthlySummaries: monthlySummariesSignal,
            allTransactions: allTransactionsSignal,
            unknownTypeCount: signal(0),
          },
        },
        {
          provide: InvestmentsService,
          useValue: {
            assets: investmentAssetsSignal,
            loading: signal(false),
            totalCurrentValue: totalCurrentValueSignal,
            totalUnrealizedPnl: totalUnrealizedPnlSignal,
            totalUnrealizedPct: totalUnrealizedPctSignal,
            portfolioChange1d: portfolioChange1dSignal,
            portfolioHistory: signal([]),
            assetMetrics: signal([]),
            loadPriceHistory,
          },
        },
      ],
    });

    fixture = TestBed.createComponent(DashboardComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  describe('netWorth', () => {
    it('sums cash and invested totals', () => {
      totalBalanceSignal.set(1000);
      totalCurrentValueSignal.set(250.5);
      expect(component.netWorth()).toBeCloseTo(1250.5);
    });

    it('equals cash when there are no investments', () => {
      totalBalanceSignal.set(1000);
      expect(component.netWorth()).toBe(1000);
    });

    it('is zero when both are zero', () => {
      expect(component.netWorth()).toBe(0);
    });
  });

  describe('monthSnapshot', () => {
    it('shows the latest closed month, skipping the in-progress current month', () => {
      monthlySummariesSignal.set([
        makeSummary({ month: monthKey(0), income: 999, expenses: 1, net: 998 }),
        makeSummary({ month: monthKey(-1), income: 100, expenses: 40, net: 60 }),
        makeSummary({ month: monthKey(-2), income: 80, expenses: 40, net: 40 }),
      ]);
      const snap = component.monthSnapshot();
      expect(snap.month).toBe(monthKey(-1));
      expect(snap.income).toBe(100);
      expect(snap.incomeDelta).toBeCloseTo(25);
    });

    it('falls back to the current month when it is the only month with data', () => {
      monthlySummariesSignal.set([
        makeSummary({ month: monthKey(0), income: 999, expenses: 1, net: 998 }),
      ]);
      const snap = component.monthSnapshot();
      expect(snap.month).toBe(monthKey(0));
      expect(snap.income).toBe(999);
    });

    it('uses the latest closed month when the current month has no data', () => {
      monthlySummariesSignal.set([
        makeSummary({ month: '2025-02', income: 200, expenses: 80, net: 120 }),
        makeSummary({ month: '2025-01', income: 100, expenses: 50, net: 50 }),
      ]);
      const snap = component.monthSnapshot();
      expect(snap.month).toBe('2025-02');
      expect(snap.income).toBe(200);
    });

    it('aggregates multiple banks for the same month', () => {
      monthlySummariesSignal.set([
        makeSummary({ month: '2025-02', bank: 'BPI', income: 100, expenses: 30, net: 70 }),
        makeSummary({ month: '2025-02', bank: 'REVOLUT', income: 50, expenses: 20, net: 30 }),
      ]);
      const snap = component.monthSnapshot();
      expect(snap.income).toBe(150);
      expect(snap.expenses).toBe(50);
      expect(snap.net).toBe(100);
    });

    it('is not affected by the selected bank filter', () => {
      monthlySummariesSignal.set([
        makeSummary({ month: '2025-02', bank: 'BPI', income: 100, expenses: 30, net: 70 }),
        makeSummary({ month: '2025-02', bank: 'REVOLUT', income: 50, expenses: 20, net: 30 }),
      ]);
      component.selectedBank.set('BPI');
      expect(component.monthSnapshot().income).toBe(150);
    });

    it('computes percentage deltas vs the previous month', () => {
      monthlySummariesSignal.set([
        makeSummary({ month: '2025-02', income: 150, expenses: 60, net: 90 }),
        makeSummary({ month: '2025-01', income: 100, expenses: 80, net: 20 }),
      ]);
      const snap = component.monthSnapshot();
      expect(snap.incomeDelta).toBeCloseTo(50);
      expect(snap.expensesDelta).toBeCloseTo(-25);
      expect(snap.netDelta).toBeCloseTo(70); // absolute EUR, not %
    });

    it('handles the January → December year boundary', () => {
      monthlySummariesSignal.set([
        makeSummary({ month: '2025-01', income: 200, expenses: 50, net: 150 }),
        makeSummary({ month: '2024-12', income: 100, expenses: 100, net: 0 }),
      ]);
      const snap = component.monthSnapshot();
      expect(snap.month).toBe('2025-01');
      expect(snap.incomeDelta).toBeCloseTo(100);
    });

    it('returns null deltas when there is no previous month', () => {
      monthlySummariesSignal.set([
        makeSummary({ month: '2025-02', income: 150, expenses: 60, net: 90 }),
      ]);
      const snap = component.monthSnapshot();
      expect(snap.incomeDelta).toBeNull();
      expect(snap.expensesDelta).toBeNull();
      expect(snap.netDelta).toBeNull();
    });

    it('returns null percentage deltas when the previous month value is zero', () => {
      monthlySummariesSignal.set([
        makeSummary({ month: '2025-02', income: 150, expenses: 60, net: 90 }),
        makeSummary({ month: '2025-01', income: 0, expenses: 0, net: 0 }),
      ]);
      const snap = component.monthSnapshot();
      expect(snap.incomeDelta).toBeNull();
      expect(snap.expensesDelta).toBeNull();
      expect(snap.netDelta).toBe(90);
    });
  });

  describe('selectedMonth', () => {
    it('starts at the latest closed month', () => {
      monthlySummariesSignal.set([
        makeSummary({ month: monthKey(0), income: 5, net: 5 }),
        makeSummary({ month: monthKey(-1), income: 3, net: 3 }),
      ]);
      expect(component.selectedMonth()).toBe(monthKey(-1));
    });

    it('drives the snapshot when the scrubber picks another month', () => {
      monthlySummariesSignal.set([
        makeSummary({ month: '2025-02', income: 200, expenses: 80, net: 120 }),
        makeSummary({ month: '2025-01', income: 100, expenses: 50, net: 50 }),
        makeSummary({ month: '2024-12', income: 40, expenses: 10, net: 30 }),
      ]);
      component.selectedMonth.set('2025-01');
      const snap = component.monthSnapshot();
      expect(snap.month).toBe('2025-01');
      expect(snap.income).toBe(100);
      expect(snap.prev?.income).toBe(40);
    });

    it('keeps the choice when the data reloads with the same months', () => {
      monthlySummariesSignal.set([
        makeSummary({ month: '2025-02', income: 2, net: 2 }),
        makeSummary({ month: '2025-01', income: 1, net: 1 }),
      ]);
      component.selectedMonth.set('2025-01');
      monthlySummariesSignal.set([
        makeSummary({ month: '2025-02', income: 3, net: 3 }),
        makeSummary({ month: '2025-01', income: 1, net: 1 }),
      ]);
      expect(component.selectedMonth()).toBe('2025-01');
    });

    it('lists the scrubber months oldest first', () => {
      monthlySummariesSignal.set([
        makeSummary({ month: '2025-02', income: 2, expenses: 1, net: 1 }),
        makeSummary({ month: '2025-01', bank: 'BPI', income: 1, net: 1 }),
        makeSummary({ month: '2025-01', bank: 'REVOLUT', income: 4, net: 4 }),
      ]);
      expect(component.months()).toEqual([
        { key: '2025-01', income: 5, expenses: 0 },
        { key: '2025-02', income: 2, expenses: 1 },
      ]);
    });

    it('limits the six months to those up to the selected one', () => {
      monthlySummariesSignal.set(
        Array.from({ length: 9 }, (_, i) =>
          makeSummary({ month: `2025-0${i + 1}`, income: i, net: i }),
        ),
      );
      component.selectedMonth.set('2025-07');
      expect(component.sixMonths().map((r) => r.month)).toEqual([
        '2025-07',
        '2025-06',
        '2025-05',
        '2025-04',
        '2025-03',
        '2025-02',
      ]);
    });
  });

  describe('uncategorised', () => {
    it('counts the selected month’s rows without a category', () => {
      monthlySummariesSignal.set([makeSummary({ month: '2025-01', income: 1, net: 1 })]);
      allTransactionsSignal.set([
        makeTx({ id: 1, amount: 12 }),
        makeTx({ id: 2, amount: 3, categoryId: 4, category: { name: 'Food', color: '#f00' } }),
        makeTx({ id: 3, amount: 8, month: '2024-12' }),
      ]);
      expect(component.uncategorised()).toEqual({ count: 1, amount: 12 });
    });
  });

  it('loads a year of prices for the Investments sparkline', () => {
    expect(loadPriceHistory).toHaveBeenCalledTimes(1);
  });

  describe('bankInitials', () => {
    it('abbreviates bank names for the avatars', () => {
      expect(bankInitials('BPI')).toBe('BPI');
      expect(bankInitials('ActivoBank')).toBe('AB');
      expect(bankInitials('Trade Republic')).toBe('TR');
      expect(bankInitials('Revolut')).toBe('RE');
    });
  });

  describe('topCategories', () => {
    beforeEach(() => {
      monthlySummariesSignal.set([makeSummary({ month: '2025-01', income: 1, net: 1 })]);
    });

    it('groups debit transactions of the snapshot month by category', () => {
      allTransactionsSignal.set([
        makeTx({ amount: 30, category: { name: 'Food', color: '#f00' } }),
        makeTx({ amount: 20, category: { name: 'Food', color: '#f00' } }),
        makeTx({ amount: 10, category: { name: 'Transport', color: '#0f0' } }),
      ]);
      const cats = component.topCategories();
      expect(cats).toHaveLength(2);
      expect(cats[0]).toMatchObject({ label: 'Food', color: '#f00', total: 50, pct: 100 });
      expect(cats[1]).toMatchObject({ label: 'Transport', total: 10, pct: 20 });
    });

    it('ignores credit transactions and other months', () => {
      allTransactionsSignal.set([
        makeTx({ amount: 30, type: 'credit', category: { name: 'Salary', color: '#00f' } }),
        makeTx({ amount: 40, month: '2024-12', category: { name: 'Food', color: '#f00' } }),
        makeTx({ amount: 5, category: { name: 'Food', color: '#f00' } }),
      ]);
      const cats = component.topCategories();
      expect(cats).toHaveLength(1);
      expect(cats[0].total).toBe(5);
    });

    it('falls back to the Unknown category and the fallback colour token', () => {
      allTransactionsSignal.set([makeTx({ amount: 7 })]);
      const cats = component.topCategories();
      expect(cats[0].label).toBe(CATEGORY_UNKNOWN);
      expect(cats[0].color).toBe('var(--category-fallback)');
    });

    it('lists every category, largest first, with its share of the month', () => {
      allTransactionsSignal.set(
        Array.from({ length: 7 }, (_, i) =>
          makeTx({ id: i, amount: i + 1, category: { name: `Cat${i}`, color: '#111' } }),
        ),
      );
      const cats = component.topCategories();
      expect(cats).toHaveLength(7);
      expect(cats[0]).toMatchObject({ label: 'Cat6', total: 7, share: 25 });
    });

    it('follows the selected month', () => {
      monthlySummariesSignal.set([
        makeSummary({ month: '2025-01', income: 1, net: 1 }),
        makeSummary({ month: '2024-12', income: 1, net: 1 }),
      ]);
      allTransactionsSignal.set([
        makeTx({ amount: 5, category: { name: 'Food', color: '#f00' } }),
        makeTx({ amount: 9, month: '2024-12', category: { name: 'Rent', color: '#00f' } }),
      ]);
      component.selectedMonth.set('2024-12');
      expect(component.topCategories().map((c) => c.label)).toEqual(['Rent']);
    });

    it('is empty when there are no debit transactions', () => {
      allTransactionsSignal.set([]);
      expect(component.topCategories()).toHaveLength(0);
    });
  });

  describe('formatSignedPct', () => {
    it('formats null as an em dash', () => {
      expect(component.formatSignedPct(null)).toBe('—');
    });

    it('prefixes positive values with +', () => {
      expect(component.formatSignedPct(1.55)).toBe('+1.6%');
    });

    it('keeps the minus sign for negative values', () => {
      expect(component.formatSignedPct(-2.34)).toBe('-2.3%');
    });
  });
});
