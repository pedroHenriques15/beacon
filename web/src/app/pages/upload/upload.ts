import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { CurrencyPipe, NgClass, SlicePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { Router } from '@angular/router';
import { FinanceService } from '../../core/services/finance.service';
import { SalaryService } from '../../core/services/salary.service';
import {
  BatchUploadItemResult,
  ParsedSlipResponse,
  SalaryItemCategory,
  SalaryProfile,
  TransferCandidate,
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
  quantity?: number | null;
  unitValue?: number | null;
  percentage?: number | null;
  incidenciaBase?: number | null;
}

interface SalaryQueueItem {
  file: File;
  status: 'pending' | 'uploading' | 'parsing' | 'ready' | 'saved' | 'error';
  pdfPath?: string;
  fileName?: string;
  parsed?: ParsedSlipResponse;
  error?: string;
}

const BANK_DETECT_ERROR = 'Could not detect bank';

@Component({
  selector: 'app-upload',
  standalone: true,
  imports: [NgClass, SlicePipe, FormsModule, CurrencyPipe],
  templateUrl: './upload.html',
  styleUrl: './upload.scss',
})
export class UploadComponent implements OnInit {
  private http = inject(HttpClient);
  private salaryService = inject(SalaryService);
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

  backupState = signal<'idle' | 'running' | 'done' | 'error'>('idle');
  backupMessage = signal('');
  restoreState = signal<'idle' | 'running' | 'done' | 'error'>('idle');
  restoreMessage = signal('');

  mealCardText = signal('');
  mealCardState = signal<'idle' | 'review' | 'importing' | 'success' | 'error'>('idle');
  mealCardMessage = signal('');
  mealCardResult = signal<UploadResult | null>(null);
  mealCardPeriodFrom = signal('');
  mealCardPeriodTo = signal('');
  mealCardBalance = signal<number | null>(null);

  salaryQueue = signal<SalaryQueueItem[]>([]);
  profiles = signal<SalaryProfile[]>([]);
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
      return;
    }
    const txIds: number[] = [];
    for (const idx of selected) {
      txIds.push(candidates[idx].newTxId, candidates[idx].existingTxId);
    }
    this.finance.markTransfers(txIds).subscribe(() => {
      this.finance.reload();
      this.showTransferReview.set(false);
    });
  }

  createBackup(): void {
    this.backupState.set('running');
    this.backupMessage.set('');
    this.http.post<{ message: string; path: string }>('/api/backup', {}).subscribe({
      next: (body) => {
        this.backupState.set('done');
        this.backupMessage.set(body.path ?? 'Backup created.');
      },
      error: (err) => {
        this.backupState.set('error');
        this.backupMessage.set(err.error?.message ?? err.error ?? 'Backup failed.');
      },
    });
  }

  restoreBackup(): void {
    this.restoreState.set('running');
    this.restoreMessage.set('');
    this.http.post<{ message: string }>('/api/backup/restore', {}).subscribe({
      next: (body) => {
        this.restoreState.set('done');
        this.restoreMessage.set(body.message);
        this.finance.reload();
      },
      error: (err) => {
        this.restoreState.set('error');
        this.restoreMessage.set(err.error?.message ?? 'Restore failed.');
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
    const valid = files.filter(
      (f) => f.name.toLowerCase().endsWith('.pdf') || f.name.toLowerCase().endsWith('.zip'),
    );

    if (valid.length === 0) {
      this.state.set('error');
      this.message.set('Only PDF files or ZIP archives containing PDFs are supported.');
      return;
    }

    const pdfs = valid.filter((f) => f.name.toLowerCase().endsWith('.pdf'));

    this.state.set('uploading');
    this.singleResult.set(null);
    this.batchSummary.set(null);
    this.salaryQueue.set([]);

    this.finance.uploadBatch(valid).subscribe({
      next: (results) => {
        const bankResults = results.filter((r) => !r.error?.includes(BANK_DETECT_ERROR));
        const unrecognised = results.filter((r) => r.error?.includes(BANK_DETECT_ERROR));

        if (bankResults.length > 0) {
          const summary: BatchSummary = {
            imported: bankResults.filter((r) => r.success).length,
            duplicates: bankResults.filter((r) => !r.success && r.result != null).length,
            errors: bankResults.filter((r) => !r.success && r.result == null).length,
            unknownCount: bankResults.reduce((sum, r) => sum + (r.result?.unknownCount ?? 0), 0),
            items: bankResults,
          };
          this.batchSummary.set(summary);
          this.finance.reload();

          const candidates: TransferCandidate[] = bankResults.flatMap(
            (r) => r.result?.transferCandidates ?? [],
          );
          if (candidates.length > 0) {
            this.transferCandidates.set(candidates);
            this.selectedTransfers.set(new Set(candidates.map((_, i) => i)));
            this.showTransferReview.set(true);
          }
        }

        if (unrecognised.length > 0) {
          const fileMap = new Map(pdfs.map((f) => [f.name, f]));
          const salaryFiles = unrecognised
            .map((r) => fileMap.get(r.fileName))
            .filter((f): f is File => f !== undefined);

          if (salaryFiles.length > 0) {
            const startIdx = this.salaryQueue().length;
            const newItems: SalaryQueueItem[] = salaryFiles.map((f) => ({
              file: f,
              status: 'pending',
            }));
            this.salaryQueue.update((q) => [...q, ...newItems]);
            newItems.forEach((_, i) => this.processSalaryFile(startIdx + i));
          }
        }

        this.state.set('success');
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

  private processSalaryFile(idx: number): void {
    this.updateSalaryItem(idx, { status: 'uploading' });
    const item = this.salaryQueue()[idx];
    this.salaryService.uploadSlipPdf(item.file).subscribe({
      next: (res) => {
        this.updateSalaryItem(idx, {
          status: 'parsing',
          pdfPath: res.pdfPath,
          fileName: res.fileName,
        });
        this.salaryService.parsePdf(res.pdfPath).subscribe({
          next: (parsed) => this.updateSalaryItem(idx, { status: 'ready', parsed }),
          error: (err) =>
            this.updateSalaryItem(idx, {
              status: 'error',
              error:
                (typeof err.error === 'string' ? err.error : null) ??
                'Could not parse PDF - unsupported format?',
            }),
        });
      },
      error: () => this.updateSalaryItem(idx, { status: 'error', error: 'Upload failed.' }),
    });
  }

  private updateSalaryItem(idx: number, patch: Partial<SalaryQueueItem>): void {
    this.salaryQueue.update((q) => q.map((item, i) => (i === idx ? { ...item, ...patch } : item)));
  }

  reviewSalaryItem(idx: number): void {
    const item = this.salaryQueue()[idx];
    if (!item.parsed || !item.pdfPath) return;
    this.openSlipFromParsed(item.parsed, item.pdfPath, item.fileName ?? item.file.name, idx);
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

    const autoProfileId = this.profiles().length === 1 ? this.profiles()[0].id : null;
    this.slipProfileId.set(autoProfileId);

    const buildLineItems = (cats: SalaryItemCategory[]) => {
      this.itemCategories.set(cats);
      this.slipLineItems.set(
        parsed.lineItems.map((li, i) => {
          const catId =
            cats.find((c) => c.name.toLowerCase() === li.description.toLowerCase())?.id ?? null;
          return {
            salaryItemCategoryId: catId,
            amount: li.amount,
            sortOrder: i,
            hint: catId === null ? li.description : undefined,
            quantity: li.quantity,
            unitValue: li.unitValue,
            percentage: li.percentage,
            incidenciaBase: li.incidenciaBase,
          };
        }),
      );
      this.showSlipModal.set(true);
    };

    if (autoProfileId) {
      this.salaryService.getItemCategories(autoProfileId).subscribe(buildLineItems);
    } else {
      buildLineItems([]);
    }
  }

  onSlipProfileChange(profileId: number | null): void {
    const id = profileId ? +profileId : null;
    this.slipProfileId.set(id);
    if (id) {
      this.salaryService.getItemCategories(id).subscribe((cats) => {
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
      });
    } else {
      this.itemCategories.set([]);
    }
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
}
