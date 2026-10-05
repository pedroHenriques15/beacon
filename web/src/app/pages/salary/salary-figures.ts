import { HourlyRateFormula, SalaryLineItem, SalarySlip } from '../../core/models/statement.model';
import { eur, signedEur } from '../../core/utils/money';
import { monthName, monthYearLabel } from '../../core/utils/month-totals';

const NUMBER = new Intl.NumberFormat('en-GB', { maximumFractionDigits: 2 });

const TYPE_LABELS: Record<string, string> = {
  income: 'Income',
  deduction: 'Deduction',
  tax: 'Tax',
};

const FORMULA_NOTES: Record<HourlyRateFormula, string> = {
  hours: 'Take-home ÷ hours worked',
  days: 'Take-home ÷ (days worked × 8 h)',
  workdays: 'Take-home ÷ (weekdays × 8 h)',
};

/** A slip's month as 'YYYY-MM', from its period ('2026-09-01'). */
export function periodKey(period: string): string {
  return period.slice(0, 7);
}

/** 'Sep 2026'. */
export function shortPeriod(period: string): string {
  const key = periodKey(period);
  return `${monthName(key, 'short')} ${key.slice(0, 4)}`;
}

/**
 * Take-home: the line items' income less their deductions and tax, or the slip's own net when it
 * has no line items.
 */
export function slipNet(slip: Pick<SalarySlip, 'lineItems' | 'netAmount'>): number {
  if (!slip.lineItems?.length) return slip.netAmount;
  return slip.lineItems.reduce(
    (sum, li) => sum + (li.categoryItemType === 'income' ? li.amount : -li.amount),
    0,
  );
}

/** Gross less take-home: deductions and tax together. */
export function slipWithheld(slip: Pick<SalarySlip, 'lineItems' | 'netAmount' | 'grossAmount'>) {
  return slip.grossAmount - slipNet(slip);
}

/** The share of the gross that does not reach take-home, in percent; null without a gross. */
export function effectiveRate(
  slip: Pick<SalarySlip, 'lineItems' | 'netAmount' | 'grossAmount'>,
): number | null {
  return slip.grossAmount > 0 ? (slipWithheld(slip) / slip.grossAmount) * 100 : null;
}

/** Monday to Friday days in the slip's month. */
export function workdaysInMonth(period: string): number {
  const [year, month] = periodKey(period).split('-').map(Number);
  const days = new Date(year, month, 0).getDate();
  let count = 0;
  for (let day = 1; day <= days; day++) {
    const weekday = new Date(year, month - 1, day).getDay();
    if (weekday !== 0 && weekday !== 6) count++;
  }
  return count;
}

/** Take-home per hour, by the profile's formula; null when the slip has no hours or days. */
export function trueHourlyRate(
  slip: Pick<SalarySlip, 'lineItems' | 'netAmount' | 'period' | 'hoursWorked'>,
  formula: HourlyRateFormula,
): number | null {
  const net = slipNet(slip);
  if (formula === 'workdays') return net / (workdaysInMonth(slip.period) * 8);
  if (!slip.hoursWorked || slip.hoursWorked <= 0) return null;
  return formula === 'hours' ? net / slip.hoursWorked : net / (slip.hoursWorked * 8);
}

/** What `hoursWorked` holds for a profile: hours, or days for the day-based formulas. */
export function workedLabel(formula: HourlyRateFormula): string {
  return formula === 'hours' ? 'Hours worked' : 'Days worked';
}

export interface SlipTile {
  label: string;
  value: string;
  note: string;
}

/** The rate and hours tiles beside a slip; a tile shows only when the slip has its figure. */
export function slipTiles(
  slip: SalarySlip,
  formula: HourlyRateFormula,
  slipCount: number,
  firstPeriod: string | null,
): SlipTile[] {
  const rate = effectiveRate(slip);
  const tiles: SlipTile[] = [
    {
      label: 'Effective rate',
      value: rate === null ? '–' : `${rate.toFixed(1)}%`,
      note: `${eur(slipWithheld(slip))} deductions and tax`,
    },
  ];
  if (slip.baseAmount) {
    tiles.push({ label: 'Base salary', value: eur(slip.baseAmount), note: 'On the slip' });
  }
  if (slip.hoursWorked) {
    const amount = NUMBER.format(slip.hoursWorked);
    tiles.push({
      label: workedLabel(formula),
      value: formula === 'hours' ? `${amount} h` : amount,
      note: monthName(periodKey(slip.period)),
    });
  }
  if (slip.hourlyRate) {
    tiles.push({ label: 'Hourly rate', value: eur(slip.hourlyRate), note: 'On the slip' });
  }
  const trueRate = trueHourlyRate(slip, formula);
  if (trueRate !== null) {
    tiles.push({ label: 'True hourly rate', value: eur(trueRate), note: FORMULA_NOTES[formula] });
  }
  if (slip.totalEspecie) {
    tiles.push({ label: 'Non-cash benefits', value: eur(slip.totalEspecie), note: 'Paid in kind' });
  }
  tiles.push({
    label: 'Slips',
    value: String(slipCount),
    note: firstPeriod ? `Since ${shortPeriod(firstPeriod)}` : 'In this profile',
  });
  return tiles;
}

/** 'Deduction, 11.0% of €1,567.86' or 'Income, 22 × €7.63'. */
export function lineDetail(li: SalaryLineItem): string {
  const parts = [TYPE_LABELS[li.categoryItemType] ?? li.categoryItemType];
  if (li.quantity && li.unitValue) {
    parts.push(`${NUMBER.format(li.quantity)} × ${eur(li.unitValue)}`);
  }
  if (li.percentage && li.incidenciaBase) {
    parts.push(`${li.percentage.toFixed(1)}% of ${eur(li.incidenciaBase)}`);
  }
  return parts.join(', ');
}

export interface SlipLine {
  id: number;
  name: string;
  /** The item category's colour; null falls back to the "Other" colour. */
  color: string | null;
  detail: string;
  /** '+€1,400.00' for income, '−€172.46' for a deduction or tax. */
  amount: string;
  inflow: boolean;
}

/** "On the slip": income lines, then deduction and tax lines, each in the slip's order. */
export function slipLines(slip: Pick<SalarySlip, 'lineItems'>): {
  incomes: SlipLine[];
  outgoings: SlipLine[];
} {
  const items = [...(slip.lineItems ?? [])].sort((a, b) => a.sortOrder - b.sortOrder);
  const toLine = (li: SalaryLineItem): SlipLine => {
    const signed = li.categoryItemType === 'income' ? li.amount : -li.amount;
    return {
      id: li.id,
      name: li.categoryName,
      color: li.categoryColor || null,
      detail: lineDetail(li),
      amount: signedEur(signed),
      inflow: signed > 0,
    };
  };
  return {
    incomes: items.filter((li) => li.categoryItemType === 'income').map(toLine),
    outgoings: items.filter((li) => li.categoryItemType !== 'income').map(toLine),
  };
}

/**
 * The profile shown first: the one with the most recent slip, else the first profile; null
 * without profiles.
 */
export function defaultProfileId(
  profiles: { id: number }[],
  slips: { salaryProfileId: number; period: string }[],
): number | null {
  if (profiles.length === 0) return null;
  const ids = new Set(profiles.map((p) => p.id));
  let latest: { salaryProfileId: number; period: string } | null = null;
  for (const s of slips) {
    if (ids.has(s.salaryProfileId) && (!latest || s.period > latest.period)) latest = s;
  }
  return latest?.salaryProfileId ?? profiles[0].id;
}

/**
 * Index of the oldest slip shown in the desktop picker: the last `page` slips per page of
 * "Earlier", and never past the selected slip.
 */
export function firstShownIndex(
  total: number,
  selectedIndex: number,
  pages: number,
  page = 12,
): number {
  const byPage = Math.max(0, total - page * pages);
  return selectedIndex === -1 ? byPage : Math.min(byPage, selectedIndex);
}

export interface SlipChip {
  id: number;
  month: string;
  /** Shown on the first chip and on January's, so the months read in their years. */
  year: string | null;
  net: string;
  label: string;
  older: boolean;
  selected: boolean;
}

/** The slip picker's chips, oldest first. */
export function slipChips(
  timeline: SalarySlip[],
  selectedId: number | null,
  firstShown: number,
): SlipChip[] {
  return timeline.map((s, i) => {
    const key = periodKey(s.period);
    const net = eur(slipNet(s));
    return {
      id: s.id,
      month: monthName(key, 'short'),
      year: i === 0 || i === firstShown || key.endsWith('-01') ? key.slice(0, 4) : null,
      net,
      label: `${monthYearLabel(key)} slip, take-home ${net}`,
      older: i < firstShown,
      selected: s.id === selectedId,
    };
  });
}

export interface TakeHomeBar {
  id: number;
  month: string;
  year: string | null;
  amount: number;
  /** Height as a share of the tallest bar shown, 0–100. */
  pct: number;
  selected: boolean;
  title: string;
}

export interface TakeHomeHistory {
  bars: TakeHomeBar[];
  from: string;
  to: string;
  summary: string;
  label: string;
}

/**
 * "Take-home over time": one bar per slip, the last `count` slips, or the `count` ending with
 * the selected slip when it is older. `slips` is oldest first.
 */
export function takeHomeHistory(
  slips: SalarySlip[],
  selectedId: number | null,
  count = 12,
): TakeHomeHistory | null {
  if (slips.length === 0) return null;
  const at = slips.findIndex((s) => s.id === selectedId);
  const end =
    at >= 0 && at < slips.length - count
      ? Math.max(at + 1, Math.min(count, slips.length))
      : slips.length;
  const shown = slips.slice(Math.max(0, end - count), end);
  const nets = shown.map((s) => slipNet(s));
  const top = Math.max(0, ...nets);
  const bars = shown.map((s, i): TakeHomeBar => {
    const key = periodKey(s.period);
    return {
      id: s.id,
      month: monthName(key, 'short'),
      year: i === 0 || key.endsWith('-01') ? key.slice(0, 4) : null,
      amount: nets[i],
      pct: top > 0 ? (Math.max(0, nets[i]) / top) * 100 : 0,
      selected: s.id === selectedId,
      title: `${monthYearLabel(key)}: ${eur(nets[i])}`,
    };
  });
  const average = nets.reduce((a, b) => a + b, 0) / nets.length;
  const summary =
    nets.length === 1
      ? 'One slip so far.'
      : `Average ${eur(average)} a slip, from ${eur(Math.min(...nets))} to ${eur(Math.max(...nets))}.`;
  return {
    bars,
    from: shortPeriod(shown[0].period),
    to: shortPeriod(shown[shown.length - 1].period),
    summary,
    label: 'Take-home per slip: ' + bars.map((b) => b.title).join(', '),
  };
}
