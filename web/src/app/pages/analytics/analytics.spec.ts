import { describe, it, expect, beforeEach, vi } from 'vitest';
import { TestBed, ComponentFixture } from '@angular/core/testing';
import { computed, signal } from '@angular/core';
import { provideRouter, Router } from '@angular/router';
import { AnalyticsComponent } from './analytics';
import { GroceriesService } from '../../core/services/groceries.service';
import { GroceryCategoriesService } from '../../core/services/grocery-categories.service';
import { EnrichedTransaction, FinanceService } from '../../core/services/finance.service';
import { CategoriesService } from '../../core/services/categories.service';
import { GroceryItem, GroceryCategory } from '../../core/models/grocery.model';
import { CATEGORY_EXCLUDED, CATEGORY_UNKNOWN } from '../../core/constants/categories';
import { MonthTotals } from '../../core/utils/month-totals';

function makeItem(overrides: Partial<GroceryItem> = {}): GroceryItem {
  return {
    id: 1,
    receiptId: 1,
    storeName: 'Continente',
    receiptDate: '2024-01-15',
    description: 'Leite',
    amount: 1.5,
    quantity: 1,
    categoryId: null,
    categoryName: null,
    categoryColor: null,
    categorySetManually: false,
    isExcluded: false,
    ...overrides,
  };
}

let nextTxId = 1;
function makeTx(
  month: string,
  categoryName: string | null,
  amount: number,
  type: 'credit' | 'debit' = 'debit',
): EnrichedTransaction {
  return {
    id: nextTxId++,
    statementId: 1,
    datePosting: `${month}-10`,
    dateValue: `${month}-10`,
    description: `Shop ${nextTxId}`,
    amount,
    type,
    balance: 0,
    categoryId: categoryName ? 1 : null,
    categoryRuleId: null,
    categorySetManually: false,
    isExcluded: false,
    category: categoryName
      ? { id: 1, name: categoryName, color: '#36ab7a', isProtected: false }
      : null,
    bank: 'BPI',
    month,
  };
}

describe('AnalyticsComponent', () => {
  let fixture: ComponentFixture<AnalyticsComponent>;
  let component: AnalyticsComponent;
  let router: Router;

  const allItemsSignal = signal<GroceryItem[]>([]);
  // Mirrors the real service: analytics reads countedItems, never allItems.
  const countedItemsSignal = computed(() =>
    allItemsSignal().filter((i) => !i.isExcluded && i.categoryName !== CATEGORY_EXCLUDED),
  );
  const groceryCatsSignal = signal<GroceryCategory[]>([]);
  // FinanceService.allTransactions: the counted rows, excluded ones already left out.
  const transactionsSignal = signal<EnrichedTransaction[]>([]);
  const monthTotalsSignal = signal<MonthTotals[]>([]);

  beforeEach(() => {
    allItemsSignal.set([]);
    groceryCatsSignal.set([]);
    transactionsSignal.set([]);
    monthTotalsSignal.set([]);

    TestBed.configureTestingModule({
      imports: [AnalyticsComponent],
      providers: [
        provideRouter([]),
        {
          provide: GroceriesService,
          useValue: {
            allItems: allItemsSignal,
            countedItems: countedItemsSignal,
            loading: signal(false),
          },
        },
        { provide: GroceryCategoriesService, useValue: { categories: groceryCatsSignal } },
        {
          provide: FinanceService,
          useValue: {
            allTransactions: transactionsSignal,
            monthTotals: monthTotalsSignal,
            unknownTypeCount: signal(0),
            loading: signal(false),
          },
        },
        { provide: CategoriesService, useValue: { categories: signal([]) } },
      ],
    });

    fixture = TestBed.createComponent(AnalyticsComponent);
    component = fixture.componentInstance;

    vi.spyOn(component as any, 'renderTrendChart').mockImplementation(() => {});
    vi.spyOn(component as any, 'renderCategoryTrendChart').mockImplementation(() => {});
    vi.spyOn(component as any, 'renderGroceryCategoryTrendChart').mockImplementation(() => {});

    fixture.detectChanges();
    router = TestBed.inject(Router);
  });

  it('activeTab defaults to transactions', () => {
    expect(component.activeTab()).toBe('transactions');
  });

  it('activeTab can be switched to groceries', () => {
    component.activeTab.set('groceries');
    expect(component.activeTab()).toBe('groceries');
  });

  it('gAvailableMonths returns unique months sorted descending', () => {
    allItemsSignal.set([
      makeItem({ receiptDate: '2024-01-15' }),
      makeItem({ receiptDate: '2024-03-10' }),
      makeItem({ receiptDate: '2024-01-20' }),
    ]);
    expect(component.gAvailableMonths()).toEqual(['2024-03', '2024-01']);
  });

  it('gAvailableMonths returns empty when no items', () => {
    expect(component.gAvailableMonths()).toEqual([]);
  });

  it('gSpendingData groups items by category and sums amount * quantity', () => {
    component.gFilterMonth.set('2024-01');
    allItemsSignal.set([
      makeItem({ receiptDate: '2024-01-15', categoryName: 'Food', amount: 2.0, quantity: 3 }),
      makeItem({ receiptDate: '2024-01-20', categoryName: 'Food', amount: 1.0, quantity: 1 }),
      makeItem({ receiptDate: '2024-01-10', categoryName: 'Drinks', amount: 4.0, quantity: 2 }),
    ]);
    const data = component.gSpendingData();
    const food = data.find((d) => d.label === 'Food');
    const drinks = data.find((d) => d.label === 'Drinks');
    expect(food?.total).toBeCloseTo(7.0);
    expect(drinks?.total).toBeCloseTo(8.0);
  });

  it('gSpendingData sorts by total descending', () => {
    component.gFilterMonth.set('2024-01');
    allItemsSignal.set([
      makeItem({ receiptDate: '2024-01-15', categoryName: 'Cheap', amount: 1.0, quantity: 1 }),
      makeItem({
        receiptDate: '2024-01-15',
        categoryName: 'Expensive',
        amount: 100.0,
        quantity: 1,
      }),
    ]);
    const labels = component.gSpendingData().map((d) => d.label);
    expect(labels[0]).toBe('Expensive');
  });

  it('gSpendingData uses CATEGORY_UNKNOWN for uncategorised items', () => {
    component.gFilterMonth.set('2024-01');
    allItemsSignal.set([
      makeItem({ receiptDate: '2024-01-15', categoryName: null, amount: 5.0, quantity: 1 }),
    ]);
    expect(component.gSpendingData()[0].label).toBe(CATEGORY_UNKNOWN);
  });

  it('gSpendingData filters to the selected month', () => {
    component.gFilterMonth.set('2024-01');
    allItemsSignal.set([
      makeItem({ receiptDate: '2024-01-15', categoryName: 'Food', amount: 10.0, quantity: 1 }),
      makeItem({ receiptDate: '2024-02-10', categoryName: 'Food', amount: 50.0, quantity: 1 }),
    ]);
    const food = component.gSpendingData().find((d) => d.label === 'Food');
    expect(food?.total).toBeCloseTo(10.0);
  });

  it('gSpendingData shows all months when no filter set', () => {
    component.gFilterMonth.set('');
    allItemsSignal.set([
      makeItem({ receiptDate: '2024-01-15', categoryName: 'Food', amount: 10.0, quantity: 1 }),
      makeItem({ receiptDate: '2024-02-10', categoryName: 'Food', amount: 20.0, quantity: 1 }),
    ]);
    const food = component.gSpendingData().find((d) => d.label === 'Food');
    expect(food?.total).toBeCloseTo(30.0);
  });

  it('gCategoryStats returns null when no category is selected', () => {
    expect(component.gCategoryStats()).toBeNull();
  });

  it('gCategoryStats returns null delta when no month filter is set', () => {
    component.gFilterMonth.set('');
    component.gSelectedCategory.set({ label: 'Food', color: '#000' });
    allItemsSignal.set([
      makeItem({ receiptDate: '2024-01-15', categoryName: 'Food', amount: 10.0, quantity: 1 }),
    ]);
    expect(component.gCategoryStats()?.delta).toBeNull();
  });

  it('gCategoryStats computes avgMonthly across all months for the category', () => {
    component.gSelectedCategory.set({ label: 'Food', color: '#000' });
    allItemsSignal.set([
      makeItem({ receiptDate: '2024-01-15', categoryName: 'Food', amount: 10.0, quantity: 1 }),
      makeItem({ receiptDate: '2024-02-10', categoryName: 'Food', amount: 20.0, quantity: 1 }),
    ]);
    expect(component.gCategoryStats()?.avgMonthly).toBeCloseTo(15.0);
  });

  it('gCategoryStats currentTotal reflects the filtered month', () => {
    component.gFilterMonth.set('2024-02');
    component.gSelectedCategory.set({ label: 'Food', color: '#000' });
    allItemsSignal.set([
      makeItem({ receiptDate: '2024-01-15', categoryName: 'Food', amount: 10.0, quantity: 1 }),
      makeItem({ receiptDate: '2024-02-10', categoryName: 'Food', amount: 30.0, quantity: 1 }),
    ]);
    expect(component.gCategoryStats()?.currentTotal).toBeCloseTo(30.0);
  });

  it('gCategoryStats delta is currentTotal minus previous month total', () => {
    component.gFilterMonth.set('2024-02');
    component.gSelectedCategory.set({ label: 'Food', color: '#000' });
    allItemsSignal.set([
      makeItem({ receiptDate: '2024-01-15', categoryName: 'Food', amount: 10.0, quantity: 1 }),
      makeItem({ receiptDate: '2024-02-10', categoryName: 'Food', amount: 30.0, quantity: 1 }),
    ]);
    expect(component.gCategoryStats()?.delta).toBeCloseTo(20.0);
  });

  it('gCategoryStats delta is negative when spend falls vs previous month', () => {
    component.gFilterMonth.set('2024-02');
    component.gSelectedCategory.set({ label: 'Food', color: '#000' });
    allItemsSignal.set([
      makeItem({ receiptDate: '2024-01-15', categoryName: 'Food', amount: 50.0, quantity: 1 }),
      makeItem({ receiptDate: '2024-02-10', categoryName: 'Food', amount: 20.0, quantity: 1 }),
    ]);
    expect(component.gCategoryStats()?.delta).toBeCloseTo(-30.0);
  });

  it('gCategoryStats ignores items from other categories', () => {
    component.gSelectedCategory.set({ label: 'Food', color: '#000' });
    allItemsSignal.set([
      makeItem({ receiptDate: '2024-01-15', categoryName: 'Food', amount: 10.0, quantity: 1 }),
      makeItem({ receiptDate: '2024-01-15', categoryName: 'Drinks', amount: 100.0, quantity: 1 }),
    ]);
    expect(component.gCategoryStats()?.avgMonthly).toBeCloseTo(10.0);
  });

  it('navigateToGroceryCategory navigates to /transactions with tab=groceries and categoryId from service', () => {
    const spy = vi.spyOn(router, 'navigate');
    groceryCatsSignal.set([{ id: 7, name: 'Food', color: '#fff', isProtected: false, rules: [] }]);
    component.navigateToGroceryCategory('Food');
    expect(spy).toHaveBeenCalledWith(['/transactions'], {
      queryParams: expect.objectContaining({ categoryId: '7', tab: 'groceries' }),
    });
  });

  it('navigateToGroceryCategory uses categoryId=unknown for Unknown label', () => {
    const spy = vi.spyOn(router, 'navigate');
    component.navigateToGroceryCategory(CATEGORY_UNKNOWN);
    expect(spy).toHaveBeenCalledWith(['/transactions'], {
      queryParams: expect.objectContaining({ categoryId: 'unknown', tab: 'groceries' }),
    });
  });

  it('navigateToGroceryCategory includes month param when a month filter is active', () => {
    const spy = vi.spyOn(router, 'navigate');
    component.gFilterMonth.set('2024-01');
    component.navigateToGroceryCategory(CATEGORY_UNKNOWN);
    expect(spy).toHaveBeenCalledWith(['/transactions'], {
      queryParams: expect.objectContaining({ month: '2024-01', tab: 'groceries' }),
    });
  });

  it('navigateToGroceryCategory omits month param when no filter is active', () => {
    const spy = vi.spyOn(router, 'navigate');
    component.gFilterMonth.set('');
    component.navigateToGroceryCategory(CATEGORY_UNKNOWN);
    const call = spy.mock.calls[0];
    expect((call[1] as any).queryParams).not.toHaveProperty('month');
    expect((call[1] as any).queryParams).toHaveProperty('tab', 'groceries');
  });

  it('gSpendingData ignores excluded items', () => {
    allItemsSignal.set([
      makeItem({ id: 1, categoryName: 'Dairy', categoryColor: '#fff', amount: 10, quantity: 1 }),
      makeItem({
        id: 2,
        categoryName: 'Dairy',
        categoryColor: '#fff',
        amount: 5,
        quantity: 1,
        isExcluded: true,
      }),
    ]);
    fixture.detectChanges();

    expect(component.gSpendingData().find((d) => d.label === 'Dairy')?.total).toBe(10);
  });

  it('gSpendingData never shows an Excluded slice', () => {
    allItemsSignal.set([
      makeItem({ id: 1, categoryName: 'Dairy', categoryColor: '#fff', amount: 10, quantity: 1 }),
      makeItem({
        id: 2,
        categoryId: 7,
        categoryName: CATEGORY_EXCLUDED,
        categoryColor: '#64748b',
        amount: 5,
        quantity: 1,
        isExcluded: false,
      }),
    ]);
    fixture.detectChanges();

    expect(component.gSpendingData().map((d) => d.label)).toEqual(['Dairy']);
  });

  it('selectMonth picks the month on both tabs', () => {
    component.selectMonth('2024-01');
    expect(component.filterMonth()).toBe('2024-01');
    expect(component.gFilterMonth()).toBe('2024-01');
    component.selectMonth('');
    expect(component.filterMonth()).toBe('');
    expect(component.gFilterMonth()).toBe('');
  });

  it('months offers months that only have grocery receipts', () => {
    allItemsSignal.set([makeItem({ receiptDate: '2024-03-10' })]);
    expect(component.months().map((m) => m.key)).toEqual(['2024-03']);
  });

  describe('By category', () => {
    beforeEach(() => {
      transactionsSignal.set([
        makeTx('2026-09', 'Rent', 700),
        makeTx('2026-09', 'Food', 50),
        makeTx('2026-08', 'Food', 40),
        makeTx('2026-09', 'Salary', 2000, 'credit'),
        makeTx('2026-09', null, 30),
        makeTx('2026-09', null, 10, 'credit'),
      ]);
      component.selectMonth('2026-09');
    });

    it('lists money in and out together by amount, with no previous month', () => {
      expect(component.categoryBars().rows.map((r) => [r.label, r.side, r.total])).toEqual([
        ['Salary', 'in', 2000],
        ['Rent', 'out', 700],
        ['Food', 'out', 50],
        [CATEGORY_UNKNOWN, 'out', 30],
        [CATEGORY_UNKNOWN, 'in', 10],
      ]);
      expect(component.categoryTitle()).toBe('By category');
      expect(component.categoryBars().rows[0]).not.toHaveProperty('previous');
    });

    it('filters to one side, and the select lists only its categories', () => {
      component.setSide('in');
      expect(component.categoryBars().rows.map((r) => r.label)).toEqual([
        'Salary',
        CATEGORY_UNKNOWN,
      ]);
      expect(component.sideCategories().map((c) => c.label)).toEqual(['Salary', CATEGORY_UNKNOWN]);
      expect(component.categoryTitle()).toBe('Where it came from');

      component.setSide('out');
      expect(component.categoryBars().rows.map((r) => r.label)).toEqual([
        'Rent',
        'Food',
        CATEGORY_UNKNOWN,
      ]);
      expect(component.categoryTitle()).toBe('Where it went');
    });

    it('lets go of a picked category that is not on the chosen side', () => {
      component.selectCategory('Rent', '#fff', 'debit');
      component.setSide('out');
      expect(component.selectedCategory()?.label).toBe('Rent');
      component.setSide('in');
      expect(component.selectedCategory()).toBeNull();
    });

    it('keeps Unknown only on the side it was picked from', () => {
      component.selectCategory(CATEGORY_UNKNOWN, '#000', 'credit');
      expect(component.selectedSide()).toBe('in');
      component.setSide('in');
      expect(component.selectedCategory()?.label).toBe(CATEGORY_UNKNOWN);
      component.setSide('out');
      expect(component.selectedCategory()).toBeNull();
    });

    it('picks a category from the select on the chosen side', () => {
      component.setSide('in');
      component.onCategoryDropdownChange(CATEGORY_UNKNOWN);
      expect(component.selectedCategory()).toMatchObject({
        label: CATEGORY_UNKNOWN,
        dominantType: 'credit',
      });
    });

    it('leads the hero with money in on the In side', () => {
      expect(component.heroLabel()).toBe('Out in September');
      expect(component.heroTotal()).toBe(780);
      component.setSide('in');
      expect(component.heroLabel()).toBe('In in September');
      expect(component.heroTotal()).toBe(2010);
    });
  });

  describe('ranges', () => {
    beforeEach(() => {
      transactionsSignal.set([
        makeTx('2026-07', 'Rent', 700),
        makeTx('2026-08', 'Eating out', 100),
        makeTx('2026-09', 'Eating out', 60, 'credit'),
        makeTx('2026-09', 'Rent', 700),
        makeTx('2026-09', 'Salary', 2000, 'credit'),
      ]);
      component.selectMonth('2026-09');
    });

    it('shows the months up to the selected one, netted over all of them', () => {
      component.rangeMonths.set(3);
      expect(component.periodMonths()).toEqual(['2026-07', '2026-08', '2026-09']);
      expect(component.isRange()).toBe(true);
      expect(component.spendingData().map((d) => [d.label, d.total])).toEqual([
        ['Rent', 1400],
        ['Eating out', 40],
      ]);
      expect(component.heroLabel()).toBe('Out over Jul – Sep 2026');
      expect(component.pageTitle()).toBe('Jul – Sep 2026 insights');
      expect(component.compareMonth()).toBeNull();
    });

    it('starts no earlier than the first month with money', () => {
      component.rangeMonths.set(12);
      expect(component.periodMonths()).toEqual(['2026-07', '2026-08', '2026-09']);
      component.rangeMonths.set('ytd');
      expect(component.periodMonths()?.[0]).toBe('2026-07');
    });

    it('averages over the range’s months', () => {
      component.rangeMonths.set(3);
      component.showAverages.set(true);
      expect(component.isAverages()).toBe(true);
      expect(component.totalSpending()).toBeCloseTo(480);
      expect(component.periodLabel()).toBe('Average month');
      expect(component.spendingNote()).toBe('Averaged over 3 months.');
    });

    it('sums a picked category over the range, with no previous month', () => {
      component.rangeMonths.set(3);
      component.selectCategory('Rent', '#fff', 'debit');
      expect(component.categoryStats()).toMatchObject({ currentTotal: -1400, delta: null });
    });

    it('shows at least six months of flow, and every month of a longer range', () => {
      const months = Array.from({ length: 14 }, (_, i) => {
        const d = new Date(Date.UTC(2025, 7 + i, 1));
        return d.toISOString().slice(0, 7);
      });
      monthTotalsSignal.set(
        [...months].reverse().map((month) => ({ month, income: 1, expenses: 1, net: 0 })),
      );
      component.rangeMonths.set(3);
      expect(component.flowMonths().length).toBe(6);
      // A range starts at the first month with transactions.
      transactionsSignal.update((rows) => [...rows, makeTx('2025-08', 'Rent', 700)]);
      component.rangeMonths.set(12);
      expect(component.flowMonths().length).toBe(12);
    });

    it('links a category to every month of Activity, which shows one month', () => {
      const spy = vi.spyOn(router, 'navigate');
      component.rangeMonths.set(3);
      component.navigateToCategory(CATEGORY_UNKNOWN, 'debit');
      expect(spy).toHaveBeenLastCalledWith(['/transactions'], {
        queryParams: { category: 'unknown', type: 'debit' },
      });
    });

    it('takes the range on the Groceries tab too', () => {
      allItemsSignal.set([
        makeItem({ receiptId: 1, receiptDate: '2026-08-10', categoryName: 'Food', amount: 20 }),
        makeItem({ receiptId: 2, receiptDate: '2026-09-10', categoryName: 'Food', amount: 30 }),
      ]);
      component.selectMonth('2026-09');
      component.rangeMonths.set(3);
      expect(component.gTotalSpending()).toBeCloseTo(50);
      expect(component.gNote()).toBe('Over Aug – Sep 2026, read from 2 receipts.');
    });
  });

  describe('URL', () => {
    async function openAt(url: string) {
      await router.navigateByUrl(url);
      const fx = TestBed.createComponent(AnalyticsComponent);
      const page = fx.componentInstance;
      vi.spyOn(page as any, 'renderTrendChart').mockImplementation(() => {});
      vi.spyOn(page as any, 'renderCategoryTrendChart').mockImplementation(() => {});
      vi.spyOn(page as any, 'renderGroceryCategoryTrendChart').mockImplementation(() => {});
      fx.detectChanges();
      await fx.whenStable();
      return { fx, page };
    }

    beforeEach(() => {
      (TestBed.inject(CategoriesService).categories as any).set([
        { id: 5, name: 'Rent', color: '#fff', isProtected: false },
      ]);
      transactionsSignal.set([
        makeTx('2026-08', 'Rent', 700),
        makeTx('2026-09', 'Rent', 700),
        makeTx('2026-09', null, 10, 'credit'),
      ]);
    });

    it('opens on the month, side and category it names', async () => {
      const { page } = await openAt('/?month=2026-08&side=out&category=5');
      expect(page.filterMonth()).toBe('2026-08');
      expect(page.side()).toBe('out');
      expect(page.selectedCategory()).toMatchObject({ label: 'Rent', dominantType: 'debit' });
    });

    it('picks Unknown on the side it names', async () => {
      const { page } = await openAt('/?month=2026-09&side=in&category=unknown');
      expect(page.selectedCategory()).toMatchObject({
        label: CATEGORY_UNKNOWN,
        dominantType: 'credit',
      });
    });

    it('opens all months, and ignores a category it does not know', async () => {
      const { page } = await openAt('/?month=all&category=99');
      expect(page.filterMonth()).toBe('');
      expect(page.selectedCategory()).toBeNull();
    });

    it('opens on the range it names', async () => {
      const { fx, page } = await openAt('/?month=2026-09&months=6');
      expect(page.rangeMonths()).toBe(6);
      expect(page.periodMonths()).toEqual(['2026-08', '2026-09']);
      page.rangeMonths.set('ytd');
      fx.detectChanges();
      await fx.whenStable();
      expect(router.url).toBe('/?month=2026-09&months=ytd');
    });

    it('puts a change on the page in the URL', async () => {
      const { fx, page } = await openAt('/?month=2026-09');
      page.setSide('in');
      page.selectCategory(CATEGORY_UNKNOWN, '#000', 'credit');
      fx.detectChanges();
      await fx.whenStable();
      expect(router.url).toBe('/?month=2026-09&side=in&category=unknown');
    });
  });

  describe('category netting (ADR-037)', () => {
    /** A dinner of 100 and three friends paying back 25 each. */
    const dinner = (month: string) => [
      makeTx(month, 'Eating out', 100),
      makeTx(month, 'Eating out', 25, 'credit'),
      makeTx(month, 'Eating out', 25, 'credit'),
      makeTx(month, 'Eating out', 25, 'credit'),
    ];

    it('nets a dinner paid back by friends to what it cost, with nothing as income', () => {
      transactionsSignal.set([...dinner('2026-09'), makeTx('2026-09', 'Salary', 2000, 'credit')]);
      component.selectMonth('2026-09');

      expect(component.spendingData().map((d) => [d.label, d.total])).toEqual([['Eating out', 25]]);
      expect(component.incomeData().map((d) => [d.label, d.total])).toEqual([['Salary', 2000]]);
      expect(component.totalSpending()).toBe(25);
      expect(component.totalIncome()).toBe(2000);
      expect(component.kept()).toBe(1975);
    });

    it('counts a category as income when more came back than went out', () => {
      transactionsSignal.set([
        makeTx('2026-09', 'Gifts', 40),
        makeTx('2026-09', 'Gifts', 100, 'credit'),
        makeTx('2026-09', 'Rent', 700),
      ]);
      component.selectMonth('2026-09');

      component.setSide('out');
      expect(component.categoryBars().rows.map((r) => r.label)).toEqual(['Rent']);
      component.setSide('in');
      expect(component.categoryBars().rows).toEqual([
        expect.objectContaining({ label: 'Gifts', total: 60, share: 100, side: 'in' }),
      ]);
    });

    it('shows a category paid back in full at zero, last of the spending bars only', () => {
      transactionsSignal.set([
        makeTx('2026-09', 'Gifts', 60),
        makeTx('2026-09', 'Gifts', 60, 'credit'),
        makeTx('2026-09', 'Rent', 700),
      ]);
      component.selectMonth('2026-09');

      const rows = component.categoryBars().rows;
      expect(rows.map((r) => [r.label, r.total])).toEqual([
        ['Rent', 700],
        ['Gifts', 0],
      ]);
      expect(rows[1].share).toBe(0);
      expect(component.incomeData()).toEqual([]);
      expect(component.totalSpending()).toBe(700);
    });

    it('keeps rows without a category gross', () => {
      transactionsSignal.set([makeTx('2026-09', null, 30), makeTx('2026-09', null, 10, 'credit')]);
      component.selectMonth('2026-09');

      expect(component.spendingData()).toEqual([
        expect.objectContaining({ label: CATEGORY_UNKNOWN, total: 30 }),
      ]);
      expect(component.incomeData()).toEqual([
        expect.objectContaining({ label: CATEGORY_UNKNOWN, total: 10 }),
      ]);
    });

    it('nets over the whole range and divides the nets for an average month', () => {
      // August's payback arrives in September: it nets only with all months shown.
      transactionsSignal.set([
        makeTx('2026-08', 'Eating out', 100),
        makeTx('2026-09', 'Eating out', 60, 'credit'),
        makeTx('2026-09', 'Eating out', 20),
      ]);
      component.selectMonth('2026-09');
      expect(component.incomeData().map((d) => [d.label, d.total])).toEqual([['Eating out', 40]]);

      component.selectMonth('');
      expect(component.totalSpending()).toBe(60);
      component.showAverages.set(true);
      expect(component.totalSpending()).toBe(30);
      expect(component.spendingData()[0].total).toBe(30);
    });

    it('trends and sums up the picked category by its net each month', () => {
      transactionsSignal.set([
        ...dinner('2026-09'),
        makeTx('2026-08', 'Eating out', 40),
        makeTx('2026-08', 'Eating out', 10, 'credit'),
      ]);
      component.selectMonth('2026-09');
      component.selectCategory('Eating out', '#36ab7a', 'debit');

      expect(component.categoryTrendData()).toEqual([
        { month: '2026-08', net: -30, received: 10, spent: 40 },
        { month: '2026-09', net: -25, received: 75, spent: 100 },
      ]);
      expect(component.trendIsGross()).toBe(false);
      expect(component.categoryStats()).toEqual({ avgMonthly: -27.5, currentTotal: -25, delta: 5 });
    });

    it('sums up Unknown by the side it was picked from', () => {
      transactionsSignal.set([
        makeTx('2026-09', null, 30),
        makeTx('2026-09', null, 10, 'credit'),
        makeTx('2026-08', null, 50),
      ]);
      component.selectMonth('2026-09');
      component.selectCategory(CATEGORY_UNKNOWN, '#000', 'debit');

      expect(component.trendIsGross()).toBe(true);
      expect(component.categoryStats()).toEqual({ avgMonthly: -40, currentTotal: -30, delta: 20 });
    });

    it('links a category to all its rows, and Unknown to the side picked', () => {
      const spy = vi.spyOn(router, 'navigate');
      (TestBed.inject(CategoriesService).categories as any).set([
        { id: 5, name: 'Eating out', color: '#fff', isProtected: false },
      ]);
      component.selectMonth('2026-09');

      component.navigateToCategory('Eating out', 'debit');
      expect(spy).toHaveBeenLastCalledWith(['/transactions'], {
        queryParams: { category: '5', month: '2026-09' },
      });

      component.navigateToCategory(CATEGORY_UNKNOWN, 'credit');
      expect(spy).toHaveBeenLastCalledWith(['/transactions'], {
        queryParams: { category: 'unknown', month: '2026-09', type: 'credit' },
      });
    });
  });

  it('the monthly average divides all-month totals by the number of months', () => {
    transactionsSignal.set([makeTx('2026-09', 'Food', 50), makeTx('2026-08', 'Food', 40)]);
    component.selectMonth('');
    component.showAverages.set(true);
    expect(component.isAverages()).toBe(true);
    expect(component.totalSpending()).toBeCloseTo(45);
    component.selectMonth('2026-09');
    expect(component.isAverages()).toBe(false);
    expect(component.totalSpending()).toBeCloseTo(50);
  });

  it('flowMonths shows six months up to the selected one', () => {
    const months = ['2026-02', '2026-03', '2026-04', '2026-05', '2026-06', '2026-07', '2026-08'];
    monthTotalsSignal.set(
      [...months].reverse().map((month) => ({ month, income: 100, expenses: 50, net: 50 })),
    );
    component.selectMonth('2026-07');
    expect(component.flowMonths().map((m) => m.month)).toEqual(months.slice(0, 6));
    expect(component.flowTitle()).toBe('Six months of flow');
  });

  it('gSpendingBars lists grocery categories without comparing months', () => {
    allItemsSignal.set([
      makeItem({ receiptDate: '2024-02-10', categoryName: 'Food', amount: 30, quantity: 1 }),
      makeItem({ receiptDate: '2024-01-10', categoryName: 'Food', amount: 10, quantity: 2 }),
    ]);
    component.gFilterMonth.set('2024-02');
    const [food] = component.gSpendingBars().rows;
    expect(food.total).toBeCloseTo(30);
    expect(food.share).toBe(100);
    expect(food.side).toBeUndefined();
    expect(food).not.toHaveProperty('previous');
  });
});
