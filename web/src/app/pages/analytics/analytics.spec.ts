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

  beforeEach(() => {
    allItemsSignal.set([]);
    groceryCatsSignal.set([]);
    transactionsSignal.set([]);

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
            monthlySummaries: signal([]),
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

  it('spendingBars compares each category with the previous month', () => {
    transactionsSignal.set([
      makeTx('2026-09', 'Rent', 700),
      makeTx('2026-09', 'Food', 50),
      makeTx('2026-08', 'Food', 40),
      makeTx('2026-09', 'Salary', 2000, 'credit'),
    ]);
    component.selectMonth('2026-09');
    const rows = component.spendingBars().rows;
    expect(rows.map((r) => r.label)).toEqual(['Rent', 'Food']);
    expect(rows[1].previous).toBe(40);
    expect(rows[1].change).toBeCloseTo(10);
    expect(rows[0].previous).toBe(0);
    expect(component.spendingCompared()).toBe(true);
    expect(component.compareName()).toBe('August');
  });

  it('spendingBars compares nothing with all months selected', () => {
    transactionsSignal.set([makeTx('2026-09', 'Food', 50), makeTx('2026-08', 'Food', 40)]);
    component.selectMonth('');
    expect(component.spendingBars().rows[0].total).toBeCloseTo(90);
    expect(component.spendingBars().rows[0].previous).toBeNull();
    expect(component.spendingCompared()).toBe(false);
    expect(component.moves()).toEqual([]);
  });

  it('moves lists the categories that changed most since the previous month', () => {
    transactionsSignal.set([
      makeTx('2026-09', 'Rent', 700),
      makeTx('2026-08', 'Rent', 700),
      makeTx('2026-09', 'Food', 50),
      makeTx('2026-08', 'Food', 140),
      makeTx('2026-09', 'Health', 30),
    ]);
    component.selectMonth('2026-09');
    expect(component.moves().map((m) => [m.label, m.change])).toEqual([
      ['Food', -90],
      ['Health', 30],
    ]);
  });

  it('moves is empty when the previous month had no spending', () => {
    transactionsSignal.set([makeTx('2026-09', 'Food', 50)]);
    component.selectMonth('2026-09');
    expect(component.moves()).toEqual([]);
    expect(component.spendingCompared()).toBe(false);
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
    (TestBed.inject(FinanceService).monthlySummaries as any).set(
      months.map((month) => ({
        month,
        bank: 'BPI',
        income: 100,
        expenses: 50,
        net: 50,
        closingBalance: 0,
      })),
    );
    component.selectMonth('2026-07');
    expect(component.flowMonths().map((m) => m.month)).toEqual(months.slice(0, 6));
    expect(component.flowTitle()).toBe('Six months of flow');
  });

  it('gSpendingBars compares grocery categories with the previous month', () => {
    allItemsSignal.set([
      makeItem({ receiptDate: '2024-02-10', categoryName: 'Food', amount: 30, quantity: 1 }),
      makeItem({ receiptDate: '2024-01-10', categoryName: 'Food', amount: 10, quantity: 2 }),
    ]);
    component.gFilterMonth.set('2024-02');
    const food = component.gSpendingBars().rows[0];
    expect(food.total).toBeCloseTo(30);
    expect(food.previous).toBeCloseTo(20);
    expect(component.gCompared()).toBe(true);
  });
});
