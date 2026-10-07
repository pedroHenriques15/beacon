/** One month's counted income and spending, each category netted (ADR-037). */
export interface MonthTotals {
  /** 'YYYY-MM' */
  month: string;
  income: number;
  expenses: number;
  net: number;
}

/** A cell of the month scrubber. */
export interface MonthCell {
  /** 'YYYY-MM' */
  key: string;
  income: number;
  expenses: number;
}

/**
 * One row per month, newest first, summing rows of the same month. Netting happens before: sum
 * only rows whose categories never meet (one bank's rows), never every bank's to get the whole
 * month, which is FinanceService.monthTotals.
 */
export function aggregateByMonth(
  summaries: { month: string; income: number; expenses: number; net: number }[],
): MonthTotals[] {
  const map = new Map<string, { income: number; expenses: number; net: number }>();
  for (const s of summaries) {
    const existing = map.get(s.month) ?? { income: 0, expenses: 0, net: 0 };
    map.set(s.month, {
      income: existing.income + s.income,
      expenses: existing.expenses + s.expenses,
      net: existing.net + s.net,
    });
  }
  return [...map.entries()]
    .sort(([a], [b]) => b.localeCompare(a))
    .map(([month, data]) => ({ month, ...data }));
}

/** The scrubber's months, oldest first, from the month totals (FinanceService.monthTotals). */
export function monthCells(
  summaries: { month: string; income: number; expenses: number; net: number }[],
): MonthCell[] {
  return aggregateByMonth(summaries)
    .reverse()
    .map((m) => ({ key: m.month, income: m.income, expenses: m.expenses }));
}

export function previousMonth(key: string): string {
  const [y, m] = key.split('-').map(Number);
  return m === 1 ? `${y - 1}-12` : `${y}-${String(m - 1).padStart(2, '0')}`;
}

/** The calendar month of a date, as 'YYYY-MM', in local time. */
export function monthKeyOf(date: Date): string {
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}`;
}

/**
 * The latest closed month: the most recent month with data before the current one, else the
 * latest month with data, else the current month. `months` is newest first.
 */
export function latestClosedMonth(months: string[], nowKey: string): string {
  return months.find((m) => m < nowKey) ?? months[0] ?? nowKey;
}

/** 'September', or 'Sep' when short (en-GB's own short form is 'Sept'). */
export function monthName(key: string, style: 'long' | 'short' = 'long'): string {
  const [y, m] = key.split('-').map(Number);
  const name = new Date(y, m - 1, 1).toLocaleString('en-GB', { month: 'long' });
  return style === 'short' ? name.slice(0, 3) : name;
}

export function monthYearLabel(key: string): string {
  const [y, m] = key.split('-').map(Number);
  return new Date(y, m - 1, 1).toLocaleString('en-GB', { month: 'long', year: 'numeric' });
}

export function daysInMonth(key: string): number {
  const [y, m] = key.split('-').map(Number);
  return new Date(y, m, 0).getDate();
}

/** `count` calendar months ending at `end`, newest first. */
export function monthsUpTo(end: string, count: number): string[] {
  const keys = [end];
  while (keys.length < count) keys.push(previousMonth(keys[keys.length - 1]));
  return keys;
}

/**
 * Kept as a share of what came in, a whole percent (the savings rate), halves away from zero so
 * a loss rounds like a gain; null without income.
 */
export function keptShare(income: number, kept: number): number | null {
  if (income <= 0) return null;
  const pct = (kept / income) * 100;
  return Math.sign(pct) * Math.round(Math.abs(pct));
}

/** '32%', '−12%' (a true minus sign), or '—' without income. */
export function keptShareText(share: number | null): string {
  if (share === null) return '—';
  return `${share < 0 ? '−' : ''}${Math.abs(share)}%`;
}
