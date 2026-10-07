import { describe, it, expect, beforeEach, vi } from 'vitest';
import { TestBed, ComponentFixture } from '@angular/core/testing';
import { signal } from '@angular/core';
import { provideRouter } from '@angular/router';
import { DashboardComponent } from './dashboard';
import { bankInitials } from '../../core/utils/bank';
import { FinanceService } from '../../core/services/finance.service';
import { InvestmentsService, PortfolioPoint } from '../../core/services/investments.service';
import { CATEGORY_UNKNOWN } from '../../core/constants/categories';
import { MonthTotals } from '../../core/utils/month-totals';

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

function makeTotals(overrides: Partial<MonthTotals> = {}): MonthTotals {
  return { month: '2025-01', income: 0, expenses: 0, net: 0, ...overrides };
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
  const monthTotalsSignal = signal<MonthTotals[]>([]);
  const allTransactionsSignal = signal<ReturnType<typeof makeTx>[]>([]);

  const investmentAssetsSignal = signal<unknown[]>([]);
  const totalCurrentValueSignal = signal(0);
  const totalUnrealizedPnlSignal = signal(0);
  const totalUnrealizedPctSignal = signal<number | null>(null);
  const portfolioChange1dSignal = signal<number | null>(null);
  const totalReturnSignal = signal(0);
  const totalReturnPctSignal = signal<number | null>(null);
  const firstBuyDateSignal = signal<string | null>(null);
  const portfolioHistorySignal = signal<PortfolioPoint[]>([]);
  const loadPriceHistory = vi.fn();

  beforeEach(() => {
    statementsSignal.set([]);
    latestPerBankSignal.set(new Map());
    totalBalanceSignal.set(0);
    monthlySummariesSignal.set([]);
    monthTotalsSignal.set([]);
    allTransactionsSignal.set([]);
    investmentAssetsSignal.set([]);
    totalCurrentValueSignal.set(0);
    totalUnrealizedPnlSignal.set(0);
    totalUnrealizedPctSignal.set(null);
    portfolioChange1dSignal.set(null);
    totalReturnSignal.set(0);
    totalReturnPctSignal.set(null);
    firstBuyDateSignal.set(null);
    portfolioHistorySignal.set([]);
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
            monthTotals: monthTotalsSignal,
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
            totalReturn: totalReturnSignal,
            totalReturnPct: totalReturnPctSignal,
            firstBuyDate: firstBuyDateSignal,
            portfolioHistory: portfolioHistorySignal,
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

  describe('netWorthChange', () => {
    beforeEach(() => {
      monthTotalsSignal.set([makeTotals({ month: '2025-02', income: 1, net: 1 })]);
      component.selectedMonth.set('2025-02');
      statementsSignal.set([{ bank: 'BPI', periodTo: '2025-01-31', closingBalance: 1000 }]);
      investmentAssetsSignal.set([{ lots: [{ date: '2025-01-10' }] }]);
    });

    it('splits the change into investment growth and what the accounts kept', () => {
      totalBalanceSignal.set(2000);
      totalCurrentValueSignal.set(600);
      portfolioHistorySignal.set([
        { date: '2025-01-31', totalValue: 500, invested: 500 },
        { date: '2025-02-28', totalValue: 600, invested: 500 },
      ]);
      expect(component.netWorthChange()).toEqual({
        amount: 1100,
        since: '2025-01-31',
        growth: 100,
        rest: 1000,
      });
    });

    it('moves neither part for a buy paid from cash', () => {
      // 1,000 kept, 500 of it moved into investments that then grew by 100.
      totalBalanceSignal.set(1500);
      totalCurrentValueSignal.set(1100);
      portfolioHistorySignal.set([
        { date: '2025-01-31', totalValue: 500, invested: 500 },
        { date: '2025-02-28', totalValue: 1100, invested: 1000 },
      ]);
      expect(component.netWorthChange()).toMatchObject({ amount: 1100, growth: 100, rest: 1000 });
    });

    it('has no parts without investments', () => {
      investmentAssetsSignal.set([]);
      totalBalanceSignal.set(1300);
      expect(component.netWorthChange()).toMatchObject({ amount: 300, growth: null, rest: null });
    });
  });

  describe('monthSnapshot', () => {
    it('shows the latest closed month, skipping the in-progress current month', () => {
      monthTotalsSignal.set([
        makeTotals({ month: monthKey(0), income: 999, expenses: 1, net: 998 }),
        makeTotals({ month: monthKey(-1), income: 100, expenses: 40, net: 60 }),
        makeTotals({ month: monthKey(-2), income: 80, expenses: 40, net: 40 }),
      ]);
      const snap = component.monthSnapshot();
      expect(snap.month).toBe(monthKey(-1));
      expect(snap.income).toBe(100);
      expect(snap.incomeDelta).toBeCloseTo(25);
    });

    it('falls back to the current month when it is the only month with data', () => {
      monthTotalsSignal.set([
        makeTotals({ month: monthKey(0), income: 999, expenses: 1, net: 998 }),
      ]);
      const snap = component.monthSnapshot();
      expect(snap.month).toBe(monthKey(0));
      expect(snap.income).toBe(999);
    });

    it('uses the latest closed month when the current month has no data', () => {
      monthTotalsSignal.set([
        makeTotals({ month: '2025-02', income: 200, expenses: 80, net: 120 }),
        makeTotals({ month: '2025-01', income: 100, expenses: 50, net: 50 }),
      ]);
      const snap = component.monthSnapshot();
      expect(snap.month).toBe('2025-02');
      expect(snap.income).toBe(200);
    });

    it('takes every bank together from the month totals, netted across banks', () => {
      // A dinner paid from BPI and paid back into Revolut nets only in the month totals.
      monthlySummariesSignal.set([
        makeSummary({ month: '2025-02', bank: 'BPI', income: 0, expenses: 100, net: -100 }),
        makeSummary({ month: '2025-02', bank: 'REVOLUT', income: 75, expenses: 0, net: 75 }),
      ]);
      monthTotalsSignal.set([makeTotals({ month: '2025-02', income: 0, expenses: 25, net: -25 })]);
      const snap = component.monthSnapshot();
      expect(snap.income).toBe(0);
      expect(snap.expenses).toBe(25);
      expect(snap.net).toBe(-25);
    });

    it('is not affected by the selected bank filter', () => {
      monthlySummariesSignal.set([
        makeSummary({ month: '2025-02', bank: 'BPI', income: 100, expenses: 30, net: 70 }),
        makeSummary({ month: '2025-02', bank: 'REVOLUT', income: 50, expenses: 20, net: 30 }),
      ]);
      monthTotalsSignal.set([
        makeTotals({ month: '2025-02', income: 150, expenses: 50, net: 100 }),
      ]);
      component.selectedBank.set('BPI');
      expect(component.monthSnapshot().income).toBe(150);
    });

    it('computes percentage deltas vs the previous month', () => {
      monthTotalsSignal.set([
        makeTotals({ month: '2025-02', income: 150, expenses: 60, net: 90 }),
        makeTotals({ month: '2025-01', income: 100, expenses: 80, net: 20 }),
      ]);
      const snap = component.monthSnapshot();
      expect(snap.incomeDelta).toBeCloseTo(50);
      expect(snap.expensesDelta).toBeCloseTo(-25);
      expect(snap.netDelta).toBeCloseTo(70); // absolute EUR, not %
    });

    it('handles the January → December year boundary', () => {
      monthTotalsSignal.set([
        makeTotals({ month: '2025-01', income: 200, expenses: 50, net: 150 }),
        makeTotals({ month: '2024-12', income: 100, expenses: 100, net: 0 }),
      ]);
      const snap = component.monthSnapshot();
      expect(snap.month).toBe('2025-01');
      expect(snap.incomeDelta).toBeCloseTo(100);
    });

    it('returns null deltas when there is no previous month', () => {
      monthTotalsSignal.set([makeTotals({ month: '2025-02', income: 150, expenses: 60, net: 90 })]);
      const snap = component.monthSnapshot();
      expect(snap.incomeDelta).toBeNull();
      expect(snap.expensesDelta).toBeNull();
      expect(snap.netDelta).toBeNull();
    });

    it('returns null percentage deltas when the previous month value is zero', () => {
      monthTotalsSignal.set([
        makeTotals({ month: '2025-02', income: 150, expenses: 60, net: 90 }),
        makeTotals({ month: '2025-01', income: 0, expenses: 0, net: 0 }),
      ]);
      const snap = component.monthSnapshot();
      expect(snap.incomeDelta).toBeNull();
      expect(snap.expensesDelta).toBeNull();
      expect(snap.netDelta).toBe(90);
    });
  });

  describe('savings rate', () => {
    it('gives the month’s share kept, none without income', () => {
      monthTotalsSignal.set([
        makeTotals({ month: '2025-02', income: 2500, expenses: 1700, net: 800 }),
        makeTotals({ month: '2025-01', expenses: 50, net: -50 }),
      ]);
      component.selectedMonth.set('2025-02');
      expect(component.monthSnapshot().keptShare).toBe(32);
      component.selectedMonth.set('2025-01');
      expect(component.monthSnapshot().keptShare).toBeNull();
    });
  });

  describe('selectedMonth', () => {
    it('starts at the latest closed month', () => {
      monthTotalsSignal.set([
        makeTotals({ month: monthKey(0), income: 5, net: 5 }),
        makeTotals({ month: monthKey(-1), income: 3, net: 3 }),
      ]);
      expect(component.selectedMonth()).toBe(monthKey(-1));
    });

    it('drives the snapshot when the scrubber picks another month', () => {
      monthTotalsSignal.set([
        makeTotals({ month: '2025-02', income: 200, expenses: 80, net: 120 }),
        makeTotals({ month: '2025-01', income: 100, expenses: 50, net: 50 }),
        makeTotals({ month: '2024-12', income: 40, expenses: 10, net: 30 }),
      ]);
      component.selectedMonth.set('2025-01');
      const snap = component.monthSnapshot();
      expect(snap.month).toBe('2025-01');
      expect(snap.income).toBe(100);
      expect(snap.prev?.income).toBe(40);
    });

    it('keeps the choice when the data reloads with the same months', () => {
      monthTotalsSignal.set([
        makeTotals({ month: '2025-02', income: 2, net: 2 }),
        makeTotals({ month: '2025-01', income: 1, net: 1 }),
      ]);
      component.selectedMonth.set('2025-01');
      monthTotalsSignal.set([
        makeTotals({ month: '2025-02', income: 3, net: 3 }),
        makeTotals({ month: '2025-01', income: 1, net: 1 }),
      ]);
      expect(component.selectedMonth()).toBe('2025-01');
    });

    it('lists the scrubber months oldest first', () => {
      monthTotalsSignal.set([
        makeTotals({ month: '2025-02', income: 2, expenses: 1, net: 1 }),
        makeTotals({ month: '2025-01', income: 5, net: 5 }),
      ]);
      expect(component.months()).toEqual([
        { key: '2025-01', income: 5, expenses: 0 },
        { key: '2025-02', income: 2, expenses: 1 },
      ]);
    });

    it('shows only the selected bank’s rows in the six months', () => {
      monthTotalsSignal.set([
        makeTotals({ month: '2025-02', income: 150, expenses: 50, net: 100 }),
      ]);
      monthlySummariesSignal.set([
        makeSummary({ month: '2025-02', bank: 'BPI', income: 100, expenses: 30, net: 70 }),
        makeSummary({ month: '2025-02', bank: 'REVOLUT', income: 50, expenses: 20, net: 30 }),
      ]);
      expect(component.sixMonths()[0]).toMatchObject({ income: 150, expenses: 50 });
      component.selectedBank.set('REVOLUT');
      expect(component.sixMonths()[0]).toMatchObject({ income: 50, expenses: 20, net: 30 });
    });

    it('limits the six months to those up to the selected one', () => {
      // Newest first, as FinanceService.monthTotals.
      monthTotalsSignal.set(
        Array.from({ length: 9 }, (_, i) =>
          makeTotals({ month: `2025-0${9 - i}`, income: i, net: i }),
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

    it('takes calendar months, an empty one as a zero row, none before the first', () => {
      monthTotalsSignal.set([
        makeTotals({ month: '2025-04', income: 100, expenses: 40, net: 60 }),
        makeTotals({ month: '2025-02', income: 50, expenses: 10, net: 40 }),
      ]);
      component.selectedMonth.set('2025-04');
      expect(component.sixMonths().map((r) => [r.month, r.income])).toEqual([
        ['2025-04', 100],
        ['2025-03', 0],
        ['2025-02', 50],
      ]);
    });

    it('nets the months together, averages them and gives the share kept', () => {
      const cat = (name: string) => ({ name, color: '#fff' });
      monthTotalsSignal.set([
        makeTotals({ month: '2025-02', income: 2060, expenses: 1500, net: 560 }),
        makeTotals({ month: '2025-01', income: 1000, expenses: 1220, net: -220 }),
      ]);
      allTransactionsSignal.set([
        makeTx({ month: '2025-01', amount: 1000, type: 'credit', category: cat('Salary') }),
        makeTx({ month: '2025-01', amount: 1100, category: cat('Rent') }),
        makeTx({ month: '2025-01', amount: 120, category: cat('Dinner') }),
        makeTx({ month: '2025-02', amount: 2000, type: 'credit', category: cat('Salary') }),
        makeTx({ month: '2025-02', amount: 1500, category: cat('Rent') }),
        // January's dinner paid back in February: income that month, a lower cost over both.
        makeTx({ month: '2025-02', amount: 60, type: 'credit', category: cat('Dinner') }),
      ]);
      component.selectedMonth.set('2025-02');
      expect(component.sixMonths().map((r) => r.keptShare)).toEqual([27, -22]);
      expect(component.sixSummary()).toEqual({
        income: 3000,
        expenses: 2660,
        net: 340,
        keptShare: 11,
        average: { income: 1500, expenses: 1330, net: 170 },
      });
    });

    it('links the six months to Insights, unless an account is picked', () => {
      component.selectedMonth.set('2025-02');
      expect(component.sixLink()).toBe('/analytics');
      expect(component.insightsQuery('Unknown', 'in', 6)).toEqual({
        month: '2025-02',
        side: 'in',
        months: '6',
        category: 'unknown',
      });
      component.selectedBank.set('BPI');
      expect(component.sixLink()).toBeNull();
    });

    it('has no summary without months', () => {
      expect(component.sixSummary()).toBeNull();
    });
  });

  describe('sixTops', () => {
    const cat = (name: string) => ({ name, color: `var(--${name})` });

    beforeEach(() => {
      monthTotalsSignal.set([
        makeTotals({ month: '2025-03', income: 1, net: 1 }),
        makeTotals({ month: '2025-02', income: 1, net: 1 }),
      ]);
      allTransactionsSignal.set([
        makeTx({ id: 1, month: '2025-02', amount: 120, category: cat('Dinner') }),
        makeTx({ id: 2, month: '2025-03', amount: 90, type: 'credit', category: cat('Dinner') }),
        makeTx({ id: 3, month: '2025-03', amount: 50, category: cat('Food'), bank: 'REVOLUT' }),
        makeTx({ id: 4, month: '2025-03', amount: 900, type: 'credit', category: cat('Salary') }),
        makeTx({ id: 5, month: '2024-08', amount: 999, category: cat('Old') }),
      ]);
      component.selectedMonth.set('2025-03');
    });

    it('nets each category over the six months, a payback the next month included', () => {
      expect(component.sixTops().spending.map((c) => [c.label, c.total])).toEqual([
        ['Food', 50],
        ['Dinner', 30],
      ]);
      expect(component.sixTops().income.map((c) => [c.label, c.total])).toEqual([['Salary', 900]]);
    });

    it('follows the picked account and the selected month', () => {
      component.selectedBank.set('REVOLUT');
      monthlySummariesSignal.set([
        makeSummary({ month: '2025-03', bank: 'REVOLUT', expenses: 50, net: -50 }),
      ]);
      expect(component.sixTops().spending.map((c) => c.label)).toEqual(['Food']);
      expect(component.sixTops().income).toEqual([]);

      component.selectedBank.set(null);
      component.selectedMonth.set('2025-02');
      expect(component.sixTops().spending.map((c) => [c.label, c.total])).toEqual([
        ['Dinner', 120],
      ]);
    });
  });

  describe('uncategorised', () => {
    it('counts the selected month’s rows without a category', () => {
      monthTotalsSignal.set([makeTotals({ month: '2025-01', income: 1, net: 1 })]);
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

  describe('Investments row', () => {
    function text(selector: string): string {
      const el: HTMLElement | null = fixture.nativeElement.querySelector(selector);
      return el?.textContent?.replace(/\s+/g, ' ').trim() ?? '';
    }

    it('shows the total return since the first buy after the value, then today’s change', () => {
      investmentAssetsSignal.set([{}]);
      totalCurrentValueSignal.set(2210);
      totalReturnSignal.set(350);
      totalReturnPctSignal.set(17.5);
      firstBuyDateSignal.set('2026-01-24');
      portfolioChange1dSignal.set(0.4);
      fixture.detectChanges();

      const parts = [...fixture.nativeElement.querySelectorAll('.invest-row__text > span')].map(
        (el: HTMLElement) => el.className,
      );
      expect(parts).toEqual([
        'invest-row__label',
        'invest-row__value',
        'invest-row__return',
        'invest-row__meta',
      ]);
      expect(text('.invest-row__value')).toBe('€2,210.00');
      expect(text('.invest-row__return')).toBe('+€350.00 (+17.5%) return since Jan 2026');
      expect(text('.invest-row__meta')).toBe('+0.4% today');
    });

    it('marks a loss and leaves the percentage out without money put in', () => {
      investmentAssetsSignal.set([{}]);
      totalReturnSignal.set(-12.3);
      fixture.detectChanges();

      expect(text('.invest-row__return')).toBe('−€12.30 return');
      expect(
        fixture.nativeElement.querySelector('.invest-row__return .invest-row__change--down'),
      ).not.toBeNull();
    });
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
      monthTotalsSignal.set([makeTotals({ month: '2025-01', income: 1, net: 1 })]);
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
      monthTotalsSignal.set([
        makeTotals({ month: '2025-01', income: 1, net: 1 }),
        makeTotals({ month: '2024-12', income: 1, net: 1 }),
      ]);
      allTransactionsSignal.set([
        makeTx({ amount: 5, category: { name: 'Food', color: '#f00' } }),
        makeTx({ amount: 9, month: '2024-12', category: { name: 'Rent', color: '#00f' } }),
      ]);
      component.selectedMonth.set('2024-12');
      expect(component.topCategories().map((c) => c.label)).toEqual(['Rent']);
    });

    it('nets a dinner paid back by friends to what it cost', () => {
      const eatingOut = { name: 'Eating out', color: '#e0806b' };
      allTransactionsSignal.set([
        makeTx({ id: 1, amount: 100, category: eatingOut }),
        makeTx({ id: 2, amount: 25, type: 'credit', category: eatingOut }),
        makeTx({ id: 3, amount: 25, type: 'credit', category: eatingOut }),
        makeTx({ id: 4, amount: 25, type: 'credit', category: eatingOut }),
        makeTx({ id: 5, amount: 50, category: { name: 'Fuel', color: '#0f0' } }),
      ]);
      expect(component.topCategories().map((c) => [c.label, c.total])).toEqual([
        ['Fuel', 50],
        ['Eating out', 25],
      ]);
      expect(component.river().total).toBe(75);
    });

    it('leaves out a category paid back in full or netting to income', () => {
      const gifts = { name: 'Gifts', color: '#f0f' };
      const trips = { name: 'Trips', color: '#0ff' };
      allTransactionsSignal.set([
        makeTx({ id: 1, amount: 60, category: gifts }),
        makeTx({ id: 2, amount: 60, type: 'credit', category: gifts }),
        makeTx({ id: 3, amount: 40, category: trips }),
        makeTx({ id: 4, amount: 90, type: 'credit', category: trips }),
        makeTx({ id: 5, amount: 5, category: { name: 'Food', color: '#f00' } }),
      ]);
      expect(component.topCategories().map((c) => c.label)).toEqual(['Food']);
    });

    it('is empty when there are no debit transactions', () => {
      allTransactionsSignal.set([]);
      expect(component.topCategories()).toHaveLength(0);
    });
  });

  describe('insightsQuery', () => {
    it('opens Insights on the selected month’s spending, on a category by its id', () => {
      monthTotalsSignal.set([makeTotals({ month: '2025-03', expenses: 10, net: -10 })]);
      allTransactionsSignal.set([
        makeTx({ month: '2025-03', category: { id: 7, name: 'Food', color: '#fff' } }),
      ]);
      component.selectedMonth.set('2025-03');
      expect(component.insightsQuery()).toEqual({ month: '2025-03', side: 'out' });
      expect(component.insightsQuery('Food')).toEqual({
        month: '2025-03',
        side: 'out',
        category: '7',
      });
      expect(component.insightsQuery(CATEGORY_UNKNOWN)).toMatchObject({ category: 'unknown' });
    });
  });

  describe('formatSignedPct', () => {
    it('formats null as an em dash', () => {
      expect(component.formatSignedPct(null)).toBe('—');
    });

    it('prefixes positive values with +', () => {
      expect(component.formatSignedPct(1.55)).toBe('+1.6%');
    });

    it('writes negative values with a true minus sign', () => {
      expect(component.formatSignedPct(-2.34)).toBe('−2.3%');
    });
  });
});
