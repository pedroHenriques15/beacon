import { MonthCell, MonthTotals } from '../../core/utils/month-totals';
import { niceScale } from '../dashboard/river';

/** A category's total for a period. `color` is the category's colour, or a CSS fallback. */
export interface CategoryTotal {
  label: string;
  color: string;
  total: number;
}

/** One row of "Where it went": the period's total with the previous month's for comparison. */
export interface CategoryBar extends CategoryTotal {
  /** Share of the period's total, 0–100. */
  share: number;
  /** Bar length as a share of the scale's top, 0–100. */
  pct: number;
  /** The previous month's total; null when nothing is compared (all months). */
  previous: number | null;
  /** Where the previous month's tick sits, 0–100; null when nothing is compared. */
  prevPct: number | null;
  /** total − previous; null when nothing is compared. */
  change: number | null;
  /** The change as a percentage of the previous month; null when it had nothing. */
  changePct: number | null;
}

export interface CategoryBars {
  rows: CategoryBar[];
  /** The period's total over every category. */
  total: number;
  /** The axis's values, from 0 to the scale's top. */
  ticks: number[];
}

export interface CategoryMove {
  label: string;
  color: string;
  total: number;
  previous: number;
  change: number;
  /** null when the category had nothing the month before. */
  changePct: number | null;
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
 * Sorted bars for "Where it went". With `previous`, each row carries the previous month's total
 * and change, and categories that had spending then but none now follow at zero, so a drop to
 * nothing still shows. The scale is a round number above the largest of either month.
 */
export function categoryBars(
  current: CategoryTotal[],
  previous: CategoryTotal[] | null,
): CategoryBars {
  const prev = new Map((previous ?? []).map((p) => [p.label, p.total]));
  const labels = new Set(current.map((c) => c.label));
  const gone = (previous ?? [])
    .filter((p) => !labels.has(p.label) && p.total > 0)
    .map((p) => ({ label: p.label, color: p.color, total: 0 }));
  const rows = [...current, ...gone];

  const total = current.reduce((sum, c) => sum + c.total, 0);
  const max = Math.max(0, ...rows.map((r) => Math.max(r.total, prev.get(r.label) ?? 0)));
  const { top, step } = niceScale(max);
  const ticks = Array.from({ length: Math.round(top / step) + 1 }, (_, i) => i * step);
  const toPct = (v: number) => Math.max(0, Math.min(100, (v / top) * 100));

  return {
    total,
    ticks,
    rows: rows.map((r) => {
      const before = previous ? (prev.get(r.label) ?? 0) : null;
      const change = before === null ? null : r.total - before;
      return {
        ...r,
        share: total > 0 ? (r.total / total) * 100 : 0,
        pct: toPct(r.total),
        previous: before,
        prevPct: before === null ? null : toPct(before),
        change,
        changePct: change !== null && before ? (change / before) * 100 : null,
      };
    }),
  };
}

/** The categories whose spending changed most since the previous month, largest change first. */
export function biggestMoves(
  current: CategoryTotal[],
  previous: CategoryTotal[],
  limit = 3,
): CategoryMove[] {
  const byLabel = new Map<
    string,
    { label: string; color: string; total: number; previous: number }
  >();
  for (const p of previous)
    byLabel.set(p.label, { label: p.label, color: p.color, total: 0, previous: p.total });
  for (const c of current) {
    const known = byLabel.get(c.label);
    if (known) Object.assign(known, { color: c.color, total: c.total });
    else byLabel.set(c.label, { label: c.label, color: c.color, total: c.total, previous: 0 });
  }
  return [...byLabel.values()]
    .map((m) => {
      const change = m.total - m.previous;
      return { ...m, change, changePct: m.previous > 0 ? (change / m.previous) * 100 : null };
    })
    .filter((m) => Math.abs(m.change) >= 0.005)
    .sort((a, b) => Math.abs(b.change) - Math.abs(a.change) || a.label.localeCompare(b.label))
    .slice(0, limit);
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
