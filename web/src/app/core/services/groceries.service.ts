import { Injectable, inject, signal, computed } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { buildParams } from '../utils/http-params';
import { Observable } from 'rxjs';
import {
  GroceryItem,
  GroceryMonthlySummary,
  GroceryReceiptSummary,
  GroceryReceiptUploadResult,
  PagedGroceryItemsResult,
} from '../models/grocery.model';

@Injectable({ providedIn: 'root' })
export class GroceriesService {
  private http = inject(HttpClient);

  receipts = signal<GroceryReceiptSummary[]>([]);
  loading = signal(false);
  error = signal<string | null>(null);
  allItems = signal<GroceryItem[]>([]);

  constructor() {
    this.reload();
    this.loadAllItems();
  }

  reload(): void {
    this.loading.set(true);
    this.error.set(null);
    this.http.get<GroceryReceiptSummary[]>('/api/groceries/receipts').subscribe({
      next: (r) => {
        this.receipts.set(r);
        this.loading.set(false);
      },
      error: (err) => {
        this.error.set('Failed to load grocery receipts. Is the API running?');
        this.loading.set(false);
        console.error(err);
      },
    });
  }

  loadAllItems(): void {
    this.http
      .get<PagedGroceryItemsResult>('/api/groceries/items', {
        params: buildParams({ take: 5000 }),
      })
      .subscribe({
        next: (res) => this.allItems.set(res.items),
        error: (err) => {
          this.error.set('Failed to load grocery items. Is the API running?');
          console.error(err);
        },
      });
  }

  stores = computed(() => [...new Set(this.receipts().map((r) => r.storeName))].sort());

  monthlySummaries = computed<GroceryMonthlySummary[]>(() => {
    const map = new Map<string, number>();
    for (const item of this.allItems()) {
      const month = item.receiptDate.slice(0, 7);
      const key = `${month}__${item.storeName}`;
      map.set(key, (map.get(key) ?? 0) + item.amount);
    }
    const result: GroceryMonthlySummary[] = [];
    for (const [key, total] of map.entries()) {
      const [month, store] = key.split('__');
      result.push({ month, store, total });
    }
    return result.sort((a, b) => a.month.localeCompare(b.month) || a.store.localeCompare(b.store));
  });

  upload(file: File): Observable<GroceryReceiptUploadResult> {
    const form = new FormData();
    form.append('file', file);
    return this.http.post<GroceryReceiptUploadResult>('/api/groceries/receipts/upload', form);
  }

  deleteReceipt(id: number): Observable<void> {
    return this.http.delete<void>(`/api/groceries/receipts/${id}`);
  }

  getItems(params: Record<string, string | number>): Observable<PagedGroceryItemsResult> {
    return this.http.get<PagedGroceryItemsResult>('/api/groceries/items', {
      params: buildParams(params),
    });
  }

  createItem(body: {
    receiptId: number;
    description: string;
    amount: number;
    quantity: number;
  }): Observable<GroceryItem> {
    return this.http.post<GroceryItem>('/api/groceries/items', body);
  }

  updateItem(
    id: number,
    body: { description: string; amount: number; quantity: number },
  ): Observable<void> {
    return this.http.put<void>(`/api/groceries/items/${id}`, body);
  }

  deleteItem(id: number): Observable<void> {
    return this.http.delete<void>(`/api/groceries/items/${id}`);
  }

  markItemsExcluded(itemIds: number[], unmark = false): Observable<unknown> {
    return this.http.patch('/api/groceries/items/mark-excluded', { itemIds, unmark });
  }
}
