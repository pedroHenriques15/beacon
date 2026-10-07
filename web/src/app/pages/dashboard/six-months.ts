import { CATEGORY_UNKNOWN } from '../../core/constants/categories';
import {
  CategoryAmount,
  NetRow,
  NetTotals,
  categoryNet,
  incomeByCategory,
  spendingByCategory,
} from '../../core/utils/category-net';

/** One month of a top category: its amount on the category's side, and its bar, 0–100. */
export interface TopMonth {
  month: string;
  amount: number;
  pct: number;
}

/** One of the categories that cost or brought in the most over a window of months. */
export interface TopCategory extends CategoryAmount {
  /** The total divided by the window's months. */
  average: number;
  /** One per month of the window, oldest first. */
  months: TopMonth[];
}

export interface WindowTops {
  spending: TopCategory[];
  income: TopCategory[];
}

function netOf(totals: NetTotals, label: string): number {
  return totals.categories.find((c) => c.label === label)?.net ?? 0;
}

/**
 * The categories that cost and brought in the most over a window of months, largest first, up
 * to `limit` a side, each netted once over the whole window (ADR-037), so a payback a month
 * after its expense cancels it. Each carries its monthly average and its amount on its side
 * month by month, each month netted on its own: 0 in a month it fell on the other side. Rows
 * without a category are Unknown on either side, kept gross. `months` is the window, oldest
 * first; rows of other months are left out.
 */
export function windowTops(
  rows: readonly (NetRow & { month: string })[],
  months: string[],
  limit = 5,
): WindowTops {
  const window = new Set(months);
  const counted = rows.filter((r) => window.has(r.month));
  const byMonth = months.map((m) => categoryNet(counted.filter((r) => r.month === m)));
  const whole = categoryNet(counted);
  const count = Math.max(1, months.length);

  const side = (list: CategoryAmount[], amountIn: (totals: NetTotals, label: string) => number) =>
    list.slice(0, limit).map((c) => {
      const amounts = byMonth.map((totals) => amountIn(totals, c.label));
      const top = Math.max(0, ...amounts);
      return {
        ...c,
        average: c.total / count,
        months: months.map((month, i) => ({
          month,
          amount: amounts[i],
          pct: top > 0 ? (amounts[i] / top) * 100 : 0,
        })),
      };
    });

  return {
    spending: side(spendingByCategory(whole), (totals, label) =>
      label === CATEGORY_UNKNOWN
        ? totals.uncategorised.spending
        : Math.max(0, -netOf(totals, label)),
    ),
    income: side(incomeByCategory(whole), (totals, label) =>
      label === CATEGORY_UNKNOWN ? totals.uncategorised.income : Math.max(0, netOf(totals, label)),
    ),
  };
}
