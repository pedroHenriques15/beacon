import { describe, it, expect } from 'vitest';
import { SalaryLineItem } from '../../core/models/statement.model';
import { FlowParts, flowLayout, flowSummary, slipFlowParts } from './salary-flow';

let nextId = 1;

function item(
  categoryName: string,
  categoryItemType: string,
  amount: number,
  sortOrder: number,
): SalaryLineItem {
  return {
    id: nextId++,
    salaryItemCategoryId: nextId,
    categoryName,
    categoryColor: '#123456',
    categoryItemType,
    amount,
    sortOrder,
    quantity: null,
    unitValue: null,
    percentage: null,
    incidenciaBase: null,
  };
}

const slip = {
  grossAmount: 3424.4,
  netAmount: 2650,
  lineItems: [
    item('IRS withholding', 'tax', 422.4, 4),
    item('Base salary', 'income', 3200, 1),
    item('Meal allowance', 'income', 224.4, 2),
    item('Social security', 'deduction', 352, 3),
  ],
};

describe('slipFlowParts', () => {
  it('flows the income lines into the gross, and the gross into take-home and deductions', () => {
    const parts = slipFlowParts(slip);
    expect(parts.gross).toBe(3424.4);
    expect(parts.inputs.map((p) => p.name)).toEqual(['Base salary', 'Meal allowance']);
    expect(parts.outputs.map((p) => [p.name, p.kind])).toEqual([
      ['Take-home', 'net'],
      ['Social security', 'out'],
      ['IRS withholding', 'out'],
    ]);
    expect(parts.outputs[0].amount).toBeCloseTo(2650, 2);
  });

  it('splits the gross into the slip’s own net and the rest without line items', () => {
    const parts = slipFlowParts({ grossAmount: 440, netAmount: 330, lineItems: [] });
    expect(parts.inputs).toEqual([]);
    expect(parts.outputs).toEqual([
      { name: 'Take-home', amount: 330, kind: 'net' },
      { name: 'Deductions and tax', amount: 110, kind: 'out' },
    ]);
  });

  it('leaves out a take-home that is not positive', () => {
    const parts = slipFlowParts({
      grossAmount: 100,
      netAmount: 0,
      lineItems: [item('Pay', 'income', 100, 1), item('Advance', 'deduction', 100, 2)],
    });
    expect(parts.outputs.map((p) => p.name)).toEqual(['Advance']);
  });
});

describe('flowSummary', () => {
  it('reads the flow in a sentence', () => {
    expect(flowSummary(slipFlowParts({ grossAmount: 440, netAmount: 330, lineItems: [] }))).toBe(
      '€440.00 gross splits into Take-home €330.00, Deductions and tax €110.00',
    );
    expect(flowSummary(slipFlowParts(slip))).toBe(
      'Base salary €3,200.00 and Meal allowance €224.40 make €3,424.40 gross, which splits into ' +
        'Take-home €2,650.00, Social security €352.00, IRS withholding €422.40',
    );
  });
});

describe('flowLayout', () => {
  const parts = slipFlowParts(slip);

  it('draws the income column on a wide flow', () => {
    const l = flowLayout(parts, 820);
    expect(l.compact).toBe(false);
    expect(l.inputs.length).toBe(2);
    expect(l.gross.place).toBe('above');
    // Income on the left, gross in between, take-home and deductions on the right.
    expect(l.inputs[0].x).toBeLessThan(l.gross.x);
    expect(l.gross.x).toBeLessThan(l.outputs[0].x);
    expect(l.outputs[0].x + l.nodeWidth + 10 + l.labelWidth).toBeLessThanOrEqual(l.width);
  });

  it('drops the income column when narrow', () => {
    const l = flowLayout(parts, 322);
    expect(l.compact).toBe(true);
    expect(l.inputs).toEqual([]);
    expect(l.gross.x).toBe(0);
    expect(l.gross.place).toBe('corner');
    expect(l.outputs[0].x).toBe(200);
  });

  it('puts the gross label beside the bar when there are no income lines', () => {
    const l = flowLayout(slipFlowParts({ grossAmount: 440, netAmount: 330, lineItems: [] }), 820);
    expect(l.gross.place).toBe('beside');
    expect(l.gross.labelX).toBeLessThan(l.gross.x);
  });

  it('makes each band as thick as its amount, filling the gross bar', () => {
    const l = flowLayout(parts, 820);
    const total = l.outputs.reduce((s, n) => s + n.h, 0);
    expect(total).toBeCloseTo(l.gross.h, 0);
    expect(l.outputs[0].h / l.outputs[1].h).toBeCloseTo(2650 / 352, 1);
  });

  it('keeps labels apart even for tiny lines', () => {
    const tiny: FlowParts = {
      gross: 1000,
      inputs: [],
      outputs: [
        { name: 'Take-home', amount: 990, kind: 'net' },
        { name: 'Union', amount: 4, kind: 'out' },
        { name: 'Fund', amount: 3, kind: 'out' },
        { name: 'Fee', amount: 3, kind: 'out' },
      ],
    };
    for (const width of [322, 820]) {
      const l = flowLayout(tiny, width);
      const spacing = l.compact ? 38 : 46;
      for (let i = 1; i < l.outputs.length; i++) {
        expect(l.outputs[i].mid - l.outputs[i - 1].mid).toBeGreaterThanOrEqual(spacing - 0.2);
      }
      const last = l.outputs[l.outputs.length - 1];
      expect(l.height).toBeGreaterThanOrEqual(last.mid + spacing / 2);
    }
  });

  it('starts each band where the one above it ends on the gross bar', () => {
    const l = flowLayout(parts, 820);
    const starts = l.outputs.map((n) => Number(n.ribbon.slice(1).split(/[ C]/)[1]));
    expect(starts[0]).toBeCloseTo(l.gross.y, 1);
    expect(starts[1]).toBeCloseTo(l.gross.y + l.outputs[0].h, 0);
  });
});
