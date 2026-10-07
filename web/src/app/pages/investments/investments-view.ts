import {
  AssetMetric,
  PortfolioPoint,
  earliestBuy,
  returnPct,
} from '../../core/services/investments.service';
import { InvestmentAsset, InvestmentLot } from '../../core/models/statement.model';
import { eurWhole, signedEur } from '../../core/utils/money';

/** The figures at the top of the page, for the holdings of the active tab. */
export interface PortfolioSummary {
  value: number;
  cost: number;
  unrealised: number;
  /** Unrealised against cost; null without a cost basis. */
  unrealisedPct: number | null;
  realised: number;
  /** Money put in: every buy with its fees, sold since or not (ADR-038). */
  invested: number;
  /** Realised plus unrealised. */
  totalReturn: number;
  /** Total return against the money put in; null with nothing put in. */
  totalReturnPct: number | null;
  /** The earliest buy among the tab's assets. */
  since: string | null;
  change1d: number | null;
  change1w: number | null;
  change1m: number | null;
}

/**
 * Value-weighted change of the held positions over one period, from each asset's own change:
 * an asset's reference value is its value now divided by (1 + change). Matches
 * InvestmentsService's portfolio changes when given every asset.
 */
function weightedChange(
  metrics: AssetMetric[],
  pick: (m: AssetMetric) => number | null,
): number | null {
  let current = 0;
  let reference = 0;
  for (const m of metrics) {
    const pct = pick(m);
    if (pct == null || m.currentPrice == null || m.totalQuantity <= 0) continue;
    const factor = 1 + pct / 100;
    if (factor <= 0) continue;
    const now = m.totalQuantity * m.currentPrice;
    current += now;
    reference += now / factor;
  }
  return reference > 0 ? ((current - reference) / reference) * 100 : null;
}

export function portfolioSummary(metrics: AssetMetric[]): PortfolioSummary {
  const value = metrics.reduce((s, m) => s + (m.currentValue ?? 0), 0);
  const cost = metrics.reduce((s, m) => s + m.netCostBasis, 0);
  const unrealised = value - cost;
  const realised = metrics.reduce((s, m) => s + m.realizedPnl, 0);
  const invested = metrics.reduce((s, m) => s + m.invested, 0);
  return {
    value,
    cost,
    unrealised,
    unrealisedPct: cost > 0 ? (unrealised / cost) * 100 : null,
    realised,
    invested,
    totalReturn: realised + unrealised,
    totalReturnPct: returnPct(realised + unrealised, invested),
    since: earliestBuy(metrics),
    change1d: weightedChange(metrics, (m) => m.change1d),
    change1w: weightedChange(metrics, (m) => m.change1w),
    change1m: weightedChange(metrics, (m) => m.change1m),
  };
}

/** '+2.87%' or '−0.30%' (a true minus sign); zero has no sign; '—' when unknown. */
export function signedPct(value: number | null, digits = 2): string {
  if (value == null || !Number.isFinite(value)) return '—';
  const text = Math.abs(value).toFixed(digits) + '%';
  if (Math.abs(value) < 0.5 * Math.pow(10, -digits)) return text;
  return (value < 0 ? '−' : '+') + text;
}

const EUR_PRICE = new Intl.NumberFormat('en-GB', {
  style: 'currency',
  currency: 'EUR',
  minimumFractionDigits: 2,
  maximumFractionDigits: 4,
});

/** A unit price: '€138.42', or up to four decimals: '€34.1035'. */
export function eurPrice(value: number): string {
  return EUR_PRICE.format(value);
}

/** A y-axis label that keeps close ticks apart: '€6,540', '€137.5'. */
export function eurTick(value: number): string {
  return '€' + value.toLocaleString('en-GB', { maximumFractionDigits: 2 });
}

const DAY = new Intl.DateTimeFormat('en-GB', {
  day: 'numeric',
  month: 'short',
  year: 'numeric',
  timeZone: 'UTC',
});
const DAY_NO_YEAR = new Intl.DateTimeFormat('en-GB', {
  day: 'numeric',
  month: 'short',
  timeZone: 'UTC',
});
const MONTH_YEAR = new Intl.DateTimeFormat('en-GB', {
  month: 'short',
  year: 'numeric',
  timeZone: 'UTC',
});
const WEEKDAY = new Intl.DateTimeFormat('en-GB', {
  weekday: 'short',
  day: 'numeric',
  month: 'short',
  timeZone: 'UTC',
});

function utc(iso: string): Date {
  return new Date(iso.slice(0, 10) + 'T00:00:00Z');
}

/** en-GB writes September as 'Sept'; the rest of the client says 'Sep'. */
function sep(text: string): string {
  return text.replace('Sept', 'Sep');
}

/** '5 Oct 2025'. */
export function shortDate(iso: string): string {
  return sep(DAY.format(utc(iso)));
}

/** 'Fri 2 Oct', with the year added when it is not this year's. */
export function weekdayDate(iso: string, today: string): string {
  const text = sep(WEEKDAY.format(utc(iso)));
  return iso.slice(0, 4) === today.slice(0, 4) ? text : `${text} ${iso.slice(0, 4)}`;
}

/** 'Jan 2026'. */
export function monthYear(iso: string): string {
  return sep(MONTH_YEAR.format(utc(iso)));
}

/** An x-axis label: '5 Oct' over a short span, 'Oct 2025' over a long one. */
export function axisDate(iso: string, longSpan: boolean): string {
  return longSpan ? monthYear(iso) : sep(DAY_NO_YEAR.format(utc(iso)));
}

/** A date as a number for a time-proportional (linear) x axis. */
export function dayValue(iso: string): number {
  return utc(iso).getTime();
}

/** The label of a linear x-axis tick at `ticks[index]`; blank when it repeats the one before. */
export function timeTick(ticks: { value: number }[], index: number, longSpan: boolean): string {
  const label = (i: number) => axisDate(new Date(ticks[i].value).toISOString(), longSpan);
  const text = label(index);
  return index > 0 && label(index - 1) === text ? '' : text;
}

/** Days between the first and the last point. */
export function spanDays(points: { date: string }[]): number {
  if (points.length < 2) return 0;
  const ms = utc(points[points.length - 1].date).getTime() - utc(points[0].date).getTime();
  return Math.round(ms / 86_400_000);
}

export interface HistoryCaption {
  title: string;
  detail: string;
}

/**
 * What the value chart shows, in words: how much the value moved over the shown points, and
 * how much of that was money put in (or taken out) and how much came from prices.
 */
export function historyCaption(points: PortfolioPoint[]): HistoryCaption | null {
  if (points.length < 2) return null;
  const first = points[0];
  const last = points[points.length - 1];
  const change = last.totalValue - first.totalValue;
  const putIn = last.invested - first.invested;
  const growth = change - putIn;
  const since = `since ${shortDate(first.date)}`;
  const title =
    Math.abs(change) < 0.5
      ? `No change ${since}`
      : `${change > 0 ? 'Up' : 'Down'} ${eurWhole(change)} ${since}`;
  const prices = `${signedEur(growth, true)} from price moves`;
  const detail =
    putIn >= 0.5
      ? `${eurWhole(putIn)} put in, ${prices}`
      : putIn <= -0.5
        ? `${eurWhole(putIn)} taken out, ${prices}`
        : prices;
  return { title, detail };
}

export interface AllocationSlice {
  label: string;
  value: number;
  color: string;
  /** Share of the total, 0 to 100. */
  pct: number;
}

export function allocationShares(
  data: { label: string; value: number; color: string }[],
): AllocationSlice[] {
  const total = data.reduce((s, d) => s + d.value, 0);
  return data.map((d) => ({ ...d, pct: total > 0 ? (d.value / total) * 100 : 0 }));
}

/**
 * Each asset's allocation colour by asset id. `allocation` is InvestmentsService.allocationDataFor
 * of the same metrics, in their order: one entry per metric with a value above zero. Assets
 * without a value have no colour (the template falls back to --category-fallback).
 */
export function assetColors(
  metrics: AssetMetric[],
  allocation: { color: string }[],
): Map<number, string> {
  const valued = metrics.filter((m) => (m.currentValue ?? 0) > 0);
  const colors = new Map<number, string>();
  valued.forEach((m, i) => {
    const color = allocation[i]?.color;
    if (color) colors.set(m.asset.id, color);
  });
  return colors;
}

export interface ActivityItem {
  lot: InvestmentLot;
  asset: InvestmentAsset;
  kind: 'buy' | 'sell';
  /** A buy's cost with fees; a sell's realised gain (negative for a loss), at average cost. */
  amount: number;
}

/**
 * The latest buys and sells across the assets, newest first. A sell's realised result uses the
 * average cost at the time, as InvestmentsService's realised P&L does.
 */
export function recentActivity(assets: InvestmentAsset[], limit: number): ActivityItem[] {
  const items: ActivityItem[] = [];
  for (const asset of assets) {
    const lots = [...asset.lots].sort((a, b) => a.date.localeCompare(b.date) || a.id - b.id);
    let avgCost = 0;
    let held = 0;
    for (const lot of lots) {
      const fee = lot.fees ?? 0;
      if (lot.quantity > 0) {
        avgCost =
          held > 0
            ? (held * avgCost + lot.quantity * lot.pricePerUnit + fee) / (held + lot.quantity)
            : (lot.quantity * lot.pricePerUnit + fee) / lot.quantity;
        held += lot.quantity;
        items.push({ lot, asset, kind: 'buy', amount: lot.quantity * lot.pricePerUnit + fee });
      } else {
        const qty = Math.min(Math.abs(lot.quantity), Math.max(held, 0));
        const realised = qty * lot.pricePerUnit - qty * avgCost - fee;
        held -= Math.abs(lot.quantity);
        items.push({ lot, asset, kind: 'sell', amount: realised });
      }
    }
  }
  return items
    .sort((a, b) => b.lot.date.localeCompare(a.lot.date) || b.lot.id - a.lot.id)
    .slice(0, limit);
}
