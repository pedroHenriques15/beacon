import { CATEGORY_UNKNOWN } from '../../core/constants/categories';
import {
  CategoryAmount,
  NetRow,
  NetTotals,
  categoryNet,
  incomeByCategory,
  spendingByCategory,
} from '../../core/utils/category-net';
import { MonthCell, MonthTotals, monthName, monthsUpTo } from '../../core/utils/month-totals';
import { eur } from '../../core/utils/money';
import { niceScale } from '../dashboard/river';

/** A category's total for a period. `color` is the category's colour, or a CSS fallback. */
export interface CategoryTotal {
  label: string;
  color: string;
  total: number;
}

/** Money in or money out: the side of the totals a category nets to (ADR-037). */
export type Side = 'in' | 'out';

/** A category for the bars, with its side when the list holds both (Insights' Spending tab). */
export interface CategoryLine extends CategoryTotal {
  side?: Side;
  /** What came in and went out before netting, when the category had both; else null. */
  detail?: string | null;
}

/** One row of the bars. */
export interface CategoryBar extends CategoryLine {
  /** Share of its side's total (of every row's, without sides), 0–100. */
  share: number;
  /** Bar length as a share of the scale's top, 0–100. */
  pct: number;
}

export interface CategoryBars {
  rows: CategoryBar[];
  /** The axis's values, from 0 to the scale's top. */
  ticks: number[];
}

/** Sums amounts per category, largest first; a category keeps the colour of its first row. */
export function sumByCategory(
  rows: { label: string; color: string; amount: number }[],
): CategoryTotal[] {
  const map = new Map<string, CategoryTotal>();
  for (const r of rows) {
    const cur = map.get(r.label);
    if (cur) cur.total += r.amount;
    else map.set(r.label, { label: r.label, color: r.color, total: r.amount });
  }
  return [...map.values()].sort((a, b) => b.total - a.total);
}

/**
 * Sorted bars, in the order given. Each row's share is of its side's total, or of every row's
 * when rows have no side. The scale is a round number above the largest row.
 */
export function categoryBars(rows: CategoryLine[]): CategoryBars {
  const sideTotal = new Map<Side | undefined, number>();
  for (const r of rows) sideTotal.set(r.side, (sideTotal.get(r.side) ?? 0) + r.total);
  const { top, step } = niceScale(Math.max(0, ...rows.map((r) => r.total)));
  const ticks = Array.from({ length: Math.round(top / step) + 1 }, (_, i) => i * step);
  return {
    ticks,
    rows: rows.map((r) => {
      const total = sideTotal.get(r.side) ?? 0;
      return {
        ...r,
        share: total > 0 ? (r.total / total) * 100 : 0,
        pct: Math.max(0, Math.min(100, (r.total / top) * 100)),
      };
    }),
  };
}

/** Which categories "By category" lists: both sides, or one. */
export type SideFilter = 'all' | Side;

/**
 * "By category": each category netted over the period (ADR-037) on the side its net falls,
 * largest first, both sides together with 'all'. The spending side keeps a category paid back
 * in full last, at zero. Rows without a category are Unknown on either side, kept gross, so
 * Unknown can appear twice. A category that had money both in and out says so in `detail`.
 * Every figure is divided by `months` (an average month).
 */
export function categoryLines(totals: NetTotals, filter: SideFilter, months = 1): CategoryLine[] {
  const nets = new Map(totals.categories.map((c) => [c.label, c]));
  const detail = (label: string): string | null => {
    const c = label === CATEGORY_UNKNOWN ? undefined : nets.get(label);
    if (!c || c.received <= 0 || c.spent <= 0) return null;
    return `${eur(c.spent / months)} out, ${eur(c.received / months)} in`;
  };
  const line = (side: Side) => (c: CategoryAmount) => ({
    ...c,
    total: c.total / months,
    side,
    detail: detail(c.label),
  });
  const lines = [
    ...(filter === 'out' ? [] : incomeByCategory(totals).map(line('in'))),
    ...(filter === 'in' ? [] : spendingByCategory(totals, true).map(line('out'))),
  ];
  return lines.sort(
    (a, b) => b.total - a.total || a.side.localeCompare(b.side) || a.label.localeCompare(b.label),
  );
}

/** How many months a period covers: one, three, six or twelve, or the year so far. */
export type RangeChoice = 1 | 3 | 6 | 12 | 'ytd';

/**
 * The calendar months of a period ending at `end`, oldest first: one month, `range` months, or
 * January of `end`'s year on ('ytd'), none before `first` (the first month with money) but
 * always `end`. Null for all months (`end` is '').
 */
export function periodKeys(end: string, range: RangeChoice, first?: string): string[] | null {
  if (!end) return null;
  const count = range === 'ytd' ? Number(end.slice(5, 7)) : range;
  return monthsUpTo(end, count)
    .reverse()
    .filter((k) => !first || k >= first || k === end);
}

/** A period's name: 'September' for one month, 'Apr – Sep 2026', 'Nov 2025 – Feb 2026'. */
export function periodName(keys: string[]): string {
  if (keys.length === 0) return '';
  const first = keys[0];
  const last = keys[keys.length - 1];
  if (keys.length === 1) return monthName(first);
  const year = (k: string) => k.slice(0, 4);
  return year(first) === year(last)
    ? `${monthName(first, 'short')} – ${monthName(last, 'short')} ${year(last)}`
    : `${monthName(first, 'short')} ${year(first)} – ${monthName(last, 'short')} ${year(last)}`;
}

/** One month of a category: what it netted, with what came in and went out before netting. */
export interface CategoryMonth {
  /** 'YYYY-MM' */
  month: string;
  /** received − spent. */
  net: number;
  received: number;
  spent: number;
}

/**
 * The months of one category (by label), oldest first, each netted on its own (ADR-037). For
 * Unknown, the rows without a category, which are never netted, `received` and `spent` are the
 * figures; `net` is only their difference.
 */
export function categoryMonths(
  rows: readonly (NetRow & { month: string })[],
  label: string,
): CategoryMonth[] {
  const byMonth = new Map<string, NetRow[]>();
  for (const r of rows) {
    if ((r.category?.name ?? CATEGORY_UNKNOWN) !== label) continue;
    if (r.type !== 'credit' && r.type !== 'debit') continue;
    const list = byMonth.get(r.month) ?? [];
    list.push(r);
    byMonth.set(r.month, list);
  }
  return [...byMonth.entries()]
    .sort(([a], [b]) => a.localeCompare(b))
    .map(([month, list]) => {
      const totals = categoryNet(list);
      const cat = totals.categories[0];
      if (cat) return { month, net: cat.net, received: cat.received, spent: cat.spent };
      const { income, spending } = totals.uncategorised;
      return { month, net: income - spending, received: income, spent: spending };
    });
}

/**
 * The months the flow chart shows, oldest first: `count` months up to the selected one, or the
 * latest `count` when all months are selected (''). `totals` is newest first.
 */
export function flowWindow(totals: MonthTotals[], selected: string, count: number): MonthTotals[] {
  return totals
    .filter((t) => !selected || t.month <= selected)
    .slice(0, count)
    .reverse();
}

/** The scrubber's cells plus an empty cell for each month in `keys` they lack, oldest first. */
export function withMonths(cells: MonthCell[], keys: string[]): MonthCell[] {
  const have = new Set(cells.map((c) => c.key));
  const extra = [...new Set(keys)]
    .filter((key) => !have.has(key))
    .map((key) => ({ key, income: 0, expenses: 0 }));
  if (extra.length === 0) return cells;
  return [...cells, ...extra].sort((a, b) => a.key.localeCompare(b.key));
}

/** '+4.8%' or '−54.2%' (a true minus sign); '0%' when it rounds to nothing. */
export function signedPct(value: number): string {
  if (Math.abs(value) < 0.05) return '0%';
  return (value < 0 ? '−' : '+') + Math.abs(value).toFixed(1) + '%';
}

/** '11.5% less than August', '4.8% more than August', 'level with August'; null without a base. */
export function compareText(
  current: number,
  previous: number | null,
  previousName: string,
): string | null {
  if (!previous) return null;
  const diff = current - previous;
  if (Math.abs(diff) < 0.005) return `level with ${previousName}`;
  const pct = Math.abs((diff / previous) * 100).toFixed(1);
  return `${pct}% ${diff < 0 ? 'less' : 'more'} than ${previousName}`;
}

export interface StoreTotal {
  store: string;
  receipts: number;
  total: number;
  /** Bar length as a share of the largest store, 0–100. */
  pct: number;
}

/** Grocery spending per store with its receipt count, largest first. */
export function storeTotals(
  items: { storeName: string; receiptId: number; amount: number; quantity: number }[],
): StoreTotal[] {
  const map = new Map<string, { total: number; receipts: Set<number> }>();
  for (const item of items) {
    const cur = map.get(item.storeName) ?? { total: 0, receipts: new Set<number>() };
    cur.total += item.amount * item.quantity;
    cur.receipts.add(item.receiptId);
    map.set(item.storeName, cur);
  }
  const rows = [...map.entries()]
    .map(([store, s]) => ({ store, receipts: s.receipts.size, total: s.total }))
    .sort((a, b) => b.total - a.total);
  const max = rows[0]?.total || 1;
  return rows.map((r) => ({ ...r, pct: Math.max(0, (r.total / max) * 100) }));
}

export interface BoughtItem {
  description: string;
  quantity: number;
  total: number;
}

/** The items with the most spent on them, with how many were bought. */
export function mostBought(
  items: { description: string; amount: number; quantity: number }[],
  limit = 8,
): BoughtItem[] {
  const map = new Map<string, BoughtItem>();
  for (const item of items) {
    const cur = map.get(item.description) ?? {
      description: item.description,
      quantity: 0,
      total: 0,
    };
    cur.quantity += item.quantity;
    cur.total += item.amount * item.quantity;
    map.set(item.description, cur);
  }
  return [...map.values()]
    .sort((a, b) => b.total - a.total || a.description.localeCompare(b.description))
    .slice(0, limit);
}
