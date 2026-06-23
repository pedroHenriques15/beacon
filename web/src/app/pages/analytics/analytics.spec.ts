import { describe, it, expect, beforeEach, vi } from 'vitest';
import { TestBed, ComponentFixture } from '@angular/core/testing';
import { signal } from '@angular/core';
import { provideRouter, Router } from '@angular/router';
import { AnalyticsComponent } from './analytics';
import { GroceriesService } from '../../core/services/groceries.service';
import { GroceryCategoriesService } from '../../core/services/grocery-categories.service';
import { FinanceService } from '../../core/services/finance.service';
import { CategoriesService } from '../../core/services/categories.service';
import { GroceryItem, GroceryCategory } from '../../core/models/grocery.model';
import { CATEGORY_UNKNOWN } from '../../core/constants/categories';

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

describe('AnalyticsComponent', () => {
  let fixture: ComponentFixture<AnalyticsComponent>;
  let component: AnalyticsComponent;
  let router: Router;

  const allItemsSignal = signal<GroceryItem[]>([]);
  const groceryCatsSignal = signal<GroceryCategory[]>([]);

  beforeEach(() => {
    allItemsSignal.set([]);
    groceryCatsSignal.set([]);

    TestBed.configureTestingModule({
      imports: [AnalyticsComponent],
      providers: [
        provideRouter([]),
        {
          provide: GroceriesService,
          useValue: { allItems: allItemsSignal, loading: signal(false) },
        },
        { provide: GroceryCategoriesService, useValue: { categories: groceryCatsSignal } },
        {
          provide: FinanceService,
          useValue: {
            allTransactions: signal([]),
            monthlySummaries: signal([]),
            loading: signal(false),
          },
        },
        { provide: CategoriesService, useValue: { categories: signal([]) } },
      ],
    });

    fixture = TestBed.createComponent(AnalyticsComponent);
    component = fixture.componentInstance;

    vi.spyOn(component as any, 'renderChart').mockImplementation(() => {});
    vi.spyOn(component as any, 'renderGrocerySpendingChart').mockImplementation(() => {});
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
});
