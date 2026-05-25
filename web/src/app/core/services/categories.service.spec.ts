import { describe, it, expect, beforeEach } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { CategoriesService } from './categories.service';
import { Category, CategoryRule } from '../models/statement.model';

const CAT_FOOD: Category = { id: 1, name: 'Food', color: '#ff0000', isProtected: false, rules: [] };
const CAT_TRAVEL: Category = {
  id: 2,
  name: 'Travel',
  color: '#0000ff',
  isProtected: false,
  rules: [],
};
const RULE_LIDL: CategoryRule = { id: 10, categoryId: 1, pattern: 'LIDL' };

describe('CategoriesService', () => {
  let service: CategoriesService;
  let controller: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [CategoriesService, provideHttpClient(), provideHttpClientTesting()],
    });

    controller = TestBed.inject(HttpTestingController);
    service = TestBed.inject(CategoriesService);

    flushLoad(controller, [CAT_FOOD], [RULE_LIDL]);
  });

  function flushLoad(
    ctrl: HttpTestingController,
    cats: Category[] = [],
    rules: CategoryRule[] = [],
  ) {
    ctrl.expectOne('/api/categories').flush(cats);
    ctrl.expectOne('/api/categories/rules').flush(rules);
  }

  it('initialises categories signal from GET /api/categories', () => {
    expect(service.categories()).toEqual([CAT_FOOD]);
  });

  it('initialises rules signal from GET /api/categories/rules', () => {
    expect(service.rules()).toEqual([RULE_LIDL]);
  });

  it('load() refreshes categories and rules', () => {
    service.load();
    flushLoad(controller, [CAT_FOOD, CAT_TRAVEL], [RULE_LIDL]);

    expect(service.categories().length).toBe(2);
  });

  it('createCategory() POSTs to /api/categories', () => {
    service.createCategory('Transport', '#00ff00').subscribe();

    const req = controller.expectOne('/api/categories');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({
      name: 'Transport',
      color: '#00ff00',
      pattern: undefined,
      value: null,
    });
    req.flush({ id: 3, name: 'Transport', color: '#00ff00', rules: [] });

    flushLoad(controller);
  });

  it('createCategory() with pattern includes pattern in body', () => {
    service.createCategory('Fuel', '#aaaaaa', 'BP GAS').subscribe();

    const req = controller.expectOne('/api/categories');
    expect(req.request.body.pattern).toBe('BP GAS');
    req.flush({ id: 4, name: 'Fuel', color: '#aaaaaa', rules: [] });

    flushLoad(controller);
  });

  it('createCategory() reloads categories after success', () => {
    let reloaded = false;
    service.createCategory('X', '#000').subscribe(() => {
      reloaded = true;
    });

    controller.expectOne('/api/categories').flush({ id: 99, name: 'X', color: '#000', rules: [] });
    flushLoad(
      controller,
      [CAT_FOOD, { id: 99, name: 'X', color: '#000', isProtected: false, rules: [] }],
      [],
    );

    expect(service.categories().length).toBe(2);
  });

  it('updateCategory() PUTs to /api/categories/:id', () => {
    service.updateCategory(1, 'Food & Drink', '#ff5500').subscribe();

    const req = controller.expectOne('/api/categories/1');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ name: 'Food & Drink', color: '#ff5500' });
    req.flush({ ...CAT_FOOD, name: 'Food & Drink', color: '#ff5500' });

    flushLoad(controller);
  });

  it('updateCategory() reloads after success', () => {
    service.updateCategory(1, 'Updated', '#ffffff').subscribe();

    controller
      .expectOne('/api/categories/1')
      .flush({ id: 1, name: 'Updated', color: '#ffffff', rules: [] });
    flushLoad(
      controller,
      [{ id: 1, name: 'Updated', color: '#ffffff', isProtected: false, rules: [] }],
      [],
    );

    expect(service.categories()[0].name).toBe('Updated');
  });

  it('deleteCategory() sends DELETE to /api/categories/:id', () => {
    service.deleteCategory(1).subscribe();

    const req = controller.expectOne('/api/categories/1');
    expect(req.request.method).toBe('DELETE');
    req.flush(null);

    flushLoad(controller, [], []);
  });

  it('deleteCategory() reloads after success', () => {
    service.deleteCategory(1).subscribe();

    controller.expectOne('/api/categories/1').flush(null);
    flushLoad(controller, [], []);

    expect(service.categories()).toEqual([]);
  });

  it('createRule() POSTs to /api/categories/rules with pattern only', () => {
    service.createRule(1, 'CONTINENTE').subscribe();

    const req = controller.expectOne('/api/categories/rules');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ categoryId: 1, pattern: 'CONTINENTE', value: null });
    req.flush({ id: 20, categoryId: 1, pattern: 'CONTINENTE', value: null });

    flushLoad(controller);
  });

  it('createRule() POSTs value when provided', () => {
    service.createRule(1, '', 1500).subscribe();

    const req = controller.expectOne('/api/categories/rules');
    expect(req.request.body).toEqual({ categoryId: 1, pattern: '', value: 1500 });
    req.flush({ id: 22, categoryId: 1, pattern: '', value: 1500 });

    flushLoad(controller);
  });

  it('createRule() POSTs both pattern and value when both provided', () => {
    service.createRule(1, 'LIDL', 30).subscribe();

    const req = controller.expectOne('/api/categories/rules');
    expect(req.request.body).toEqual({ categoryId: 1, pattern: 'LIDL', value: 30 });
    req.flush({ id: 23, categoryId: 1, pattern: 'LIDL', value: 30 });

    flushLoad(controller);
  });

  it('createRule() reloads rules after success', () => {
    service.createRule(1, 'ZARA').subscribe();

    controller
      .expectOne('/api/categories/rules')
      .flush({ id: 21, categoryId: 1, pattern: 'ZARA', value: null });
    flushLoad(controller, [CAT_FOOD], [RULE_LIDL, { id: 21, categoryId: 1, pattern: 'ZARA' }]);

    expect(service.rules().length).toBe(2);
  });

  it('deleteRule() sends DELETE to /api/categories/rules/:id', () => {
    service.deleteRule(10).subscribe();

    const req = controller.expectOne('/api/categories/rules/10');
    expect(req.request.method).toBe('DELETE');
    req.flush(null);

    flushLoad(controller);
  });

  it('deleteRule() reloads rules after success', () => {
    service.deleteRule(10).subscribe();

    controller.expectOne('/api/categories/rules/10').flush(null);
    flushLoad(controller, [CAT_FOOD], []);

    expect(service.rules()).toEqual([]);
  });

  it('setTransactionCategory() PATCHes /api/transactions/:id/category', () => {
    service.setTransactionCategory(42, 1).subscribe();

    const req = controller.expectOne('/api/transactions/42/category');
    expect(req.request.method).toBe('PATCH');
    expect(req.request.body).toEqual({ categoryId: 1, deleteRuleId: null });
    req.flush(null);
  });

  it('setTransactionCategory() sends deleteRuleId when provided', () => {
    service.setTransactionCategory(42, 2, 10).subscribe();

    const req = controller.expectOne('/api/transactions/42/category');
    expect(req.request.body).toEqual({ categoryId: 2, deleteRuleId: 10 });
    req.flush(null);
  });

  it('setTransactionCategory() sends null categoryId to unset', () => {
    service.setTransactionCategory(42, null).subscribe();

    const req = controller.expectOne('/api/transactions/42/category');
    expect(req.request.body.categoryId).toBeNull();
    req.flush(null);
  });

  it('setTransactionCategory() does not trigger a reload', () => {
    service.setTransactionCategory(5, 1).subscribe();

    controller.expectOne('/api/transactions/5/category').flush(null);

    controller.verify();
  });
});
