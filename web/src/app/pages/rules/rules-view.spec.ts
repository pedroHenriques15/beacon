import { describe, it, expect } from 'vitest';
import {
  filterByName,
  groupByCategory,
  plural,
  ruleAmount,
  ruleCountLabel,
  ruleSummary,
} from './rules-view';

describe('filterByName', () => {
  const categories = [{ name: 'Groceries' }, { name: 'Dining out' }, { name: 'Transport' }];

  it('keeps every item, in order, for an empty or blank term', () => {
    expect(filterByName(categories, '')).toEqual(categories);
    expect(filterByName(categories, '   ')).toEqual(categories);
  });

  it('returns a new array, leaving the input alone', () => {
    const result = filterByName(categories, '');
    expect(result).not.toBe(categories);
  });

  it('matches part of the name, ignoring case', () => {
    expect(filterByName(categories, 'OUT').map((c) => c.name)).toEqual(['Dining out']);
    expect(filterByName(categories, 'r').map((c) => c.name)).toEqual(['Groceries', 'Transport']);
  });

  it('ignores spaces around the term', () => {
    expect(filterByName(categories, '  trans ').map((c) => c.name)).toEqual(['Transport']);
  });

  it('returns nothing when no name matches', () => {
    expect(filterByName(categories, 'rent')).toEqual([]);
  });
});

describe('groupByCategory', () => {
  it('groups rules by category id, keeping their order', () => {
    const rules = [
      { id: 1, categoryId: 7 },
      { id: 2, categoryId: 3 },
      { id: 3, categoryId: 7 },
    ];

    const groups = groupByCategory(rules);

    expect(groups.get(7)?.map((r) => r.id)).toEqual([1, 3]);
    expect(groups.get(3)?.map((r) => r.id)).toEqual([2]);
  });

  it('has no entry for a category without rules', () => {
    expect(groupByCategory([{ id: 1, categoryId: 7 }]).has(3)).toBe(false);
    expect(groupByCategory([]).size).toBe(0);
  });
});

describe('plural and ruleCountLabel', () => {
  it('picks the word that fits the count', () => {
    expect(plural(1, 'category', 'categories')).toBe('1 category');
    expect(plural(0, 'category', 'categories')).toBe('0 categories');
    expect(plural(12, 'rule', 'rules')).toBe('12 rules');
  });

  it('says "No rules" for a category without any', () => {
    expect(ruleCountLabel(0)).toBe('No rules');
    expect(ruleCountLabel(1)).toBe('1 rule');
    expect(ruleCountLabel(4)).toBe('4 rules');
  });
});

describe('ruleAmount', () => {
  it('formats the amount in euros, with a true minus sign when negative', () => {
    expect(ruleAmount(1500)).toBe('€1,500.00');
    expect(ruleAmount(2.5)).toBe('€2.50');
    expect(ruleAmount(-12.5)).toBe('−€12.50');
  });

  it('is empty for a rule without an amount', () => {
    expect(ruleAmount(null)).toBe('');
    expect(ruleAmount(undefined)).toBe('');
  });
});

describe('ruleSummary', () => {
  it('describes a text rule, an amount rule and a rule with both', () => {
    expect(ruleSummary('TRF MB WAY', null, false)).toBe('contains TRF MB WAY');
    expect(ruleSummary(null, 2.5, false)).toBe('amount €2.50');
    expect(ruleSummary('Banana', 2.5, false)).toBe('contains Banana and amount €2.50');
  });

  it('says equals for a rule that matches the whole description', () => {
    expect(ruleSummary('TRF MB WAY', null, true)).toBe('equals TRF MB WAY');
    expect(ruleSummary('Banana', 2.5, true)).toBe('equals Banana and amount €2.50');
    expect(ruleSummary(null, 2.5, true)).toBe('amount €2.50');
  });

  it('treats an amount of zero as an amount', () => {
    expect(ruleSummary('', 0, true)).toBe('amount €0.00');
  });
});
