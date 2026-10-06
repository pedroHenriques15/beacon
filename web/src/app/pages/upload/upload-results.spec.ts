import { describe, it, expect } from 'vitest';
import {
  BatchUploadItemResult,
  ParsedSlipResponse,
  TradesUploadResult,
  UnifiedUploadItemResult,
} from '../../core/models/statement.model';
import { GroceryReceiptUploadResult } from '../../core/models/grocery.model';
import {
  dayLabel,
  groupSummary,
  periodLabel,
  SalaryQueueItem,
  UploadOutcome,
  uploadGroups,
  uploadTally,
} from './upload-results';

function outcome(overrides: Partial<UploadOutcome>): UploadOutcome {
  return {
    statements: [],
    trades: [],
    slips: [],
    groceries: [],
    micro1: [],
    failed: [],
    ...overrides,
  };
}

function statement(overrides: Partial<BatchUploadItemResult>): BatchUploadItemResult {
  return {
    fileName: 'statement.pdf',
    success: true,
    result: {
      imported: true,
      bank: 'BPI',
      periodFrom: '2026-09-01',
      transactionCount: 19,
      unknownCount: 0,
      message: null,
    },
    error: null,
    ...overrides,
  };
}

function brokerExport(
  trades: Partial<TradesUploadResult> | null,
  overrides: Partial<UnifiedUploadItemResult> = {},
): UnifiedUploadItemResult {
  return {
    fileName: 'EUR_1_2026-05-31_2026-06-30.xlsx',
    documentType: 'BrokerExport',
    success: true,
    wasDuplicate: false,
    error: null,
    statementResult: null,
    groceryResult: null,
    salaryResult: null,
    tradesResult: trades && {
      broker: 'XTB',
      periodFrom: '2026-06-01',
      periodTo: '2026-06-30',
      tradeCount: 4,
      added: 4,
      warnings: [],
      ...trades,
    },
    ...overrides,
  };
}

function receipt(overrides: Partial<GroceryReceiptUploadResult>): GroceryReceiptUploadResult {
  return {
    receiptId: 1,
    storeName: 'Continente',
    receiptDate: '2026-09-14',
    total: 45.2,
    itemCount: 12,
    wasDuplicate: false,
    newReceiptCategories: [],
    ...overrides,
  };
}

const parsedSlip: ParsedSlipResponse = {
  parserName: 'Test',
  employer: 'Acme',
  employerNif: null,
  period: '2026-09-01',
  grossAmount: 2980,
  netAmount: 2100,
  lineItems: [],
  baseAmount: null,
  hoursWorked: null,
  hourlyRate: null,
  totalEspecie: null,
};

describe('dayLabel and periodLabel', () => {
  it('formats dates the way the timeline reads them', () => {
    expect(dayLabel('2026-09-14')).toBe('14 Sep 2026');
    expect(dayLabel('2026-04-03T00:00:00')).toBe('3 Apr 2026');
    expect(periodLabel('2026-09-01')).toBe('September 2026');
  });

  it('passes anything that is not a date through', () => {
    expect(dayLabel('')).toBe('');
    expect(periodLabel('soon')).toBe('soon');
  });
});

describe('uploadGroups', () => {
  it('leaves out kinds the upload had none of', () => {
    expect(uploadGroups(outcome({}))).toEqual([]);
    const groups = uploadGroups(outcome({ groceries: [receipt({})] }));
    expect(groups.map((g) => g.key)).toEqual(['groceries']);
  });

  it('names an imported statement by bank and month, with its count', () => {
    const [group] = uploadGroups(
      outcome({
        statements: [
          statement({}),
          statement({
            fileName: 'again.pdf',
            success: false,
            result: { ...statement({}).result!, message: 'Already imported' },
          }),
          statement({ fileName: 'broken.pdf', success: false, result: null, error: 'No bank' }),
        ],
      }),
    );

    expect(group.title).toBe('Bank statements');
    expect(group.entries.map((e) => e.tone)).toEqual(['ok', 'duplicate', 'error']);
    expect(group.entries[0].title).toBe('BPI statement, September 2026');
    expect(group.entries[0].meta).toBe('19 transactions added');
    expect(group.entries[0].fileName).toBe('statement.pdf');
    expect(group.entries[1].meta).toBe('Already imported');
    expect(group.entries[2].meta).toBe('No bank');
    expect(group.summary).toBe('1 imported, 1 already in Beacon, 1 failed');
  });

  it('names a broker export by broker and month, with the trades it added to Invest', () => {
    const [group] = uploadGroups(
      outcome({
        trades: [
          brokerExport({}),
          brokerExport({ added: 2, warnings: ['SXR8.DE: XTB lists 3 held'] }),
          brokerExport({ added: 0 }, { success: false, wasDuplicate: true }),
          brokerExport({ tradeCount: 0, added: 0, periodFrom: '2026-07-01' }),
          brokerExport(null, { success: false, error: 'Cannot sell 2 VWCE.DE' }),
        ],
      }),
    );

    expect(group.key).toBe('trades');
    expect(group.title).toBe('Investment trades');
    expect(group.entries.map((e) => e.tone)).toEqual(['ok', 'ok', 'duplicate', 'ok', 'error']);
    expect(group.entries[0].title).toBe('XTB trades, June 2026');
    expect(group.entries[0].meta).toBe('4 trades added to Invest');
    expect(group.entries[0].fileName).toBe('EUR_1_2026-05-31_2026-06-30.xlsx');
    expect(group.entries[1].meta).toBe(
      '2 of 4 trades added to Invest, the rest were there already',
    );
    expect(group.entries[1].warnings).toEqual(['SXR8.DE: XTB lists 3 held']);
    expect(group.entries[2].meta).toBe('Already in Invest, 4 trades');
    expect(group.entries[3].title).toBe('XTB trades, July 2026');
    expect(group.entries[3].meta).toBe('No trades in this period');
    expect(group.entries[4].title).toBe('EUR_1_2026-05-31_2026-06-30.xlsx');
    expect(group.entries[4].meta).toBe('Cannot sell 2 VWCE.DE');
    expect(group.summary).toBe('3 added, 1 already in Beacon, 1 failed');
  });

  it('lists broker exports right after the statements', () => {
    const groups = uploadGroups(
      outcome({
        groceries: [receipt({})],
        trades: [brokerExport({})],
        statements: [statement({})],
      }),
    );
    expect(groups.map((g) => g.key)).toEqual(['statements', 'trades', 'groceries']);
  });

  it('says how many transactions of a statement need a category', () => {
    const base = statement({}).result!;
    const [group] = uploadGroups(
      outcome({ statements: [statement({ result: { ...base, unknownCount: 3 } })] }),
    );
    expect(group.entries[0].meta).toBe('19 transactions added, 3 need a category');
  });

  it('offers Review and save for a read slip, and for one whose review failed to open', () => {
    const slips: SalaryQueueItem[] = [
      { status: 'ready', fileName: 'a.pdf', pdfPath: 'a', parsed: parsedSlip },
      { status: 'saved', fileName: 'b.pdf', pdfPath: 'b', parsed: parsedSlip },
      { status: 'error', fileName: 'c.pdf', pdfPath: 'c', parsed: parsedSlip, error: 'Retry' },
      { status: 'error', fileName: 'd.pdf', error: 'Unreadable' },
    ];
    const [group] = uploadGroups(outcome({ slips }));

    expect(group.entries.map((e) => e.slipIndex)).toEqual([0, null, 2, null]);
    expect(group.entries.map((e) => e.action)).toEqual([
      'Review and save',
      null,
      'Review and save',
      null,
    ]);
    expect(group.entries[0].title).toBe('Acme slip, September 2026');
    expect(group.entries[0].amount).toBe(2980);
    expect(group.entries[1].meta).toBe('Saved to Salary');
    expect(group.entries[3].title).toBe('d.pdf');
    expect(group.note).toBe('Review and save each slip before it is recorded.');
    expect(group.summary).toBe('1 to review, 1 saved, 2 failed');
  });

  it('asks for the EUR received of a Mercor statement before its review', () => {
    const mercor = {
      pdfPath: 'm',
      fileName: 'mercor.pdf',
      period: '2026-08-01',
      totalPayUsd: 145.17,
      hoursWorked: 3.6,
      payRateUsd: 40,
      suggestedEur: null,
      payouts: [],
    };
    const [group] = uploadGroups(
      outcome({
        slips: [
          { status: 'needs-eur', fileName: 'mercor.pdf', pdfPath: 'm', mercor },
          {
            status: 'ready',
            fileName: 'mercor.pdf',
            pdfPath: 'm',
            mercor,
            parsed: { ...parsedSlip, employer: 'Mercor', period: '2026-08-01' },
          },
        ],
      }),
    );

    const [waiting, converted] = group.entries;
    expect(waiting.tone).toBe('review');
    expect(waiting.title).toBe('Mercor statement, August 2026');
    expect(waiting.meta).toBe('$145.17 for 3.60 h, waiting for the EUR received');
    expect(waiting.fileName).toBe('mercor.pdf');
    expect(waiting.amount).toBeNull();
    expect([waiting.slipIndex, waiting.action]).toEqual([0, 'Enter EUR received']);
    expect(converted.title).toBe('Mercor slip, August 2026');
    expect(converted.action).toBe('Review and save');
    expect(group.summary).toBe('2 to review');
  });

  it('drops the review note once every slip is saved', () => {
    const [group] = uploadGroups(
      outcome({ slips: [{ status: 'saved', fileName: 'a.pdf', parsed: parsedSlip }] }),
    );
    expect(group.note).toBeNull();
  });

  it('lists receipts with their store, date and total', () => {
    const [group] = uploadGroups(
      outcome({ groceries: [receipt({}), receipt({ receiptId: 1, wasDuplicate: true })] }),
    );
    expect(group.entries[0].title).toBe('Continente receipt, 14 Sep 2026');
    expect(group.entries[0].meta).toBe('12 items added to Groceries');
    expect(group.entries[0].amount).toBe(45.2);
    expect(group.entries[1].tone).toBe('duplicate');
    expect(group.entries.map((e) => e.key)).toEqual(['grocery-0', 'grocery-1']);
    expect(group.summary).toBe('1 added, 1 already in Beacon');
  });

  it('keeps unpaired micro1 files apart from failures, with the pairing rule', () => {
    const groups = uploadGroups(
      outcome({
        micro1: [{ fileName: 'invoice.pdf', error: 'Missing its Deel withdrawal' }],
        failed: [{ fileName: 'menu.pdf', error: 'Unknown document' }],
      }),
    );
    expect(groups.map((g) => g.key)).toEqual(['micro1', 'failed']);
    expect(groups[0].entries[0].tone).toBe('waiting');
    expect(groups[0].note).toContain('Deel withdrawal');
    expect(groups[0].summary).toBe('1 not imported');
    expect(groups[1].entries[0].tone).toBe('error');
  });
});

describe('groupSummary', () => {
  it('is empty with no entries', () => {
    expect(groupSummary([])).toBe('');
  });
});

describe('uploadTally', () => {
  it('counts every file once', () => {
    const groups = uploadGroups(
      outcome({
        statements: [statement({}), statement({ success: false, result: null, error: 'x' })],
        slips: [{ status: 'ready', fileName: 'a.pdf', pdfPath: 'a', parsed: parsedSlip }],
        groceries: [receipt({ wasDuplicate: true })],
        micro1: [{ fileName: 'i.pdf', error: 'alone' }],
      }),
    );
    expect(uploadTally(groups)).toEqual({
      files: 5,
      imported: 1,
      duplicates: 1,
      review: 1,
      failed: 2,
    });
  });
});
