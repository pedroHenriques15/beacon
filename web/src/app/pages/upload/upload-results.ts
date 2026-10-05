import { BatchUploadItemResult, ParsedSlipResponse } from '../../core/models/statement.model';
import { GroceryReceiptUploadResult } from '../../core/models/grocery.model';
import { monthName, monthYearLabel } from '../../core/utils/month-totals';

/** A salary slip found in an upload, waiting to be reviewed and saved. */
export interface SalaryQueueItem {
  file?: File;
  status: 'pending' | 'uploading' | 'parsing' | 'ready' | 'saved' | 'error';
  pdfPath?: string;
  fileName?: string;
  parsed?: ParsedSlipResponse;
  error?: string;
}

/** A file the upload recognised but did not import. */
export interface FailedFile {
  fileName: string;
  error: string;
}

/**
 * What happened to a file: imported, already in Beacon, waiting for the owner's review, still
 * being read, waiting for its micro1 counterpart, or failed.
 */
export type FileTone = 'ok' | 'duplicate' | 'review' | 'working' | 'waiting' | 'error';

/** One file on the results timeline. */
export interface FileEntry {
  key: string;
  tone: FileTone;
  title: string;
  meta: string;
  /** The file's name, when the title says something else. */
  fileName: string | null;
  /** A receipt's total or a slip's gross pay. */
  amount: number | null;
  warnings: string[];
  /** The salary queue index the "Review and save" action opens, if it has one. */
  slipIndex: number | null;
}

export interface FileGroup {
  key: 'statements' | 'slips' | 'groceries' | 'micro1' | 'failed';
  title: string;
  /** '2 imported, 1 already in Beacon'. */
  summary: string;
  note: string | null;
  entries: FileEntry[];
}

export interface UploadOutcome {
  statements: BatchUploadItemResult[];
  slips: SalaryQueueItem[];
  groceries: GroceryReceiptUploadResult[];
  micro1: FailedFile[];
  failed: FailedFile[];
}

export interface UploadTally {
  files: number;
  imported: number;
  duplicates: number;
  review: number;
  failed: number;
}

const TONE_LABELS: Record<FileTone, string> = {
  ok: 'imported',
  duplicate: 'already in Beacon',
  review: 'to review',
  working: 'being read',
  waiting: 'waiting for a match',
  error: 'failed',
};

const SLIP_PROGRESS: Partial<Record<SalaryQueueItem['status'], string>> = {
  pending: 'Waiting…',
  uploading: 'Uploading…',
  parsing: 'Extracting text…',
};

export const MICRO1_NOTE =
  'A micro1 paycheck needs both PDFs, the invoice and its Deel withdrawal, uploaded together. ' +
  'These were recognised but not imported.';

/** '2026-09-14' → '14 Sep 2026'; anything else comes back as it is. */
export function dayLabel(date: string): string {
  const match = /^(\d{4})-(\d{2})-(\d{2})/.exec(date ?? '');
  if (!match) return date ?? '';
  const [, y, m, d] = match;
  return `${Number(d)} ${monthName(`${y}-${m}`, 'short')} ${y}`;
}

/** '2026-09-01' → 'September 2026'. */
export function periodLabel(date: string): string {
  return /^\d{4}-\d{2}/.test(date ?? '') ? monthYearLabel(date.slice(0, 7)) : (date ?? '');
}

function count(n: number, one: string, many: string): string {
  return `${n} ${n === 1 ? one : many}`;
}

function statementEntry(item: BatchUploadItemResult, i: number): FileEntry {
  const r = item.result;
  const base = {
    key: `statement-${i}`,
    amount: null,
    warnings: r?.warnings ?? [],
    slipIndex: null,
  };
  if (item.success && r) {
    const unknown = r.unknownCount > 0 ? `, ${r.unknownCount} need a category` : '';
    return {
      ...base,
      tone: 'ok',
      title: `${r.bank} statement, ${periodLabel(r.periodFrom)}`,
      meta: `${count(r.transactionCount, 'transaction', 'transactions')} added${unknown}`,
      fileName: item.fileName,
    };
  }
  if (r) {
    return {
      ...base,
      tone: 'duplicate',
      title: item.fileName,
      meta: r.message ?? 'Already imported',
      fileName: null,
    };
  }
  return {
    ...base,
    tone: 'error',
    title: item.fileName,
    meta: item.error ?? 'Import failed.',
    fileName: null,
  };
}

function slipEntry(item: SalaryQueueItem, i: number): FileEntry {
  const fileName = item.fileName ?? item.file?.name ?? '';
  const p = item.parsed;
  const base = {
    key: `slip-${i}`,
    title: p ? `${p.employer?.trim() || 'Salary'} slip, ${periodLabel(p.period)}` : fileName,
    fileName: p ? fileName : null,
    amount: p?.grossAmount ?? null,
    warnings: [],
    slipIndex: null,
  };
  switch (item.status) {
    case 'ready':
      return { ...base, tone: 'review', meta: 'Gross pay, not saved yet', slipIndex: i };
    case 'saved':
      return { ...base, tone: 'ok', meta: 'Saved to Salary' };
    case 'error':
      // A slip that was read but failed to open its review can be retried.
      return {
        ...base,
        tone: 'error',
        meta: item.error ?? 'Could not read this slip.',
        slipIndex: p && item.pdfPath ? i : null,
      };
    default:
      return { ...base, tone: 'working', meta: SLIP_PROGRESS[item.status] ?? '' };
  }
}

function groceryEntry(r: GroceryReceiptUploadResult, i: number): FileEntry {
  const items = count(r.itemCount, 'item', 'items');
  return {
    key: `grocery-${i}`,
    tone: r.wasDuplicate ? 'duplicate' : 'ok',
    title: `${r.storeName} receipt, ${dayLabel(r.receiptDate)}`,
    meta: r.wasDuplicate ? `Already in Groceries, ${items}` : `${items} added to Groceries`,
    fileName: null,
    amount: r.total,
    warnings: r.warnings ?? [],
    slipIndex: null,
  };
}

function plainEntry(key: string, tone: FileTone, f: FailedFile): FileEntry {
  return {
    key,
    tone,
    title: f.fileName,
    meta: f.error,
    fileName: null,
    amount: null,
    warnings: [],
    slipIndex: null,
  };
}

/** '2 imported, 1 already in Beacon', counting each tone once, in timeline order. */
export function groupSummary(
  entries: FileEntry[],
  labels: Partial<Record<FileTone, string>> = {},
): string {
  const order: FileTone[] = ['review', 'working', 'ok', 'duplicate', 'waiting', 'error'];
  return order
    .map((tone) => [tone, entries.filter((e) => e.tone === tone).length] as const)
    .filter(([, n]) => n > 0)
    .map(([tone, n]) => `${n} ${labels[tone] ?? TONE_LABELS[tone]}`)
    .join(', ');
}

/** The upload's files grouped by kind, in the order the timeline shows them. Empty kinds are left out. */
export function uploadGroups(outcome: UploadOutcome): FileGroup[] {
  const groups: FileGroup[] = [];
  const add = (
    key: FileGroup['key'],
    title: string,
    entries: FileEntry[],
    note: string | null = null,
    labels: Partial<Record<FileTone, string>> = {},
  ) => {
    if (entries.length > 0) {
      groups.push({ key, title, summary: groupSummary(entries, labels), note, entries });
    }
  };

  add('statements', 'Bank statements', outcome.statements.map(statementEntry));

  const slips = outcome.slips.map(slipEntry);
  add(
    'slips',
    'Salary slips',
    slips,
    slips.some((e) => e.tone === 'review')
      ? 'Review and save each slip before it is recorded.'
      : null,
    { ok: 'saved' },
  );

  add('groceries', 'Grocery receipts', outcome.groceries.map(groceryEntry), null, {
    ok: 'added',
  });

  add(
    'micro1',
    'micro1, waiting for a match',
    outcome.micro1.map((f, i) => plainEntry(`micro1-${i}`, 'waiting', f)),
    MICRO1_NOTE,
    { waiting: 'not imported' },
  );

  add(
    'failed',
    'Not imported',
    outcome.failed.map((f, i) => plainEntry(`failed-${i}`, 'error', f)),
  );

  return groups;
}

/** Counts across every group, for the chips at the top of the results. */
export function uploadTally(groups: FileGroup[]): UploadTally {
  const tally: UploadTally = { files: 0, imported: 0, duplicates: 0, review: 0, failed: 0 };
  for (const entry of groups.flatMap((g) => g.entries)) {
    tally.files++;
    if (entry.tone === 'ok') tally.imported++;
    else if (entry.tone === 'duplicate') tally.duplicates++;
    else if (entry.tone === 'review' || entry.tone === 'working') tally.review++;
    else tally.failed++;
  }
  return tally;
}
