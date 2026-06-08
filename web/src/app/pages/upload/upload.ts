import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { CurrencyPipe, DatePipe, NgClass, SlicePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { Router, RouterLink } from '@angular/router';
import { forkJoin, Observable, of } from 'rxjs';
import { map, switchMap, tap } from 'rxjs/operators';
import { FinanceService } from '../../core/services/finance.service';
import { SalaryService } from '../../core/services/salary.service';
import { GroceriesService } from '../../core/services/groceries.service';
import { GroceryCategoriesService } from '../../core/services/grocery-categories.service';
import { GroceryReceiptUploadResult } from '../../core/models/grocery.model';
import {
  BatchUploadItemResult,
  ParsedLineItemResponse,
  ParsedSlipResponse,
  SalaryItemCategory,
  SalaryProfile,
  TransferCandidate,
  UnifiedUploadItemResult,
  UploadResult,
} from '../../core/models/statement.model';

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

interface SalaryQueueItem {
  file?: File;
  status: 'pending' | 'uploading' | 'parsing' | 'ready' | 'saved' | 'error';
  pdfPath?: string;
  fileName?: string;
  parsed?: ParsedSlipResponse;
  error?: string;
}

type PendingDialog =
  | { type: 'grocery-mapping'; receiptId: number; categories: string[] }
  | { type: 'salary-review'; queueIdx: number }
  | { type: 'transfer-review'; candidates: TransferCandidate[] };

const BANK_DETECT_ERROR = 'Could not detect bank';

@Component({
  selector: 'app-upload',
  standalone: true,
  imports: [NgClass, SlicePipe, DatePipe, FormsModule, CurrencyPipe, RouterLink],
  templateUrl: './upload.html',
  styleUrl: './upload.scss',
})
export class UploadComponent implements OnInit {
  private http = inject(HttpClient);
  private salaryService = inject(SalaryService);
  private groceriesSvc = inject(GroceriesService);
  groceryCatSvc = inject(GroceryCategoriesService);
  finance = inject(FinanceService);
  router = inject(Router);

  state = signal<UploadState>('idle');
  dragOver = signal(false);
  message = signal('');

  singleResult = signal<UploadResult | null>(null);
  batchSummary = signal<BatchSummary | null>(null);

  showTransferReview = signal(false);
  transferCandidates = signal<TransferCandidate[]>([]);
  selectedTransfers = signal<Set<number>>(new Set());

  mealCardText = signal('');
  mealCardState = signal<'idle' | 'review' | 'importing' | 'success' | 'error'>('idle');
  mealCardMessage = signal('');
  mealCardResult = signal<UploadResult | null>(null);
  mealCardPeriodFrom = signal('');
  mealCardPeriodTo = signal('');
  mealCardBalance = signal<number | null>(null);

  salaryQueue = signal<SalaryQueueItem[]>([]);
  profiles = signal<SalaryProfile[]>([]);

  groceryResults = signal<GroceryReceiptUploadResult[]>([]);
  pendingDialogs = signal<PendingDialog[]>([]);

  mappingReceiptId = signal<number | null>(null);
  pendingMappingCategories = signal<string[]>([]);
  mappingIndex = signal(0);
  mappingMode = signal<'new' | 'existing'>('new');
  newMappingName = signal('');
  newMappingColor = signal('#a855f7');
  selectedExistingCatId = signal<number | null>(null);
  mappingLoading = signal(false);
  showMappingModal = signal(false);
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

  slipFormValid = computed(
    () =>
      !!(
        this.slipProfileId() &&
        this.slipPeriod() &&
        this.slipGross() != null &&
        this.slipNet() != null
      ),
  );

  hasUnmatchedLineItems = computed(() =>
    this.slipLineItems().some((li) => li.hint !== undefined && li.salaryItemCategoryId === null),
  );

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

  goToCategorize(): void {
    this.router.navigate(['/transactions'], { queryParams: { filter: 'unknown' } });
  }

  resetForm(): void {
    this.state.set('idle');
    this.singleResult.set(null);
    this.batchSummary.set(null);
    this.message.set('');
    this.showTransferReview.set(false);
    this.transferCandidates.set([]);
    this.selectedTransfers.set(new Set());
    this.salaryQueue.set([]);
    this.groceryResults.set([]);
    this.pendingDialogs.set([]);
    this.showMappingModal.set(false);
    this.pendingMappingCategories.set([]);
    this.mappingIndex.set(0);
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
      this.showTransferReview.set(false);
      this.advanceDialogQueue();
      return;
    }
    const txIds: number[] = [];
    for (const idx of selected) {
      txIds.push(candidates[idx].newTxId, candidates[idx].existingTxId);
    }
    this.finance.markTransfers(txIds).subscribe(() => {
      this.finance.reload();
      this.showTransferReview.set(false);
      this.advanceDialogQueue();
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
    const valid = files.filter(
      (f) => f.name.toLowerCase().endsWith('.pdf') || f.name.toLowerCase().endsWith('.zip'),
    );

    if (valid.length === 0) {
      this.state.set('error');
      this.message.set('Only PDF files or ZIP archives containing PDFs are supported.');
      return;
    }

    this.state.set('uploading');
    this.singleResult.set(null);
    this.batchSummary.set(null);
    this.salaryQueue.set([]);
    this.groceryResults.set([]);
    this.pendingDialogs.set([]);

    this.finance.uploadUnified(valid).subscribe({
      next: (results: UnifiedUploadItemResult[]) => {
        const bankItems = results.filter((r) => r.documentType === 'BankStatement');
        const groceryItems = results.filter((r) => r.documentType === 'GroceryReceipt');
        const salaryItems = results.filter((r) => r.documentType === 'SalarySlip');
        const unknownItems = results.filter((r) => r.documentType === 'Unknown');

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

        if (groceryItems.length > 0) {
          this.groceryResults.set(groceryItems.map((r) => r.groceryResult!));
          this.groceriesSvc.reload();
          this.groceriesSvc.loadAllItems();
          for (const item of groceryItems) {
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
          const newItems: SalaryQueueItem[] = salaryItems.map((r) => ({
            status: r.salaryResult ? ('ready' as const) : ('error' as const),
            pdfPath: r.salaryResult?.pdfPath,
            fileName: r.salaryResult?.fileName ?? r.fileName,
            parsed: r.salaryResult?.parsed,
            error: r.error ?? undefined,
          }));
          this.salaryQueue.update((q) => [...q, ...newItems]);
          salaryItems.forEach((r, i) => {
            if (r.salaryResult) {
              dialogs.push({ type: 'salary-review', queueIdx: startIdx + i });
            }
          });
        }

        if (unknownItems.length > 0) {
          const unknownBatchItems = unknownItems.map((r) => ({
            fileName: r.fileName,
            success: false,
            result: null,
            error: r.error,
          }));
          this.batchSummary.update((s) =>
            s
              ? {
                  ...s,
                  errors: s.errors + unknownItems.length,
                  items: [...s.items, ...unknownBatchItems],
                }
              : {
                  imported: 0,
                  duplicates: 0,
                  errors: unknownItems.length,
                  unknownCount: 0,
                  items: unknownBatchItems,
                },
          );
        }

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

  reviewSalaryItem(idx: number): void {
    const item = this.salaryQueue()[idx];
    if (!item.parsed || !item.pdfPath) return;
    this.openSlipFromParsed(item.parsed, item.pdfPath, item.fileName ?? item.file?.name ?? '', idx);
  }

  private openSlipFromParsed(
    parsed: ParsedSlipResponse,
    pdfPath: string,
    fileName: string,
    queueIdx: number,
  ): void {
    this.slipQueueIdx.set(queueIdx);
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

    this.ensureProfile(parsed.employer)
      .pipe(
        tap((profileId) => this.slipProfileId.set(profileId)),
        switchMap((profileId) =>
          this.salaryService
            .getItemCategories(profileId)
            .pipe(switchMap((cats) => this.autoEnsureCategories(profileId, parsed.lineItems, cats))),
        ),
      )
      .subscribe((allCats) => {
        this.itemCategories.set(allCats);
        this.slipLineItems.set(
          parsed.lineItems.map((li, i) => {
            const catId =
              allCats.find((c) => c.name.toLowerCase() === li.description.toLowerCase())?.id ??
              null;
            return {
              salaryItemCategoryId: catId,
              amount: li.amount,
              sortOrder: i,
              itemType: li.itemType,
              hint: catId === null ? li.description : undefined,
              quantity: li.quantity,
              unitValue: li.unitValue,
              percentage: li.percentage,
              incidenciaBase: li.incidenciaBase,
            };
          }),
        );
        this.showSlipModal.set(true);
      });
  }

  onSlipProfileChange(profileId: number | null): void {
    const id = profileId ? +profileId : null;
    this.slipProfileId.set(id);
    if (!id) {
      this.itemCategories.set([]);
      return;
    }
    const unmatchedDrafts = this.slipLineItems().filter(
      (li) => li.salaryItemCategoryId === null && li.hint && li.itemType,
    );
    this.salaryService
      .getItemCategories(id)
      .pipe(
        switchMap((cats) => {
          const asLineItems: ParsedLineItemResponse[] = unmatchedDrafts
            .filter((li) => !cats.some((c) => c.name.toLowerCase() === li.hint!.toLowerCase()))
            .map((li) => ({
              description: li.hint!,
              itemType: li.itemType!,
              amount: li.amount ?? 0,
              quantity: li.quantity ?? null,
              unitValue: li.unitValue ?? null,
              percentage: li.percentage ?? null,
              incidenciaBase: li.incidenciaBase ?? null,
            }));
          return this.autoEnsureCategories(id, asLineItems, cats);
        }),
      )
      .subscribe((allCats) => {
        this.itemCategories.set(allCats);
        this.slipLineItems.update((items) =>
          items.map((li) => {
            if (li.salaryItemCategoryId !== null || !li.hint) return li;
            const catId =
              allCats.find((c) => c.name.toLowerCase() === li.hint!.toLowerCase())?.id ?? null;
            return { ...li, salaryItemCategoryId: catId, hint: catId !== null ? undefined : li.hint };
          }),
        );
      });
  }

  private ensureProfile(employerName: string): Observable<number> {
    const name = employerName?.trim() || 'My Profile';
    const match = this.profiles().find((p) => p.name.toLowerCase() === name.toLowerCase());
    if (match) return of(match.id);
    return this.salaryService.createProfile(name).pipe(
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
    if (type === 'income') return '#22c55e';
    if (type === 'deduction') return '#ef4444';
    return '#f59e0b';
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
    this.slipLoading.set(true);
    this.slipError.set('');
    const period = this.slipPeriod() + '-01';
    const lineItems = this.slipLineItems()
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
      salaryProfileId: this.slipProfileId()!,
      period,
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
    this.salaryService.createSlip(body).subscribe({
      next: () => {
        this.slipLoading.set(false);
        this.showSlipModal.set(false);
        const qIdx = this.slipQueueIdx();
        if (qIdx !== null) {
          this.updateSalaryItem(qIdx, { status: 'saved' });
          this.slipQueueIdx.set(null);
        }
        this.advanceDialogQueue();
      },
      error: (err) => {
        this.slipLoading.set(false);
        this.slipError.set(err.error ?? 'Save failed.');
      },
    });
  }

  formatPeriod(period: string): string {
    if (!period) return '';
    const d = new Date(period);
    const str = d.toLocaleString('default', { month: 'long', year: 'numeric' });
    return str.charAt(0).toUpperCase() + str.slice(1);
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
      this.mappingReceiptId.set(next.receiptId);
      this.pendingMappingCategories.set(next.categories);
      this.mappingIndex.set(0);
      this.mappingMode.set('new');
      this.newMappingName.set(next.categories[0]);
      this.newMappingColor.set('#a855f7');
      this.selectedExistingCatId.set(null);
      this.showMappingModal.set(true);
    } else if (next.type === 'salary-review') {
      this.reviewSalaryItem(next.queueIdx);
    }
  }

  confirmMapping(): void {
    const cat = this.currentMappingCategory();
    if (!cat) return;
    this.mappingLoading.set(true);

    const doMapping = (categoryId: number) => {
      this.groceryCatSvc.createReceiptMapping(cat, categoryId).subscribe({
        next: () => this.advanceMappingOrClose(),
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
    this.advanceMappingOrClose();
  }

  private advanceMappingOrClose(): void {
    this.mappingLoading.set(false);
    const next = this.mappingIndex() + 1;
    if (next < this.pendingMappingCategories().length) {
      this.mappingIndex.set(next);
      this.mappingMode.set('new');
      this.newMappingName.set(this.pendingMappingCategories()[next]);
      this.newMappingColor.set('#a855f7');
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
    this.advanceDialogQueue();
  }

  dismissSlipModal(): void {
    this.showSlipModal.set(false);
    this.slipQueueIdx.set(null);
    this.advanceDialogQueue();
  }
}
