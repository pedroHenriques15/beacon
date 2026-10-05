import { describe, it, expect } from 'vitest';
import { SalaryLineItem, SalarySlip } from '../../core/models/statement.model';
import {
  defaultProfileId,
  effectiveRate,
  firstShownIndex,
  lineDetail,
  periodKey,
  slipChips,
  slipLines,
  slipNet,
  slipTiles,
  takeHomeHistory,
  trueHourlyRate,
  workdaysInMonth,
} from './salary-figures';

let nextId = 1;

function item(overrides: Partial<SalaryLineItem>): SalaryLineItem {
  return {
    id: nextId++,
    salaryItemCategoryId: 1,
    categoryName: 'Base salary',
    categoryColor: '#123456',
    categoryItemType: 'income',
    amount: 1000,
    sortOrder: 0,
    quantity: null,
    unitValue: null,
    percentage: null,
    incidenciaBase: null,
    ...overrides,
  };
}

function slip(overrides: Partial<SalarySlip>): SalarySlip {
  return {
    id: nextId++,
    salaryProfileId: 1,
    profileName: 'Main job',
    period: '2026-09-01',
    grossAmount: 1000,
    netAmount: 800,
    notes: null,
    pdfPath: null,
    sourceFile: null,
    importedAt: '2026-10-02T10:00:00',
    lineItems: [],
    baseAmount: null,
    hoursWorked: null,
    hourlyRate: null,
    totalEspecie: null,
    ...overrides,
  };
}

const lineItems = [
  item({ categoryName: 'Base salary', amount: 1400, sortOrder: 1 }),
  item({
    categoryName: 'Meal allowance',
    amount: 167.86,
    sortOrder: 2,
    quantity: 22,
    unitValue: 7.63,
  }),
  item({
    categoryName: 'Social security',
    categoryItemType: 'deduction',
    amount: 172.46,
    sortOrder: 3,
    percentage: 11,
    incidenciaBase: 1567.86,
  }),
  item({ categoryName: 'IRS', categoryItemType: 'tax', amount: 252, sortOrder: 4 }),
];

describe('slipNet', () => {
  it('is income less deductions and tax when the slip has line items', () => {
    expect(slipNet(slip({ lineItems }))).toBeCloseTo(1143.4, 2);
  });

  it('is the slip’s own net without line items', () => {
    expect(slipNet(slip({ netAmount: 812.5 }))).toBe(812.5);
  });
});

describe('effectiveRate', () => {
  it('is the share of the gross that is withheld', () => {
    expect(effectiveRate(slip({ grossAmount: 1000, netAmount: 774 }))).toBeCloseTo(22.6, 5);
  });

  it('is null without a gross', () => {
    expect(effectiveRate(slip({ grossAmount: 0 }))).toBeNull();
  });
});

describe('workdaysInMonth', () => {
  it('counts Monday to Friday', () => {
    expect(workdaysInMonth('2026-09-01')).toBe(22);
    expect(workdaysInMonth('2026-02-01')).toBe(20);
  });
});

describe('trueHourlyRate', () => {
  const s = slip({ netAmount: 1600, hoursWorked: 20 });

  it('divides by hours worked', () => {
    expect(trueHourlyRate(s, 'hours')).toBe(80);
  });

  it('divides by days worked of eight hours', () => {
    expect(trueHourlyRate(s, 'days')).toBe(10);
  });

  it('divides by the month’s weekdays of eight hours', () => {
    expect(trueHourlyRate(slip({ netAmount: 1760 }), 'workdays')).toBe(10);
  });

  it('is null without hours for the hour and day formulas', () => {
    expect(trueHourlyRate(slip({ hoursWorked: null }), 'hours')).toBeNull();
    expect(trueHourlyRate(slip({ hoursWorked: 0 }), 'days')).toBeNull();
  });
});

describe('slipTiles', () => {
  it('shows only the figures the slip has', () => {
    const tiles = slipTiles(slip({ grossAmount: 1000, netAmount: 774 }), 'days', 3, '2026-07-01');
    expect(tiles.map((t) => t.label)).toEqual(['Effective rate', 'Slips']);
    expect(tiles[0].value).toBe('22.6%');
    expect(tiles[0].note).toBe('€226.00 deductions and tax');
    expect(tiles[1]).toEqual({ label: 'Slips', value: '3', note: 'Since Jul 2026' });
  });

  it('names hours or days by the profile’s formula', () => {
    const s = slip({ hoursWorked: 168, baseAmount: 900, hourlyRate: 5.36, totalEspecie: 42 });
    expect(slipTiles(s, 'hours', 1, null).map((t) => t.label)).toEqual([
      'Effective rate',
      'Base salary',
      'Hours worked',
      'Hourly rate',
      'True hourly rate',
      'Non-cash benefits',
      'Slips',
    ]);
    expect(slipTiles(s, 'hours', 1, null)[2].value).toBe('168 h');
    expect(slipTiles(s, 'days', 1, null)[2]).toEqual({
      label: 'Days worked',
      value: '168',
      note: 'September',
    });
  });
});

describe('lineDetail', () => {
  it('reads the type, quantity and rate of a line', () => {
    expect(lineDetail(lineItems[1])).toBe('Income, 22 × €7.63');
    expect(lineDetail(lineItems[2])).toBe('Deduction, 11.0% of €1,567.86');
    expect(lineDetail(lineItems[3])).toBe('Tax');
  });
});

describe('slipLines', () => {
  it('splits income from deductions and tax, signed', () => {
    const lines = slipLines(slip({ lineItems: [...lineItems].reverse() }));
    expect(lines.incomes.map((l) => l.name)).toEqual(['Base salary', 'Meal allowance']);
    expect(lines.outgoings.map((l) => l.name)).toEqual(['Social security', 'IRS']);
    expect(lines.incomes[0].amount).toBe('+€1,400.00');
    expect(lines.incomes[0].inflow).toBe(true);
    expect(lines.outgoings[0].amount).toBe('−€172.46');
    expect(lines.outgoings[0].inflow).toBe(false);
  });

  it('leaves the colour to the fallback when a category has none', () => {
    const lines = slipLines(slip({ lineItems: [item({ categoryColor: '' })] }));
    expect(lines.incomes[0].color).toBeNull();
  });
});

describe('defaultProfileId', () => {
  it('picks the profile with the most recent slip', () => {
    const profiles = [{ id: 1 }, { id: 2 }];
    const slips = [
      { salaryProfileId: 1, period: '2026-08-01' },
      { salaryProfileId: 2, period: '2026-09-01' },
    ];
    expect(defaultProfileId(profiles, slips)).toBe(2);
  });

  it('falls back to the first profile, and to null without profiles', () => {
    expect(defaultProfileId([{ id: 4 }, { id: 5 }], [])).toBe(4);
    expect(defaultProfileId([], [{ salaryProfileId: 1, period: '2026-09-01' }])).toBeNull();
  });

  it('ignores slips of a profile that is gone', () => {
    const slips = [
      { salaryProfileId: 9, period: '2026-09-01' },
      { salaryProfileId: 2, period: '2026-01-01' },
    ];
    expect(defaultProfileId([{ id: 1 }, { id: 2 }], slips)).toBe(2);
  });
});

describe('firstShownIndex', () => {
  it('shows the last page of slips', () => {
    expect(firstShownIndex(30, 29, 1)).toBe(18);
    expect(firstShownIndex(30, 29, 2)).toBe(6);
    expect(firstShownIndex(5, 4, 1)).toBe(0);
  });

  it('always shows the selected slip', () => {
    expect(firstShownIndex(30, 3, 1)).toBe(3);
  });
});

describe('slipChips', () => {
  it('labels each slip with its month, year where it changes, and take-home', () => {
    const timeline = [
      slip({ id: 101, period: '2025-12-01', netAmount: 900 }),
      slip({ id: 102, period: '2026-01-01', netAmount: 950 }),
      slip({ id: 103, period: '2026-02-01', netAmount: 950 }),
    ];
    const chips = slipChips(timeline, 103, 0);
    expect(chips.map((c) => [c.month, c.year])).toEqual([
      ['Dec', '2025'],
      ['Jan', '2026'],
      ['Feb', null],
    ]);
    expect(chips[2].selected).toBe(true);
    expect(chips[2].net).toBe('€950.00');
    expect(chips[2].label).toBe('February 2026 slip, take-home €950.00');
  });

  it('marks the slips before the first shown as older', () => {
    const timeline = [slip({ period: '2026-01-01' }), slip({ period: '2026-02-01' })];
    expect(slipChips(timeline, null, 1).map((c) => c.older)).toEqual([true, false]);
  });
});

describe('takeHomeHistory', () => {
  const months = Array.from({ length: 15 }, (_, i) =>
    slip({
      id: 200 + i,
      period: `${2025 + Math.floor((i + 6) / 12)}-${String(((i + 6) % 12) + 1).padStart(2, '0')}-01`,
      netAmount: 1000 + i * 10,
    }),
  );

  it('is null without slips', () => {
    expect(takeHomeHistory([], null)).toBeNull();
  });

  it('shows the last twelve slips, scaled to the tallest', () => {
    const h = takeHomeHistory(months, 214)!;
    expect(h.bars.length).toBe(12);
    expect(h.bars[0].id).toBe(203);
    expect(h.bars[11].pct).toBe(100);
    expect(h.bars[11].selected).toBe(true);
    expect(h.from).toBe('Oct 2025');
    expect(h.to).toBe('Sep 2026');
  });

  it('moves back to include an older selected slip', () => {
    const h = takeHomeHistory(months, 201)!;
    expect(h.bars.map((b) => b.id)).toEqual(months.slice(0, 12).map((s) => s.id));
    expect(h.bars.find((b) => b.selected)?.id).toBe(201);
  });

  it('summarises the take-home shown', () => {
    const h = takeHomeHistory(months.slice(0, 2), null)!;
    expect(h.summary).toBe('Average €1,005.00 a slip, from €1,000.00 to €1,010.00.');
    expect(takeHomeHistory(months.slice(0, 1), null)!.summary).toBe('One slip so far.');
  });

  it('labels the year on the first bar and on January', () => {
    const h = takeHomeHistory(months, 214)!;
    expect(h.bars.filter((b) => b.year).map((b) => `${b.month} ${b.year}`)).toEqual([
      'Oct 2025',
      'Jan 2026',
    ]);
  });
});

describe('periodKey', () => {
  it('takes the month of a period', () => {
    expect(periodKey('2026-09-01')).toBe('2026-09');
    expect(periodKey('2026-09-01T00:00:00')).toBe('2026-09');
  });
});
