import { categoryNet } from '../../core/utils/category-net';
import { daysInMonth, previousMonth } from '../../core/utils/month-totals';

/** The fields the River chart reads from a counted transaction. */
export interface RiverTransaction {
  id: number;
  datePosting: string;
  description: string;
  amount: number;
  type: 'credit' | 'debit' | 'unknown';
  categoryId: number | null;
  category: { name: string; color?: string | null } | null;
  bank: string;
}

export interface RiverMark {
  id: number;
  day: number;
  amount: number;
  description: string;
  category: string | null;
  bank: string;
  /** Uncategorised: drawn as a hollow ring. */
  needsCategory: boolean;
  /** Cumulative spending at the end of the mark's day; where an outflow sits on the line. */
  at: number;
}

export interface RiverSeries {
  month: string;
  previous: string;
  /**
   * Cumulative spending by day, index 0 is day 1; ends today for the month in progress. Money
   * paid back into a category that nets to spending takes the line down on its day.
   */
  current: number[];
  /** The previous month, whole. */
  last: number[];
  outflows: RiverMark[];
  inflows: RiverMark[];
  total: number;
  lastTotal: number;
}

/**
 * A month's spending line: what each row adds to the month's spending, summed day by day. With
 * each category netted over the month (ADR-037), a category netting to spending adds its debits
 * and takes off its credits, one netting to income adds nothing, and a row without a category
 * adds its debit, so the line ends at the month's spending.
 */
function spendingLine(rows: RiverTransaction[], days: number): number[] {
  const toIncome = new Set(
    categoryNet(rows)
      .categories.filter((c) => c.net > 0)
      .map((c) => c.label),
  );
  const perDay = new Array<number>(days).fill(0);
  for (const t of rows) {
    const day = Number(t.datePosting.slice(8, 10));
    if (!t.category) {
      if (t.type === 'debit') perDay[day - 1] += t.amount;
    } else if (!toIncome.has(t.category.name)) {
      if (t.type === 'debit') perDay[day - 1] += t.amount;
      else if (t.type === 'credit') perDay[day - 1] -= t.amount;
    }
  }
  let sum = 0;
  return perDay.map((v) => (sum += v));
}

/**
 * The River chart's data for a month: cumulative counted spending day by day, the previous month
 * for comparison, one mark per outflow and per inflow. `transactions` must be the counted rows
 * (FinanceService.allTransactions), so excluded rows never reach a total. Rows of type 'unknown'
 * are neither in nor out and are left out, as in the monthly totals. Every row keeps its mark;
 * only the line nets each category.
 */
export function riverSeries(
  transactions: RiverTransaction[],
  month: string,
  today: string,
): RiverSeries {
  const previous = previousMonth(month);
  const days = daysInMonth(month);
  const shownDays = today.startsWith(month) ? Math.min(days, Number(today.slice(8, 10))) : days;

  const ofMonth = (key: string) => transactions.filter((t) => t.datePosting.startsWith(key));
  const toMark = (t: RiverTransaction): RiverMark => ({
    id: t.id,
    day: Number(t.datePosting.slice(8, 10)),
    amount: t.amount,
    description: t.description,
    category: t.category?.name ?? null,
    bank: t.bank,
    needsCategory: t.categoryId === null,
    at: 0,
  });

  const current = ofMonth(month);
  const outflows = current.filter((t) => t.type === 'debit').map(toMark);
  const inflows = current.filter((t) => t.type === 'credit').map(toMark);

  const line = spendingLine(current, days);
  for (const o of outflows) o.at = line[o.day - 1];
  const last = spendingLine(ofMonth(previous), daysInMonth(previous));

  return {
    month,
    previous,
    current: line.slice(0, shownDays),
    last,
    outflows: outflows.filter((o) => o.day <= shownDays),
    inflows: inflows.filter((o) => o.day <= shownDays),
    total: line[days - 1] ?? 0,
    lastTotal: last[last.length - 1] ?? 0,
  };
}

/** A round axis top above `max`, and its step, for four or five gridlines. */
export function niceScale(max: number): { top: number; step: number } {
  if (max <= 0) return { top: 100, step: 25 };
  const rough = max / 5;
  const magnitude = Math.pow(10, Math.floor(Math.log10(rough)));
  const step = [1, 2, 2.5, 5, 10].map((m) => m * magnitude).find((s) => s >= rough)!;
  return { top: Math.ceil(max / step) * step, step };
}

/**
 * A smooth path through points that never overshoots: monotone cubic interpolation
 * (Fritsch–Carlson), so the line only rises or falls where the points do.
 */
export function smoothPath(points: [number, number][]): string {
  if (points.length === 0) return '';
  if (points.length === 1) return `M${points[0][0].toFixed(1)} ${points[0][1].toFixed(1)}`;
  const slopes = monotoneSlopes(points);
  let path = `M${points[0][0].toFixed(1)} ${points[0][1].toFixed(1)}`;
  for (let i = 0; i < points.length - 1; i++) {
    const [x0, y0] = points[i];
    const [x1, y1] = points[i + 1];
    const h = (x1 - x0) / 3;
    path +=
      ` C${(x0 + h).toFixed(1)} ${(y0 + slopes[i] * h).toFixed(1)}` +
      ` ${(x1 - h).toFixed(1)} ${(y1 - slopes[i + 1] * h).toFixed(1)}` +
      ` ${x1.toFixed(1)} ${y1.toFixed(1)}`;
  }
  return path;
}

function monotoneSlopes(p: [number, number][]): number[] {
  const n = p.length;
  const d: number[] = [];
  for (let i = 0; i < n - 1; i++) d.push((p[i + 1][1] - p[i][1]) / (p[i + 1][0] - p[i][0]));
  const m: number[] = new Array(n);
  m[0] = d[0];
  m[n - 1] = d[n - 2];
  for (let i = 1; i < n - 1; i++) m[i] = d[i - 1] * d[i] <= 0 ? 0 : (d[i - 1] + d[i]) / 2;
  for (let i = 0; i < n - 1; i++) {
    if (d[i] === 0) {
      m[i] = 0;
      m[i + 1] = 0;
      continue;
    }
    const a = m[i] / d[i];
    const b = m[i + 1] / d[i];
    const s = a * a + b * b;
    if (s > 9) {
      const t = 3 / Math.sqrt(s);
      m[i] = t * a * d[i];
      m[i + 1] = t * b * d[i];
    }
  }
  return m;
}

/** Dot radius for an amount: area grows with the amount, capped so salaries stay readable. */
export function markRadius(amount: number, scale: number, max: number): number {
  return Math.min(max, 3 * scale + Math.sqrt(Math.abs(amount)) * 0.42 * scale);
}

/** A sparkline path for values across a box, with its area under it. */
export function sparkline(
  values: number[],
  width: number,
  height: number,
  pad = 4,
): { line: string; area: string; endY: number } | null {
  if (values.length < 2) return null;
  const min = Math.min(...values);
  const max = Math.max(...values);
  const span = max - min || 1;
  const pts = values.map((v, i): [number, number] => [
    pad + (i / (values.length - 1)) * (width - pad * 2),
    height - pad - ((v - min) / span) * (height - pad * 2),
  ]);
  const line = smoothPath(pts);
  const last = pts[pts.length - 1];
  return {
    line,
    area: `${line} L${last[0].toFixed(1)} ${height} L${pad} ${height} Z`,
    endY: last[1],
  };
}
