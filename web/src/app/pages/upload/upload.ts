import {
  Component,
  computed,
  inject,
  Injector,
  OnInit,
  signal,
  ChangeDetectionStrategy,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { RouterLink } from '@angular/router';
import { forkJoin, Observable, of } from 'rxjs';
import { map, switchMap, tap } from 'rxjs/operators';
import { FinanceService } from '../../core/services/finance.service';
import { SalaryService } from '../../core/services/salary.service';
import { GroceriesService } from '../../core/services/groceries.service';
import { GroceryCategoriesService } from '../../core/services/grocery-categories.service';
import { InvestmentsService } from '../../core/services/investments.service';
import { GroceryReceiptUploadResult } from '../../core/models/grocery.model';
import {
  BatchUploadItemResult,
  HourlyRateFormula,
  ParsedLineItemResponse,
  ParsedSlipResponse,
  SalaryItemCategory,
  SalaryProfile,
  SalarySlip,
  TransferCandidate,
  UnifiedUploadItemResult,
  UploadResult,
} from '../../core/models/statement.model';
import { eur, usd } from '../../core/utils/money';
import { bankInitials } from '../../core/utils/bank';
import {
  dayLabel,
  FailedFile,
  FileEntry,
  periodLabel,
  SalaryQueueItem,
  uploadGroups,
  uploadTally,
} from './upload-results';

type UploadState = 'idle' | 'uploading' | 'success' | 'error';

interface BatchSummary {
  imported: number;
  duplicates: number;
  errors: number;
  unknownCount: number;
  items: BatchUploadItemResult[];
}

interface LineItemDraft {
  salaryItemCategoryId: number | null;
  amount: number | null;
  sortOrder: number;
  hint?: string;
  itemType?: 'income' | 'deduction' | 'tax';
  quantity?: number | null;
  unitValue?: number | null;
  percentage?: number | null;
  incidenciaBase?: number | null;
}

type PendingDialog =
  | { type: 'grocery-mapping'; receiptId: number; categories: string[] }
  | { type: 'salary-review'; queueIdx: number }
  | { type: 'mercor-eur'; queueIdx: number }
  | { type: 'transfer-review'; candidates: TransferCandidate[] };

const BANK_DETECT_ERROR = 'Could not detect bank';

// Stored data colours, not theme colours: the default a new grocery category starts with (as on
// the Categories and Activity pages), and the colour a salary item category gets per item type.
const NEW_CATEGORY_COLOR = '#a855f7';
const ITEM_TYPE_COLORS: Record<'income' | 'deduction' | 'tax', string> = {
  income: '#22c55e',
  deduction: '#ef4444',
  tax: '#f59e0b',
};

/** The hourly-rate formula a profile created from a slip starts with, by the slip's parser. */
const NEW_PROFILE_FORMULA: Partial<Record<string, HourlyRateFormula>> = { Mercor: 'hours' };
import { StatementListComponent } from './statement-list';

@Component({
  selector: 'app-upload',
  standalone: true,
  imports: [FormsModule, RouterLink, StatementListComponent],
  templateUrl: './upload.html',
  changeDetection: ChangeDetectionStrategy.Eager,
  styleUrl: './upload.scss',
})
export class UploadComponent implements OnInit {
  private http = inject(HttpClient);
  private salaryService = inject(SalaryService);
  private groceriesSvc = inject(GroceriesService);
  groceryCatSvc = inject(GroceryCategoriesService);
  finance = inject(FinanceService);
  private injector = inject(Injector);

  readonly eur = eur;
  readonly usd = usd;
  readonly bankInitials = bankInitials;
  readonly dayLabel = dayLabel;

  state = signal<UploadState>('idle');
  dragOver = signal(false);
  message = signal('');
  /** How many files the upload in progress sent. */
  uploadingCount = signal(0);

  singleResult = signal<UploadResult | null>(null);
  batchSummary = signal<BatchSummary | null>(null);

  showTransferReview = signal(false);
  transferCandidates = signal<TransferCandidate[]>([]);
  selectedTransfers = signal<Set<number>>(new Set());
  transferSaving = signal(false);
  transferError = signal('');

  mealCardText = signal('');
  mealCardState = signal<'idle' | 'review' | 'importing' | 'success' | 'error'>('idle');
  mealCardMessage = signal('');
  mealCardResult = signal<UploadResult | null>(null);
  mealCardPeriodFrom = signal('');
  mealCardPeriodTo = signal('');
  mealCardBalance = signal<number | null>(null);

  salaryQueue = signal<SalaryQueueItem[]>([]);
  profiles = signal<SalaryProfile[]>([]);

  micro1Unpaired = signal<FailedFile[]>([]);
  /** Broker exports (XTB) of the last upload, read or refused. */
  tradeFiles = signal<UnifiedUploadItemResult[]>([]);
  /** Grocery receipts that failed and files of no known kind. */
  failedFiles = signal<FailedFile[]>([]);

  groceryResults = signal<GroceryReceiptUploadResult[]>([]);
  pendingDialogs = signal<PendingDialog[]>([]);

  /** Every file of the last upload, grouped by kind, for the results timeline. */
  groups = computed(() =>
    uploadGroups({
      statements: this.batchSummary()?.items ?? [],
      trades: this.tradeFiles(),
      slips: this.salaryQueue(),
      groceries: this.groceryResults(),
      micro1: this.micro1Unpaired(),
      failed: this.failedFiles(),
    }),
  );
  tally = computed(() => uploadTally(this.groups()));

  mappingReceiptId = signal<number | null>(null);
  pendingMappingCategories = signal<string[]>([]);
  mappingIndex = signal(0);
  mappingMode = signal<'new' | 'existing'>('new');
  newMappingName = signal('');
  newMappingColor = signal(NEW_CATEGORY_COLOR);
  selectedExistingCatId = signal<number | null>(null);
  mappingLoading = signal(false);
  showMappingModal = signal(false);
  groceryMappingFileIdx = signal(0);
  groceryMappingFileTotal = signal(0);
  private handledReceiptCategories = signal<Set<string>>(new Set());
  currentMappingCategory = computed(() => this.pendingMappingCategories()[this.mappingIndex()]);
  mappingProgress = computed(
    () => `${this.mappingIndex() + 1} of ${this.pendingMappingCategories().length}`,
  );
  itemCategories = signal<SalaryItemCategory[]>([]);

  readonly itemTypes: Array<{ value: 'income' | 'deduction' | 'tax'; label: string }> = [
    { value: 'income', label: 'Income' },
    { value: 'deduction', label: 'Deduction' },
    { value: 'tax', label: 'Tax' },
  ];

  showSlipModal = signal(false);
  slipQueueIdx = signal<number | null>(null);
  slipProfileId = signal<number | null>(null);
  slipPeriod = signal('');
  slipGross = signal<number | null>(null);
  slipNet = signal<number | null>(null);
  slipNotes = signal('');
  slipLineItems = signal<LineItemDraft[]>([]);
  slipLoading = signal(false);
  slipError = signal('');
  slipPdfPath = signal<string | null>(null);
  slipSourceFile = signal<string | null>(null);
  slipBaseAmount = signal<number | null>(null);
  slipHoursWorked = signal<number | null>(null);
  slipHourlyRate = signal<number | null>(null);
  slipTotalEspecie = signal<number | null>(null);

  slipPendingProfileName = signal<string | null>(null);
  /** The parser of the slip under review: picks a new profile's formula. */
  private slipParserName = signal<string | null>(null);

  /** The Mercor statement whose EUR received is being asked for (ADR-033). */
  showMercorModal = signal(false);
  mercorQueueIdx = signal<number | null>(null);
  mercorEur = signal<number | null>(null);
  mercorLoading = signal(false);
  mercorError = signal('');
  mercorStatement = computed(() => {
    const idx = this.mercorQueueIdx();
    return idx === null ? null : (this.salaryQueue()[idx]?.mercor ?? null);
  });
  mercorEurValid = computed(() => (this.mercorEur() ?? 0) > 0);

  /** Slips already saved for the profile being reviewed - drives the merge notice below. */
  private profileSlips = signal<SalarySlip[]>([]);
  slipMergeConfirmed = signal(true);

  /**
   * The saved slip this review would collide with, if any. A profile+period pair is unique, so a
   * micro1 paycheck for the second half of a month lands on the slip its first half created.
   */
  slipMergeTarget = computed(() => {
    const period = this.slipPeriod();
    if (!period) return null;
    return this.profileSlips().find((s) => s.period.slice(0, 7) === period) ?? null;
  });

  mergedGross = computed(
    () => (this.slipMergeTarget()?.grossAmount ?? 0) + (this.slipGross() ?? 0),
  );
  mergedNet = computed(() => (this.slipMergeTarget()?.netAmount ?? 0) + (this.slipNet() ?? 0));

  slipFormValid = computed(
    () =>
      !!(
        (this.slipProfileId() || this.slipPendingProfileName()) &&
        this.slipPeriod() &&
        this.slipGross() != null &&
        this.slipNet() != null
      ),
  );

  hasUnmatchedLineItems = computed(() =>
    this.slipLineItems().some((li) => li.hint !== undefined && li.salaryItemCategoryId === null),
  );

  transferActionLabel = computed(() => {
    const n = this.selectedTransfers().size;
    if (n === 0) return 'Continue';
    return n === 1 ? 'Mark 1 as a transfer' : `Mark ${n} as transfers`;
  });

  slipWarnings = computed(() => {
    const idx = this.slipQueueIdx();
    if (idx === null) return [];
    return this.salaryQueue()[idx]?.parsed?.warnings ?? [];
  });

  ngOnInit(): void {
    this.salaryService.getProfiles().subscribe((v) => this.profiles.set(v));
  }

  onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    if (!input.files?.length) return;
    this.uploadFiles(Array.from(input.files));
    input.value = '';
  }

  onDrop(event: DragEvent): void {
    event.preventDefault();
    this.dragOver.set(false);
    const files = event.dataTransfer?.files;
    if (files?.length) this.uploadFiles(Array.from(files));
  }

  onDragOver(event: DragEvent): void {
    event.preventDefault();
    this.dragOver.set(true);
  }
  onDragLeave(): void {
    this.dragOver.set(false);
  }

  resetForm(): void {
    this.state.set('idle');
    this.singleResult.set(null);
    this.batchSummary.set(null);
    this.message.set('');
    this.showTransferReview.set(false);
    this.transferCandidates.set([]);
    this.selectedTransfers.set(new Set());
    this.transferSaving.set(false);
    this.transferError.set('');
    this.salaryQueue.set([]);
    this.micro1Unpaired.set([]);
    this.tradeFiles.set([]);
    this.failedFiles.set([]);
    this.groceryResults.set([]);
    this.pendingDialogs.set([]);
    this.showMercorModal.set(false);
    this.mercorQueueIdx.set(null);
    this.showMappingModal.set(false);
    this.pendingMappingCategories.set([]);
    this.mappingIndex.set(0);
    this.groceryMappingFileIdx.set(0);
    this.groceryMappingFileTotal.set(0);
    this.handledReceiptCategories.set(new Set());
  }

  toggleTransferCandidate(index: number): void {
    const s = new Set(this.selectedTransfers());
    if (s.has(index)) s.delete(index);
    else s.add(index);
    this.selectedTransfers.set(s);
  }

  isTransferSelected(index: number): boolean {
    return this.selectedTransfers().has(index);
  }

  confirmTransfers(): void {
    const candidates = this.transferCandidates();
    const selected = [...this.selectedTransfers()];
    if (selected.length === 0) {
      this.dismissTransferReview();
      return;
    }
    const txIds: number[] = [];
    for (const idx of selected) {
      txIds.push(candidates[idx].newTxId, candidates[idx].existingTxId);
    }
    this.transferSaving.set(true);
    this.transferError.set('');
    this.finance.markTransfers(txIds).subscribe({
      next: () => {
        this.finance.reload();
        this.transferSaving.set(false);
        this.dismissTransferReview();
      },
      error: () => {
        this.transferSaving.set(false);
        this.transferError.set('Could not mark transfers. Try again, or skip for now.');
      },
    });
  }

  reviewMealCard(): void {
    const text = this.mealCardText().trim();
    if (!text) return;
    const dates = [...text.matchAll(/^(\d{2})\/(\d{2})\/(\d{4})/gm)].map(
      (m) => `${m[3]}-${m[2]}-${m[1]}`,
    );
    dates.sort();
    this.mealCardPeriodFrom.set(dates[0] ?? '');
    this.mealCardPeriodTo.set(dates[dates.length - 1] ?? '');
    this.mealCardBalance.set(null);
    this.mealCardState.set('review');
  }

  importMealCard(): void {
    const text = this.mealCardText().trim();
    if (!text) return;
    this.mealCardState.set('importing');
    this.mealCardResult.set(null);
    this.mealCardMessage.set('');
    this.finance
      .importMealCardText(text, {
        periodFrom: this.mealCardPeriodFrom() || undefined,
        periodTo: this.mealCardPeriodTo() || undefined,
        closingBalance: this.mealCardBalance() ?? undefined,
      })
      .subscribe({
        next: (result) => {
          this.mealCardState.set('success');
          this.mealCardResult.set(result);
          this.mealCardText.set('');
          this.finance.reload();
          if (result.transferCandidates?.length) {
            this.transferCandidates.set(result.transferCandidates);
            this.selectedTransfers.set(new Set(result.transferCandidates.map((_, i) => i)));
            this.showTransferReview.set(true);
          }
        },
        error: (err) => {
          this.mealCardState.set('error');
          const body = err.error;
          this.mealCardMessage.set(
            typeof body === 'string'
              ? body
              : typeof body === 'object' && body?.message
                ? body.message
                : 'Import failed.',
          );
        },
      });
  }

  resetMealCard(): void {
    this.mealCardState.set('idle');
    this.mealCardResult.set(null);
    this.mealCardMessage.set('');
    this.mealCardPeriodFrom.set('');
    this.mealCardPeriodTo.set('');
    this.mealCardBalance.set(null);
  }

  private uploadFiles(files: File[]): void {
    const valid = files.filter((f) => /\.(pdf|csv|xlsx|zip)$/i.test(f.name));

    if (valid.length === 0) {
      this.state.set('error');
      this.message.set('Only PDF, CSV and XLSX files, or ZIP archives of them, are supported.');
      return;
    }

    this.state.set('uploading');
    this.uploadingCount.set(valid.length);
    this.singleResult.set(null);
    this.batchSummary.set(null);
    this.salaryQueue.set([]);
    this.micro1Unpaired.set([]);
    this.tradeFiles.set([]);
    this.failedFiles.set([]);
    this.groceryResults.set([]);
    this.pendingDialogs.set([]);

    this.finance.uploadUnified(valid).subscribe({
      next: (results: UnifiedUploadItemResult[]) => {
        const bankItems = results.filter((r) => r.documentType === 'BankStatement');
        const groceryItems = results.filter((r) => r.documentType === 'GroceryReceipt');
        // A Mercor statement queues as a slip that waits for the EUR it paid.
        const salaryItems = results.filter(
          (r) => r.documentType === 'SalarySlip' || r.documentType === 'MercorNeedsEur',
        );
        const unknownItems = results.filter((r) => r.documentType === 'Unknown');
        const micro1Items = results.filter((r) => r.documentType === 'Micro1Unpaired');
        const tradeItems = results.filter((r) => r.documentType === 'BrokerExport');

        const dialogs: PendingDialog[] = [];

        if (bankItems.length > 0) {
          const summary: BatchSummary = {
            imported: bankItems.filter((r) => r.success).length,
            duplicates: bankItems.filter((r) => r.wasDuplicate).length,
            errors: bankItems.filter((r) => !r.success && !r.wasDuplicate).length,
            unknownCount: bankItems.reduce(
              (sum, r) => sum + (r.statementResult?.unknownCount ?? 0),
              0,
            ),
            items: bankItems.map((r) => ({
              fileName: r.fileName,
              success: r.success,
              result: r.statementResult,
              error: r.error,
            })),
          };
          this.batchSummary.set(summary);
          this.finance.reload();

          const candidates = bankItems.flatMap((r) => r.statementResult?.transferCandidates ?? []);
          if (candidates.length > 0) {
            dialogs.push({ type: 'transfer-review', candidates });
          }
        }

        const okGroceryItems = groceryItems.filter((r) => r.success && r.groceryResult);
        const failedGroceryItems = groceryItems.filter((r) => !r.success || !r.groceryResult);

        if (okGroceryItems.length > 0) {
          this.groceryResults.set(okGroceryItems.map((r) => r.groceryResult!));
          this.groceriesSvc.reload();
          this.groceriesSvc.loadAllItems();
          for (const item of okGroceryItems) {
            if (item.groceryResult?.newReceiptCategories?.length) {
              dialogs.push({
                type: 'grocery-mapping',
                receiptId: item.groceryResult.receiptId,
                categories: item.groceryResult.newReceiptCategories,
              });
            }
          }
        }

        if (salaryItems.length > 0) {
          const startIdx = this.salaryQueue().length;
          const newItems: SalaryQueueItem[] = salaryItems.map((r) =>
            r.mercorResult
              ? {
                  status: 'needs-eur' as const,
                  pdfPath: r.mercorResult.pdfPath,
                  fileName: r.mercorResult.fileName,
                  mercor: r.mercorResult,
                }
              : {
                  status: r.salaryResult ? ('ready' as const) : ('error' as const),
                  pdfPath: r.salaryResult?.pdfPath,
                  fileName: r.salaryResult?.fileName ?? r.fileName,
                  parsed: r.salaryResult?.parsed,
                  error: r.error ?? undefined,
                },
          );
          this.salaryQueue.update((q) => [...q, ...newItems]);
          salaryItems.forEach((r, i) => {
            if (r.mercorResult) {
              dialogs.push({ type: 'mercor-eur', queueIdx: startIdx + i });
            } else if (r.salaryResult) {
              dialogs.push({ type: 'salary-review', queueIdx: startIdx + i });
            }
          });
        }

        if (micro1Items.length > 0) {
          this.micro1Unpaired.set(
            micro1Items.map((r) => ({
              fileName: r.fileName,
              error: r.error ?? 'This micro1 file is missing its counterpart and was not imported.',
            })),
          );
        }

        this.tradeFiles.set(tradeItems);
        // Invest and the dashboard keep the assets for the session: show them the new lots, from
        // a statement's buys or a broker's export.
        const lotsAdded =
          bankItems.some((r) => (r.statementResult?.lotsAdded ?? 0) > 0) ||
          tradeItems.some((r) => (r.tradesResult?.added ?? 0) > 0);
        if (lotsAdded) this.injector.get(InvestmentsService).load();

        const failedItems = [...failedGroceryItems, ...unknownItems];
        this.failedFiles.set(
          failedItems.map((r) => ({ fileName: r.fileName, error: r.error ?? 'Import failed.' })),
        );

        this.groceryMappingFileTotal.set(
          dialogs.filter((d) => d.type === 'grocery-mapping').length,
        );
        this.groceryMappingFileIdx.set(0);
        this.pendingDialogs.set(dialogs);
        this.state.set('success');
        this.advanceDialogQueue();
      },
      error: (err) => {
        this.state.set('error');
        const body = err.error;
        this.message.set(
          typeof body === 'string'
            ? body
            : typeof body === 'object' && body?.message
              ? body.message
              : 'Upload failed.',
        );
      },
    });
  }

  private updateSalaryItem(idx: number, patch: Partial<SalaryQueueItem>): void {
    this.salaryQueue.update((q) => q.map((item, i) => (i === idx ? { ...item, ...patch } : item)));
  }

  /** "Review and save" on a slip in the results timeline. */
  reviewEntry(entry: FileEntry): void {
    if (entry.slipIndex !== null) this.reviewSalaryItem(entry.slipIndex);
  }

  reviewSalaryItem(idx: number): void {
    const item = this.salaryQueue()[idx];
    if (item.status === 'needs-eur') {
      this.openMercorEur(idx);
      return;
    }
    if (!item.parsed || !item.pdfPath) return;
    this.openSlipFromParsed(item.parsed, item.pdfPath, item.fileName ?? item.file?.name ?? '', idx);
  }

  /** Asks for the EUR a Mercor statement paid, pre-filled with the payouts imported for its month. */
  private openMercorEur(queueIdx: number): void {
    const statement = this.salaryQueue()[queueIdx]?.mercor;
    if (!statement) return;
    this.mercorQueueIdx.set(queueIdx);
    this.mercorEur.set(statement.suggestedEur);
    this.mercorError.set('');
    this.mercorLoading.set(false);
    this.showMercorModal.set(true);
  }

  setMercorEur(value: number | string | null): void {
    this.mercorEur.set(value === null || value === '' ? null : +value);
  }

  /** Converts the statement at the EUR entered, then opens the usual slip review. */
  confirmMercorEur(): void {
    const idx = this.mercorQueueIdx();
    const item = idx === null ? undefined : this.salaryQueue()[idx];
    const amount = this.mercorEur();
    if (idx === null || !item?.pdfPath || amount === null || !this.mercorEurValid()) return;
    const pdfPath = item.pdfPath;

    this.mercorLoading.set(true);
    this.mercorError.set('');
    this.salaryService.parseMercor(pdfPath, amount).subscribe({
      next: (parsed) => {
        this.mercorLoading.set(false);
        this.showMercorModal.set(false);
        this.mercorQueueIdx.set(null);
        this.updateSalaryItem(idx, { status: 'ready', parsed });
        this.openSlipFromParsed(parsed, pdfPath, item.fileName ?? '', idx);
      },
      error: (err) => {
        this.mercorLoading.set(false);
        const body = err?.error;
        this.mercorError.set(
          typeof body === 'string'
            ? body
            : (body?.message ?? 'Could not convert the statement. Try again.'),
        );
      },
    });
  }

  /** Leaves the statement waiting: its row on the timeline asks again. */
  dismissMercorModal(): void {
    this.showMercorModal.set(false);
    this.mercorQueueIdx.set(null);
    this.advanceDialogQueue();
  }

  private openSlipFromParsed(
    parsed: ParsedSlipResponse,
    pdfPath: string,
    fileName: string,
    queueIdx: number,
  ): void {
    this.slipQueueIdx.set(queueIdx);
    this.slipParserName.set(parsed.parserName);
    this.slipPeriod.set(parsed.period.slice(0, 7));
    this.slipGross.set(parsed.grossAmount);
    this.slipNet.set(parsed.netAmount);
    this.slipNotes.set('');
    this.slipError.set('');
    this.slipPdfPath.set(pdfPath);
    this.slipSourceFile.set(fileName);
    this.slipBaseAmount.set(parsed.baseAmount ?? null);
    this.slipHoursWorked.set(parsed.hoursWorked ?? null);
    this.slipHourlyRate.set(parsed.hourlyRate ?? null);
    this.slipTotalEspecie.set(parsed.totalEspecie ?? null);
    this.slipPendingProfileName.set(null);
    this.profileSlips.set([]);
    this.slipMergeConfirmed.set(true);

    const name = parsed.employer?.trim() || 'My Profile';
    const match = this.profiles().find((p) => p.name.toLowerCase() === name.toLowerCase());

    if (!match) {
      this.slipPendingProfileName.set(name);
      this.slipProfileId.set(null);
      this.itemCategories.set([]);
      this.slipLineItems.set(parsed.lineItems.map((li, i) => this.toDraft(li, i, [])));
      this.showSlipModal.set(true);
      return;
    }

    this.slipProfileId.set(match.id);
    // Fetched per modal open, not cached: an earlier review in this same batch may have just
    // created the slip this one has to merge into.
    forkJoin({
      cats: this.salaryService.getItemCategories(match.id),
      slips: this.salaryService.getSlips(match.id),
    }).subscribe({
      next: ({ cats, slips }) => {
        this.itemCategories.set(cats);
        this.profileSlips.set(slips);
        this.slipLineItems.set(parsed.lineItems.map((li, i) => this.toDraft(li, i, cats)));
        this.showSlipModal.set(true);
      },
      error: () => {
        this.updateSalaryItem(queueIdx, {
          status: 'error',
          error: 'Could not load salary categories - use Review & Save to retry.',
        });
        this.advanceDialogQueue();
      },
    });
  }

  private toDraft(
    li: ParsedLineItemResponse,
    sortOrder: number,
    cats: SalaryItemCategory[],
  ): LineItemDraft {
    const catId =
      cats.find((c) => c.name.toLowerCase() === li.description.toLowerCase())?.id ?? null;
    return {
      salaryItemCategoryId: catId,
      amount: li.amount,
      sortOrder,
      itemType: li.itemType,
      hint: catId === null ? li.description : undefined,
      quantity: li.quantity,
      unitValue: li.unitValue,
      percentage: li.percentage,
      incidenciaBase: li.incidenciaBase,
    };
  }

  onSlipProfileChange(profileId: number | null): void {
    const id = profileId ? +profileId : null;
    this.slipProfileId.set(id);
    if (id !== null) this.slipPendingProfileName.set(null);
    // Slips are per profile, so the merge target must be re-resolved against the new one.
    this.profileSlips.set([]);
    if (!id) {
      this.itemCategories.set([]);
      return;
    }
    this.salaryService.getSlips(id).subscribe({ next: (slips) => this.profileSlips.set(slips) });
    this.salaryService.getItemCategories(id).subscribe({
      next: (cats) => {
        this.itemCategories.set(cats);
        this.slipLineItems.update((items) =>
          items.map((li) => {
            if (li.salaryItemCategoryId !== null || !li.hint) return li;
            const catId =
              cats.find((c) => c.name.toLowerCase() === li.hint!.toLowerCase())?.id ?? null;
            return {
              ...li,
              salaryItemCategoryId: catId,
              hint: catId !== null ? undefined : li.hint,
            };
          }),
        );
      },
      error: () => this.slipError.set('Could not load categories for this profile.'),
    });
  }

  private ensureProfile(employerName: string): Observable<number> {
    const name = employerName?.trim() || 'My Profile';
    const match = this.profiles().find((p) => p.name.toLowerCase() === name.toLowerCase());
    if (match) return of(match.id);
    const formula = NEW_PROFILE_FORMULA[this.slipParserName() ?? ''];
    return this.salaryService.createProfile(name, undefined, formula).pipe(
      switchMap((profile) =>
        this.salaryService.getProfiles().pipe(
          tap((all) => this.profiles.set(all)),
          map(() => profile.id),
        ),
      ),
    );
  }

  private autoEnsureCategories(
    profileId: number,
    lineItems: ParsedLineItemResponse[],
    existingCats: SalaryItemCategory[],
  ): Observable<SalaryItemCategory[]> {
    const seen = new Set<string>();
    const toCreate = lineItems.filter((li) => {
      const key = li.description.toLowerCase();
      if (seen.has(key) || existingCats.some((c) => c.name.toLowerCase() === key)) return false;
      seen.add(key);
      return true;
    });
    if (toCreate.length === 0) return of(existingCats);
    return forkJoin(
      toCreate.map((li) =>
        this.salaryService.createItemCategory(
          profileId,
          li.description,
          this.colorForItemType(li.itemType),
          li.itemType,
        ),
      ),
    ).pipe(map((newCats) => [...existingCats, ...newCats]));
  }

  private colorForItemType(type: 'income' | 'deduction' | 'tax'): string {
    return ITEM_TYPE_COLORS[type];
  }

  onPdfSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';
    if (!file) return;
    this.salaryService.uploadSlipPdf(file).subscribe({
      next: (res) => {
        this.slipPdfPath.set(res.pdfPath);
        this.slipSourceFile.set(res.fileName);
      },
      error: () => this.slipError.set('PDF upload failed. Please try again.'),
    });
  }

  addLineItem(): void {
    const items = this.slipLineItems();
    this.slipLineItems.set([
      ...items,
      { salaryItemCategoryId: null, amount: null, sortOrder: items.length },
    ]);
  }

  removeLineItem(idx: number): void {
    this.slipLineItems.update((items) => items.filter((_, i) => i !== idx));
  }

  updateLineItem(idx: number, field: keyof LineItemDraft, value: unknown): void {
    this.slipLineItems.update((items) => {
      const copy = [...items];
      copy[idx] = { ...copy[idx], [field]: value };
      if (field === 'salaryItemCategoryId' && value !== null) {
        copy[idx] = { ...copy[idx], hint: undefined };
      }
      return copy;
    });
  }

  submitSlip(): void {
    if (!this.slipFormValid()) return;

    const mergeTarget = this.slipMergeTarget();
    if (mergeTarget && !this.slipMergeConfirmed()) {
      this.slipError.set(
        `A slip for ${this.formatPeriod(mergeTarget.period)} already exists. ` +
          `Tick "Add to existing slip" to combine them, or pick a different period.`,
      );
      return;
    }

    this.slipLoading.set(true);
    this.slipError.set('');
    const period = this.slipPeriod() + '-01';

    const profileId$: Observable<number> = this.slipProfileId()
      ? of(this.slipProfileId()!)
      : this.ensureProfile(this.slipPendingProfileName()!);

    profileId$
      .pipe(
        switchMap((profileId) =>
          this.salaryService.getItemCategories(profileId).pipe(
            switchMap((cats) => {
              const hinted: ParsedLineItemResponse[] = this.slipLineItems()
                .filter((li) => li.salaryItemCategoryId === null && li.hint && li.itemType)
                .map((li) => ({
                  description: li.hint!,
                  itemType: li.itemType!,
                  amount: li.amount ?? 0,
                  quantity: li.quantity ?? null,
                  unitValue: li.unitValue ?? null,
                  percentage: li.percentage ?? null,
                  incidenciaBase: li.incidenciaBase ?? null,
                }));
              return this.autoEnsureCategories(profileId, hinted, cats);
            }),
            map((allCats) => ({ profileId, allCats })),
          ),
        ),
        switchMap(({ profileId, allCats }) => {
          const lineItems = this.slipLineItems()
            .map((li) => {
              if (li.salaryItemCategoryId !== null || !li.hint) return li;
              const catId =
                allCats.find((c) => c.name.toLowerCase() === li.hint!.toLowerCase())?.id ?? null;
              return { ...li, salaryItemCategoryId: catId };
            })
            .filter((li) => li.salaryItemCategoryId != null && li.amount != null)
            .map((li, i) => ({
              salaryItemCategoryId: li.salaryItemCategoryId!,
              amount: li.amount!,
              sortOrder: i,
              quantity: li.quantity ?? undefined,
              unitValue: li.unitValue ?? undefined,
              percentage: li.percentage ?? undefined,
              incidenciaBase: li.incidenciaBase ?? undefined,
            }));
          const body = {
            grossAmount: this.slipGross()!,
            netAmount: this.slipNet()!,
            notes: this.slipNotes() || undefined,
            pdfPath: this.slipPdfPath() ?? undefined,
            sourceFile: this.slipSourceFile() ?? undefined,
            baseAmount: this.slipBaseAmount() ?? undefined,
            hoursWorked: this.slipHoursWorked() ?? undefined,
            hourlyRate: this.slipHourlyRate() ?? undefined,
            totalEspecie: this.slipTotalEspecie() ?? undefined,
            lineItems,
          };

          return mergeTarget
            ? this.salaryService.mergeSlip(mergeTarget.id, body)
            : this.salaryService.createSlip({ ...body, salaryProfileId: profileId, period });
        }),
      )
      .subscribe({
        next: () => {
          this.slipLoading.set(false);
          this.showSlipModal.set(false);
          this.slipPendingProfileName.set(null);
          const qIdx = this.slipQueueIdx();
          if (qIdx !== null) {
            this.updateSalaryItem(qIdx, { status: 'saved' });
            this.slipQueueIdx.set(null);
          }
          this.advanceDialogQueue();
        },
        error: (err) => {
          this.slipLoading.set(false);
          const body = err?.error;
          this.slipError.set(
            typeof body === 'string'
              ? body
              : (body?.message ?? 'Save failed. Please check the form and try again.'),
          );
        },
      });
  }

  /** '2026-09-01' → 'September 2026'. */
  formatPeriod(period: string): string {
    return periodLabel(period);
  }

  catsByType(type: string): SalaryItemCategory[] {
    return this.itemCategories().filter((c) => c.itemType === type);
  }

  private advanceDialogQueue(): void {
    const queue = this.pendingDialogs();
    if (queue.length === 0) return;
    const [next, ...rest] = queue;
    this.pendingDialogs.set(rest);

    if (next.type === 'transfer-review') {
      this.transferCandidates.set(next.candidates);
      this.selectedTransfers.set(new Set(next.candidates.map((_, i) => i)));
      this.showTransferReview.set(true);
    } else if (next.type === 'grocery-mapping') {
      const handled = this.handledReceiptCategories();
      const filtered = next.categories.filter((c) => !handled.has(c));
      if (filtered.length === 0) {
        this.groceryMappingFileTotal.update((n) => Math.max(0, n - 1));
        this.advanceDialogQueue();
        return;
      }
      this.groceryMappingFileIdx.update((n) => n + 1);
      this.mappingReceiptId.set(next.receiptId);
      this.pendingMappingCategories.set(filtered);
      this.mappingIndex.set(0);
      this.mappingMode.set('new');
      this.newMappingName.set(filtered[0]);
      this.newMappingColor.set(NEW_CATEGORY_COLOR);
      this.selectedExistingCatId.set(null);
      this.showMappingModal.set(true);
    } else if (next.type === 'salary-review') {
      this.reviewSalaryItem(next.queueIdx);
    } else if (next.type === 'mercor-eur') {
      this.openMercorEur(next.queueIdx);
    }
  }

  confirmMapping(): void {
    const cat = this.currentMappingCategory();
    if (!cat) return;
    this.mappingLoading.set(true);

    const doMapping = (categoryId: number) => {
      this.groceryCatSvc.createReceiptMapping(cat, categoryId).subscribe({
        next: () => {
          this.handledReceiptCategories.update((s) => new Set([...s, cat]));
          this.advanceMappingOrClose();
        },
        error: () => this.mappingLoading.set(false),
      });
    };

    if (this.mappingMode() === 'new') {
      this.groceryCatSvc
        .createCategory(this.newMappingName(), this.newMappingColor(), undefined, undefined)
        .subscribe({
          next: (newCat) => doMapping(newCat.id),
          error: () => this.mappingLoading.set(false),
        });
    } else {
      const existing = this.selectedExistingCatId();
      if (existing == null) {
        this.mappingLoading.set(false);
        return;
      }
      doMapping(existing);
    }
  }

  skipMapping(): void {
    this.handledReceiptCategories.update((s) => new Set([...s, this.currentMappingCategory()]));
    this.advanceMappingOrClose();
  }

  private advanceMappingOrClose(): void {
    this.mappingLoading.set(false);
    const next = this.mappingIndex() + 1;
    if (next < this.pendingMappingCategories().length) {
      this.mappingIndex.set(next);
      this.mappingMode.set('new');
      this.newMappingName.set(this.pendingMappingCategories()[next]);
      this.newMappingColor.set(NEW_CATEGORY_COLOR);
      this.selectedExistingCatId.set(null);
    } else {
      this.closeMappingModal();
    }
  }

  closeMappingModal(): void {
    this.showMappingModal.set(false);
    this.groceriesSvc.reload();
    this.groceriesSvc.loadAllItems();
    this.advanceDialogQueue();
  }

  dismissTransferReview(): void {
    this.showTransferReview.set(false);
    this.transferSaving.set(false);
    this.transferError.set('');
    this.advanceDialogQueue();
  }

  dismissSlipModal(): void {
    this.showSlipModal.set(false);
    this.slipQueueIdx.set(null);
    this.slipPendingProfileName.set(null);
    this.advanceDialogQueue();
  }
}
