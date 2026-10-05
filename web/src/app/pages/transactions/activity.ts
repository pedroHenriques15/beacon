import { CATEGORY_EXCLUDED } from '../../core/constants/categories';
import { MonthCell } from '../../core/utils/month-totals';
import { eur, signedEur } from '../../core/utils/money';

/** The fields of a transaction the Activity page's figures read. */
export interface ActivityTx {
  id: number;
  datePosting: string;
  description: string;
  amount: number;
  type: 'credit' | 'debit' | 'unknown';
  categoryId: number | null;
  isExcluded: boolean;
  category: { name: string } | null;
  bank: string;
}

/** The fields of a grocery item the Groceries tab's figures read. */
export interface ActivityItem {
  id: number;
  receiptId: number;
  storeName: string;
  receiptDate: string;
  description: string;
  amount: number;
  quantity: number;
  categoryId: number | null;
  categoryName: string | null;
  isExcluded: boolean;
}

/** The Transactions tab's filters; '' means any. */
export interface TxFilter {
  bank: string;
  month: string;
  type: string;
  /** A category id, or 'unknown' for rows without a category. */
  category: string;
  search: string;
}

/** The Groceries tab's filters; '' means any. */
export interface ItemFilter {
  store: string;
  month: string;
  category: string;
  search: string;
}

/** Out of every total: flagged excluded, or in the Excluded category (as FinanceService). */
export function isExcludedTx(tx: Pick<ActivityTx, 'isExcluded' | 'category'>): boolean {
  return tx.isExcluded || tx.category?.name === CATEGORY_EXCLUDED;
}

/** Out of every total: flagged excluded, or in the Excluded category (as GroceriesService). */
export function isExcludedItem(item: Pick<ActivityItem, 'isExcluded' | 'categoryName'>): boolean {
  return item.isExcluded || item.categoryName === CATEGORY_EXCLUDED;
}

function matchesCategory(categoryId: number | null, filter: string): boolean {
  if (!filter) return true;
  if (filter === 'unknown') return categoryId === null;
  const id = Number(filter);
  return !Number.isInteger(id) || categoryId === id;
}

function matchesSearch(text: string, search: string): boolean {
  return !search || text.toLowerCase().includes(search.toLowerCase());
}

/** The API's transaction filters, applied to rows the client already holds. */
export function matchesTxFilter(tx: ActivityTx, f: TxFilter): boolean {
  return (
    (!f.bank || tx.bank.toUpperCase() === f.bank.toUpperCase()) &&
    (!f.month || tx.datePosting.slice(0, 7) === f.month) &&
    (!f.type || tx.type === f.type.toLowerCase()) &&
    matchesCategory(tx.categoryId, f.category) &&
    matchesSearch(tx.description, f.search)
  );
}

/** The API's grocery item filters (the store matches by part of its name). */
export function matchesItemFilter(item: ActivityItem, f: ItemFilter): boolean {
  return (
    matchesSearch(item.storeName, f.store) &&
    (!f.month || item.receiptDate.slice(0, 7) === f.month) &&
    matchesCategory(item.categoryId, f.category) &&
    matchesSearch(item.description, f.search)
  );
}

/**
 * What a row adds to a day's or a filter's total: + for money in, − for money out, null when
 * it is left out (excluded, or of an unclassified type).
 */
export function countedAmount(tx: ActivityTx): number | null {
  if (isExcludedTx(tx) || tx.type === 'unknown') return null;
  return tx.type === 'credit' ? tx.amount : -tx.amount;
}

/** '+€2,980.00' in, '−€42.18' out, '€12.00' when the type is unclassified. */
export function txAmountText(tx: Pick<ActivityTx, 'amount' | 'type'>): string {
  if (tx.type === 'credit') return signedEur(tx.amount);
  if (tx.type === 'debit') return signedEur(-tx.amount);
  return eur(tx.amount);
}

export interface FlowTotals {
  income: number;
  expenses: number;
  net: number;
  /** Rows that are not excluded, unclassified ones included. */
  count: number;
}

/** Money in and out; excluded rows and rows of an unclassified type add nothing. */
export function flowTotals(rows: readonly ActivityTx[]): FlowTotals {
  let income = 0;
  let expenses = 0;
  let count = 0;
  for (const tx of rows) {
    if (isExcludedTx(tx)) continue;
    count++;
    if (tx.type === 'credit') income += tx.amount;
    else if (tx.type === 'debit') expenses += tx.amount;
  }
  return { income, expenses, net: income - expenses, count };
}

export interface BankTotals extends FlowTotals {
  bank: string;
}

/** flowTotals per bank, the bank with the most money out first. */
export function totalsByBank(rows: readonly ActivityTx[]): BankTotals[] {
  const byBank = new Map<string, ActivityTx[]>();
  for (const tx of rows) {
    if (isExcludedTx(tx)) continue;
    byBank.set(tx.bank, [...(byBank.get(tx.bank) ?? []), tx]);
  }
  return [...byBank.entries()]
    .map(([bank, txs]) => ({ bank, ...flowTotals(txs) }))
    .sort((a, b) => b.expenses - a.expenses || b.income - a.income || a.bank.localeCompare(b.bank));
}

export interface DayGroup<T> {
  key: string;
  /** 'YYYY-MM-DD', or null for the single group of a list not sorted by date. */
  date: string | null;
  rows: T[];
  /** Sum of the counted rows' amounts. */
  total: number;
  /** How many rows count towards the total. */
  counted: number;
}

/**
 * Splits rows into runs of the same day, in the order given. Unless `byDay`, every row goes in
 * one group without a date (a list sorted by amount or name has no day order to show).
 */
export function dayGroups<T>(
  rows: readonly T[],
  byDay: boolean,
  dateOf: (row: T) => string,
  amountOf: (row: T) => number | null,
): DayGroup<T>[] {
  const groups: DayGroup<T>[] = [];
  for (const row of rows) {
    const date = byDay ? dateOf(row).slice(0, 10) : null;
    const last = groups[groups.length - 1];
    let group: DayGroup<T>;
    if (last && last.date === date) {
      group = last;
    } else {
      const key = date ?? 'all';
      const unique = groups.some((g) => g.key === key) ? `${key}-${groups.length}` : key;
      group = { key: unique, date, rows: [], total: 0, counted: 0 };
      groups.push(group);
    }
    group.rows.push(row);
    const amount = amountOf(row);
    if (amount !== null) {
      group.total += amount;
      group.counted++;
    }
  }
  return groups;
}

export interface StoreTotal {
  store: string;
  amount: number;
  receipts: number;
  /** Bar length against the largest store, 0–100. */
  pct: number;
}

/** Counted grocery spending per store, largest first. */
export function storeTotals(items: readonly ActivityItem[]): StoreTotal[] {
  const map = new Map<string, { amount: number; receipts: Set<number> }>();
  for (const item of items) {
    if (isExcludedItem(item)) continue;
    const cur = map.get(item.storeName) ?? { amount: 0, receipts: new Set<number>() };
    cur.amount += item.amount;
    cur.receipts.add(item.receiptId);
    map.set(item.storeName, cur);
  }
  const rows = [...map.entries()]
    .map(([store, s]) => ({ store, amount: s.amount, receipts: s.receipts.size }))
    .sort((a, b) => b.amount - a.amount || a.store.localeCompare(b.store));
  const max = rows[0]?.amount || 1;
  return rows.map((r) => ({ ...r, pct: (r.amount / max) * 100 }));
}

export interface BoughtItem {
  description: string;
  quantity: number;
  amount: number;
}

/** The counted items with the most money spent on them, summed by description. */
export function mostBought(items: readonly ActivityItem[], limit = 5): BoughtItem[] {
  const map = new Map<string, BoughtItem>();
  for (const item of items) {
    if (isExcludedItem(item)) continue;
    const key = item.description.trim();
    const cur = map.get(key) ?? { description: key, quantity: 0, amount: 0 };
    map.set(key, {
      ...cur,
      quantity: cur.quantity + item.quantity,
      amount: cur.amount + item.amount,
    });
  }
  return [...map.values()]
    .sort((a, b) => b.amount - a.amount || a.description.localeCompare(b.description))
    .slice(0, limit)
    .map((b) => ({ ...b, quantity: Math.round(b.quantity * 1000) / 1000 }));
}

/**
 * The scrubber's months plus any month the page can show that has no counted money (a month of
 * excluded rows only, a grocery month without statements, a month from a link), oldest first.
 */
export function withMonths(cells: readonly MonthCell[], extra: readonly string[]): MonthCell[] {
  const known = new Set(cells.map((c) => c.key));
  const added = [...new Set(extra)]
    .filter((key) => /^\d{4}-\d{2}$/.test(key) && !known.has(key))
    .map((key) => ({ key, income: 0, expenses: 0 }));
  return [...cells, ...added].sort((a, b) => a.key.localeCompare(b.key));
}

/** Where a popover goes: under its anchor if it fits, else above; always on screen. */
export function popoverPosition(
  anchor: { top: number; bottom: number; left: number; right: number },
  size: { width: number; height: number },
  viewport: { width: number; height: number },
  alignRight = false,
): { top: number; left: number } {
  const margin = 8;
  const below = viewport.height - anchor.bottom - margin >= size.height;
  const top = below ? anchor.bottom + 4 : Math.max(margin, anchor.top - size.height - 4);
  const wanted = alignRight ? anchor.right - size.width : anchor.left;
  const left = Math.max(margin, Math.min(wanted, viewport.width - size.width - margin));
  return { top, left };
}
