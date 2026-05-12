import { Injectable, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, tap } from 'rxjs';
import {
  GroceryCategory,
  GroceryCategoryRule,
  GroceryReceiptCategoryMapping,
} from '../models/grocery.model';

@Injectable({ providedIn: 'root' })
export class GroceryCategoriesService {
  private http = inject(HttpClient);

  categories = signal<GroceryCategory[]>([]);
  rules = signal<GroceryCategoryRule[]>([]);

  constructor() {
    this.load();
  }

  load(): void {
    this.http
      .get<GroceryCategory[]>('/api/grocery-categories')
      .subscribe((c) => this.categories.set(c));
    this.http
      .get<GroceryCategoryRule[]>('/api/grocery-categories/rules')
      .subscribe((r) => this.rules.set(r));
  }

  updateCategory(id: number, name: string, color: string): Observable<GroceryCategory> {
    return this.http
      .put<GroceryCategory>(`/api/grocery-categories/${id}`, { name, color })
      .pipe(tap(() => this.load()));
  }

  createCategory(
    name: string,
    color: string,
    pattern?: string,
    value?: number | null,
  ): Observable<GroceryCategory> {
    return this.http
      .post<GroceryCategory>('/api/grocery-categories', {
        name,
        color,
        pattern,
        value: value ?? null,
      })
      .pipe(tap(() => this.load()));
  }

  deleteCategory(id: number): Observable<void> {
    return this.http.delete<void>(`/api/grocery-categories/${id}`).pipe(tap(() => this.load()));
  }

  createRule(
    categoryId: number,
    pattern: string,
    value?: number | null,
  ): Observable<GroceryCategoryRule> {
    return this.http
      .post<GroceryCategoryRule>('/api/grocery-categories/rules', {
        categoryId,
        pattern,
        value: value ?? null,
      })
      .pipe(tap(() => this.load()));
  }

  updateRule(id: number, pattern: string | null, value: number | null): Observable<void> {
    return this.http
      .put<void>(`/api/grocery-categories/rules/${id}`, { pattern, value })
      .pipe(tap(() => this.load()));
  }

  deleteRule(id: number): Observable<void> {
    return this.http
      .delete<void>(`/api/grocery-categories/rules/${id}`)
      .pipe(tap(() => this.load()));
  }

  setItemCategory(
    itemId: number,
    categoryId: number | null,
    deleteRuleId?: number | null,
  ): Observable<void> {
    return this.http.patch<void>(`/api/groceries/items/${itemId}/category`, {
      categoryId,
      deleteRuleId: deleteRuleId ?? null,
    });
  }

  getReceiptMappings(): Observable<GroceryReceiptCategoryMapping[]> {
    return this.http.get<GroceryReceiptCategoryMapping[]>(
      '/api/grocery-categories/receipt-mappings',
    );
  }

  createReceiptMapping(
    receiptCategoryName: string,
    groceryCategoryId: number,
  ): Observable<GroceryReceiptCategoryMapping> {
    return this.http.post<GroceryReceiptCategoryMapping>(
      '/api/grocery-categories/receipt-mappings',
      { receiptCategoryName, groceryCategoryId },
    );
  }

  deleteReceiptMapping(id: number): Observable<void> {
    return this.http.delete<void>(`/api/grocery-categories/receipt-mappings/${id}`);
  }
}
