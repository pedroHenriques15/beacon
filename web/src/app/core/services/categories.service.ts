import { Injectable, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, tap } from 'rxjs';
import { Category, CategoryRule } from '../models/statement.model';

@Injectable({ providedIn: 'root' })
export class CategoriesService {
  private http = inject(HttpClient);

  categories = signal<Category[]>([]);
  rules = signal<CategoryRule[]>([]);

  constructor() {
    this.load();
  }

  load(): void {
    this.http.get<Category[]>('/api/categories').subscribe((c) => this.categories.set(c));
    this.http.get<CategoryRule[]>('/api/categories/rules').subscribe((r) => this.rules.set(r));
  }

  updateCategory(id: number, name: string, color: string): Observable<Category> {
    return this.http
      .put<Category>(`/api/categories/${id}`, { name, color })
      .pipe(tap(() => this.load()));
  }

  createCategory(
    name: string,
    color: string,
    pattern?: string,
    value?: number | null,
  ): Observable<Category> {
    return this.http
      .post<Category>('/api/categories', { name, color, pattern, value: value ?? null })
      .pipe(tap(() => this.load()));
  }

  deleteCategory(id: number): Observable<void> {
    return this.http.delete<void>(`/api/categories/${id}`).pipe(tap(() => this.load()));
  }

  createRule(categoryId: number, pattern: string, value?: number | null): Observable<CategoryRule> {
    return this.http
      .post<CategoryRule>('/api/categories/rules', { categoryId, pattern, value: value ?? null })
      .pipe(tap(() => this.load()));
  }

  updateRule(id: number, pattern: string | null, value: number | null): Observable<void> {
    return this.http
      .put<void>(`/api/categories/rules/${id}`, { pattern, value })
      .pipe(tap(() => this.load()));
  }

  deleteRule(id: number): Observable<void> {
    return this.http.delete<void>(`/api/categories/rules/${id}`).pipe(tap(() => this.load()));
  }

  setTransactionCategory(
    txId: number,
    categoryId: number | null,
    deleteRuleId?: number,
  ): Observable<unknown> {
    return this.http.patch(`/api/transactions/${txId}/category`, {
      categoryId,
      deleteRuleId: deleteRuleId ?? null,
    });
  }
}
