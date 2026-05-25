import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { GroceriesService } from './groceries.service';
import {
  GroceryItem,
  GroceryReceiptSummary,
  PagedGroceryItemsResult,
} from '../models/grocery.model';

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
    ...overrides,
  };
}

function makeReceipt(overrides: Partial<GroceryReceiptSummary> = {}): GroceryReceiptSummary {
  return {
    id: 1,
    storeName: 'Continente',
    receiptDate: '2024-01-15',
    total: 25.5,
    itemCount: 10,
    sourceFile: null,
    ...overrides,
  };
}

function makePagedResult(items: GroceryItem[]): PagedGroceryItemsResult {
  return {
    items,
    totalCount: items.length,
    totalAmount: items.reduce((s, i) => s + i.amount, 0),
  };
}

describe('GroceriesService', () => {
  let service: GroceriesService;
  let controller: HttpTestingController;

  function flushInit(receipts: GroceryReceiptSummary[] = [], items: GroceryItem[] = []): void {
    controller.expectOne('/api/groceries/receipts').flush(receipts);
    const req = controller.expectOne((r) => r.url === '/api/groceries/items');
    expect(req.request.params.get('take')).toBe('5000');
    req.flush(makePagedResult(items));
  }

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [GroceriesService, provideHttpClient(), provideHttpClientTesting()],
    });
    controller = TestBed.inject(HttpTestingController);
    service = TestBed.inject(GroceriesService);
    flushInit();
  });

  afterEach(() => controller.verify());

  it('starts with empty receipts', () => {
    expect(service.receipts()).toEqual([]);
  });

  it('starts with empty allItems', () => {
    expect(service.allItems()).toEqual([]);
  });

  it('starts with loading=false after init', () => {
    expect(service.loading()).toBe(false);
  });

  it('starts with no error', () => {
    expect(service.error()).toBeNull();
  });

  it('loadAllItems() requests take=5000', () => {
    service.loadAllItems();
    const req = controller.expectOne((r) => r.url === '/api/groceries/items');
    expect(req.request.params.get('take')).toBe('5000');
    req.flush(makePagedResult([]));
  });

  it('loadAllItems() populates allItems signal', () => {
    const item = makeItem({ id: 10, description: 'Pão' });
    service.loadAllItems();
    controller.expectOne((r) => r.url === '/api/groceries/items').flush(makePagedResult([item]));
    expect(service.allItems()).toHaveLength(1);
    expect(service.allItems()[0].description).toBe('Pão');
  });

  it('loadAllItems() sets error signal on failure', () => {
    service.loadAllItems();
    controller.expectOne((r) => r.url === '/api/groceries/items').error(new ProgressEvent('error'));
    expect(service.error()).toBe('Failed to load grocery items. Is the API running?');
  });

  it('reload() repopulates receipts signal', () => {
    const receipt = makeReceipt({ id: 5, storeName: 'Pingo Doce' });
    service.reload();
    controller.expectOne('/api/groceries/receipts').flush([receipt]);
    expect(service.receipts()).toHaveLength(1);
    expect(service.receipts()[0].storeName).toBe('Pingo Doce');
  });

  it('reload() sets loading=true before response', () => {
    service.reload();
    expect(service.loading()).toBe(true);
    controller.expectOne('/api/groceries/receipts').flush([]);
  });

  it('reload() sets loading=false after response', () => {
    service.reload();
    controller.expectOne('/api/groceries/receipts').flush([]);
    expect(service.loading()).toBe(false);
  });

  it('reload() sets error signal on failure', () => {
    service.reload();
    controller.expectOne('/api/groceries/receipts').error(new ProgressEvent('error'));
    expect(service.error()).toBe('Failed to load grocery receipts. Is the API running?');
    expect(service.loading()).toBe(false);
  });

  it('stores() returns sorted unique store names', () => {
    service.reload();
    controller
      .expectOne('/api/groceries/receipts')
      .flush([
        makeReceipt({ id: 1, storeName: 'Pingo Doce' }),
        makeReceipt({ id: 2, storeName: 'Continente' }),
        makeReceipt({ id: 3, storeName: 'Pingo Doce' }),
      ]);
    expect(service.stores()).toEqual(['Continente', 'Pingo Doce']);
  });

  it('stores() returns empty array when no receipts', () => {
    expect(service.stores()).toEqual([]);
  });

  it('monthlySummaries() groups items by month and store', () => {
    service.loadAllItems();
    controller
      .expectOne((r) => r.url === '/api/groceries/items')
      .flush(
        makePagedResult([
          makeItem({ id: 1, storeName: 'Continente', receiptDate: '2024-01-15', amount: 10 }),
          makeItem({ id: 2, storeName: 'Continente', receiptDate: '2024-01-20', amount: 5 }),
          makeItem({ id: 3, storeName: 'Pingo Doce', receiptDate: '2024-01-10', amount: 8 }),
        ]),
      );
    const summaries = service.monthlySummaries();
    const continente = summaries.find((s) => s.store === 'Continente' && s.month === '2024-01');
    const pingodoce = summaries.find((s) => s.store === 'Pingo Doce' && s.month === '2024-01');
    expect(continente?.total).toBe(15);
    expect(pingodoce?.total).toBe(8);
  });

  it('monthlySummaries() returns entries sorted by month then store', () => {
    service.loadAllItems();
    controller
      .expectOne((r) => r.url === '/api/groceries/items')
      .flush(
        makePagedResult([
          makeItem({ id: 1, storeName: 'Continente', receiptDate: '2024-02-01', amount: 1 }),
          makeItem({ id: 2, storeName: 'Continente', receiptDate: '2024-01-01', amount: 1 }),
        ]),
      );
    const months = service.monthlySummaries().map((s) => s.month);
    expect(months).toEqual(['2024-01', '2024-02']);
  });
});
