import { CATEGORY_EXCLUDED, CATEGORY_UNKNOWN } from '../constants/categories';

/** A category without a colour of its own. */
export const CATEGORY_FALLBACK_COLOR = 'var(--category-fallback)';

/** The fields of a transaction that category netting reads. */
export interface NetRow {
  amount: number;
  type: 'credit' | 'debit' | 'unknown';
  category: { name: string; color?: string | null } | null;
  isExcluded?: boolean;
}

/** One category's money over the rows given. */
export interface CategoryNet {
  label: string;
  color: string;
  /** What came in less what went out, to the cent: above zero it is income, else spending. */
  net: number;
  /** The credits and debits before netting, as detail. */
  received: number;
  spent: number;
}

export interface NetTotals {
  /** Categories netting to income, plus every uncategorised credit. */
  income: number;
  /** Categories netting to spending, plus every uncategorised debit. */
  spending: number;
  /** income − spending: credits less debits, the same as before netting. */
  net: number;
  /** One per category, uncategorised rows not among them. */
  categories: CategoryNet[];
  /** Rows without a category, kept gross: nothing relates one to another. */
  uncategorised: { income: number; spending: number };
}

/** A category's amount on one side of the totals, always zero or above. */
export interface CategoryAmount {
  label: string;
  color: string;
  total: number;
}

const cents = (value: number) => Math.round(value * 100) / 100;

/**
 * Totals with each category netted (ADR-037): a category's credits and debits cancel, so money
 * paid back (a shared dinner, a refund) lowers its spending instead of counting as income. A
 * category netting above zero is income of that amount, one at or below zero spending of the
 * absolute amount. Rows without a category are not netted. Excluded rows and rows of an
 * unclassified type add nothing, whatever the caller passes.
 */
export function categoryNet(rows: readonly NetRow[]): NetTotals {
  const byLabel = new Map<string, CategoryNet>();
  const uncategorised = { income: 0, spending: 0 };
  for (const tx of rows) {
    if (tx.isExcluded || tx.category?.name === CATEGORY_EXCLUDED) continue;
    if (tx.type !== 'credit' && tx.type !== 'debit') continue;
    if (!tx.category) {
      if (tx.type === 'credit') uncategorised.income += tx.amount;
      else uncategorised.spending += tx.amount;
      continue;
    }
    const label = tx.category.name;
    let cat = byLabel.get(label);
    if (!cat) {
      cat = {
        label,
        color: tx.category.color || CATEGORY_FALLBACK_COLOR,
        net: 0,
        received: 0,
        spent: 0,
      };
      byLabel.set(label, cat);
    }
    if (tx.type === 'credit') cat.received += tx.amount;
    else cat.spent += tx.amount;
  }

  const categories = [...byLabel.values()].map((c) => ({ ...c, net: cents(c.received - c.spent) }));
  let income = uncategorised.income;
  let spending = uncategorised.spending;
  for (const c of categories) {
    if (c.net > 0) income += c.net;
    else spending -= c.net;
  }
  return { income, spending, net: income - spending, categories, uncategorised };
}

function byTotal(a: CategoryAmount, b: CategoryAmount): number {
  return b.total - a.total || a.label.localeCompare(b.label);
}

/**
 * The spending side by category, largest first: each category netting to spending, the
 * uncategorised debits as Unknown, and with `withZero` the categories paid back in full, at 0.
 */
export function spendingByCategory(totals: NetTotals, withZero = false): CategoryAmount[] {
  const rows = totals.categories
    .filter((c) => c.net < 0 || (withZero && c.net === 0))
    .map((c) => ({ label: c.label, color: c.color, total: -c.net || 0 }));
  if (totals.uncategorised.spending > 0)
    rows.push({
      label: CATEGORY_UNKNOWN,
      color: CATEGORY_FALLBACK_COLOR,
      total: totals.uncategorised.spending,
    });
  return rows.sort(byTotal);
}

/** The income side by category, largest first, the uncategorised credits as Unknown. */
export function incomeByCategory(totals: NetTotals): CategoryAmount[] {
  const rows = totals.categories
    .filter((c) => c.net > 0)
    .map((c) => ({ label: c.label, color: c.color, total: c.net }));
  if (totals.uncategorised.income > 0)
    rows.push({
      label: CATEGORY_UNKNOWN,
      color: CATEGORY_FALLBACK_COLOR,
      total: totals.uncategorised.income,
    });
  return rows.sort(byTotal);
}
