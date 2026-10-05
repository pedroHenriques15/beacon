import { SalarySlip } from '../../core/models/statement.model';
import { eur } from '../../core/utils/money';
import { slipNet } from './salary-figures';

/** An income line, the take-home, or a deduction or tax line. */
export type FlowKind = 'in' | 'net' | 'out';

export interface FlowPart {
  name: string;
  amount: number;
  kind: FlowKind;
}

export interface FlowParts {
  /** The slip's gross, for the label on the middle bar. */
  gross: number;
  /** Income lines, drawn on the left into the gross bar. */
  inputs: FlowPart[];
  /** The take-home first, then each deduction and tax line. */
  outputs: FlowPart[];
}

/**
 * What a slip's flow shows: its income lines flowing into the gross, and the gross splitting into
 * take-home and each deduction and tax line. Without line items, the gross splits into the
 * slip's own net and the rest.
 */
export function slipFlowParts(
  slip: Pick<SalarySlip, 'lineItems' | 'grossAmount' | 'netAmount'>,
): FlowParts {
  const items = [...(slip.lineItems ?? [])].sort((a, b) => a.sortOrder - b.sortOrder);
  if (items.length === 0) {
    const outputs: FlowPart[] = [
      { name: 'Take-home', amount: slip.netAmount, kind: 'net' },
      { name: 'Deductions and tax', amount: slip.grossAmount - slip.netAmount, kind: 'out' },
    ];
    return { gross: slip.grossAmount, inputs: [], outputs: outputs.filter((p) => p.amount > 0) };
  }
  const inputs: FlowPart[] = items
    .filter((li) => li.categoryItemType === 'income' && li.amount > 0)
    .map((li): FlowPart => ({ name: li.categoryName, amount: li.amount, kind: 'in' }));
  const withheld: FlowPart[] = items
    .filter((li) => li.categoryItemType !== 'income' && li.amount > 0)
    .map((li): FlowPart => ({ name: li.categoryName, amount: li.amount, kind: 'out' }));
  const net = slipNet(slip);
  const outputs: FlowPart[] =
    net > 0 ? [{ name: 'Take-home', amount: net, kind: 'net' }, ...withheld] : withheld;
  return { gross: slip.grossAmount, inputs, outputs };
}

/** 'Base salary €1,400.00 and … make €1,567.86 gross, which splits into Take-home …'. */
export function flowSummary(parts: FlowParts): string {
  const outs = parts.outputs.map((p) => `${p.name} ${eur(p.amount)}`).join(', ');
  if (parts.inputs.length === 0) return `${eur(parts.gross)} gross splits into ${outs}`;
  const ins = parts.inputs.map((p) => `${p.name} ${eur(p.amount)}`).join(' and ');
  return `${ins} make ${eur(parts.gross)} gross, which splits into ${outs}`;
}

export interface FlowNode {
  name: string;
  value: string;
  kind: FlowKind;
  x: number;
  y: number;
  h: number;
  /** Vertical centre of the node's label. */
  mid: number;
  /** Where the label starts (outputs) or ends (inputs). */
  labelX: number;
  ribbon: string;
}

export interface FlowLayout {
  width: number;
  height: number;
  /** Narrow: no income column, smaller type, as on a phone. */
  compact: boolean;
  nodeWidth: number;
  labelWidth: number;
  gross: {
    x: number;
    y: number;
    h: number;
    value: string;
    labelX: number;
    labelY: number;
    /** Above the bar (with income lines), beside it (without), or in the corner (compact). */
    place: 'above' | 'beside' | 'corner';
  };
  inputs: FlowNode[];
  outputs: FlowNode[];
}

/** Below this width the flow drops its income column. */
export const FLOW_COMPACT_BELOW = 600;

const round = (v: number) => Math.round(v * 10) / 10;
const sum = (parts: FlowPart[]) => parts.reduce((s, p) => s + p.amount, 0);

/** A band from the segment a1–b1 at x1 to the segment a2–b2 at x2. */
function ribbon(x1: number, a1: number, b1: number, x2: number, a2: number, b2: number): string {
  const xm = round((x1 + x2) / 2);
  const [p1, q1, p2, q2] = [a1, b1, a2, b2].map(round);
  return (
    `M${round(x1)} ${p1}C${xm} ${p1} ${xm} ${p2} ${round(x2)} ${p2}` +
    `L${round(x2)} ${q2}C${xm} ${q2} ${xm} ${q1} ${round(x1)} ${q1}Z`
  );
}

/**
 * Stacks nodes from `top`, `gap` apart, pushing a node down when its label would sit closer than
 * `spacing` to the one above.
 */
function stack(parts: FlowPart[], scale: number, top: number, gap: number, spacing: number) {
  let prevMid = -Infinity;
  let nextY = top;
  return parts.map((p) => {
    const h = p.amount * scale;
    let y = nextY;
    if (y + h / 2 < prevMid + spacing) y = prevMid + spacing - h / 2;
    prevMid = y + h / 2;
    nextY = y + h + gap;
    return { y, h, mid: prevMid };
  });
}

/**
 * The flow drawn at `width` pixels: income nodes on the left, the gross bar, then take-home and
 * the deductions on the right, joined by bands as thick as their amounts.
 */
export function flowLayout(parts: FlowParts, width: number): FlowLayout {
  const compact = width < FLOW_COMPACT_BELOW;
  const inputs = compact ? [] : parts.inputs;
  const nodeWidth = compact ? 12 : 14;
  const top = compact ? 26 : 40;
  const barH = compact ? 190 : 280;
  const gap = compact ? 12 : 18;
  const spacing = compact ? 38 : 46;
  const labelWidth = compact ? Math.min(150, Math.round(width * 0.31)) : 150;
  const scale = barH / Math.max(sum(parts.inputs), sum(parts.outputs), 0.01);

  const inX = labelWidth + 12;
  const outX = width - labelWidth - (compact ? 10 : 12) - nodeWidth;
  const grossX = compact
    ? 0
    : inputs.length > 0
      ? Math.round((inX + nodeWidth + outX) / 2 - nodeWidth / 2)
      : inX;

  let at = top;
  const inPlaces = stack(inputs, scale, top, gap, spacing);
  const inNodes = inputs.map((p, i): FlowNode => {
    const { y, h, mid } = inPlaces[i];
    const node: FlowNode = {
      name: p.name,
      value: eur(p.amount),
      kind: p.kind,
      x: inX,
      y: round(y),
      h: round(h),
      mid: round(mid),
      labelX: inX - 12,
      ribbon: ribbon(inX + nodeWidth, y, y + h, grossX, at, at + h),
    };
    at += h;
    return node;
  });

  at = top;
  const outPlaces = stack(parts.outputs, scale, top, gap, spacing);
  const outNodes = parts.outputs.map((p, i): FlowNode => {
    const { y, h, mid } = outPlaces[i];
    const node: FlowNode = {
      name: p.name,
      value: (p.kind === 'out' ? '−' : '') + eur(p.amount),
      kind: p.kind,
      x: outX,
      y: round(y),
      h: round(h),
      mid: round(mid),
      labelX: outX + nodeWidth + 10,
      ribbon: ribbon(grossX + nodeWidth, at, at + h, outX, y, y + h),
    };
    at += h;
    return node;
  });

  const bottom = Math.max(
    top + barH,
    ...[...inNodes, ...outNodes].map((n) => Math.max(n.y + n.h, n.mid + spacing / 2)),
  );

  let label: Pick<FlowLayout['gross'], 'labelX' | 'labelY' | 'place'>;
  if (compact) label = { labelX: 0, labelY: 0, place: 'corner' };
  else if (inputs.length > 0)
    label = { labelX: grossX + nodeWidth / 2, labelY: top - 8, place: 'above' };
  else label = { labelX: grossX - 12, labelY: top + barH / 2, place: 'beside' };

  return {
    width,
    height: Math.ceil(bottom + 8),
    compact,
    nodeWidth,
    labelWidth,
    gross: { x: grossX, y: top, h: barH, value: eur(parts.gross), ...label },
    inputs: inNodes,
    outputs: outNodes,
  };
}
