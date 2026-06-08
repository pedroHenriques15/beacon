import { Component, inject, signal, computed, OnInit } from '@angular/core';
import { CurrencyPipe, DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { SalaryService } from '../../core/services/salary.service';
import {
  SalaryItemCategory,
  SalaryLineItem,
  SalaryProfile,
  SalarySlip,
} from '../../core/models/statement.model';
import { SalaryPieChartComponent } from './salary-pie-chart';

type View = 'overview' | 'profile' | 'slip';

interface LineItemDraft {
  salaryItemCategoryId: number | null;
  amount: number | null;
  sortOrder: number;
  quantity?: number | null;
  unitValue?: number | null;
  percentage?: number | null;
  incidenciaBase?: number | null;
}

@Component({
  selector: 'app-salary',
  standalone: true,
  imports: [CurrencyPipe, DecimalPipe, FormsModule, SalaryPieChartComponent],
  templateUrl: './salary.html',
  styleUrl: './salary.scss',
})
export class SalaryComponent implements OnInit {
  private svc = inject(SalaryService);

  view = signal<View>('overview');
  selectedProfileId = signal<number | null>(null);
  selectedSlipId = signal<number | null>(null);

  profiles = signal<SalaryProfile[]>([]);
  itemCategories = signal<SalaryItemCategory[]>([]);
  slips = signal<SalarySlip[]>([]);
  loading = signal(true);

  selectedProfile = computed(
    () => this.profiles().find((p) => p.id === this.selectedProfileId()) ?? null,
  );

  selectedSlip = computed(() => this.slips().find((s) => s.id === this.selectedSlipId()) ?? null);

  profileSlips = computed(() =>
    [...this.slips().filter((s) => s.salaryProfileId === this.selectedProfileId())].sort((a, b) =>
      b.period.localeCompare(a.period),
    ),
  );

  latestSlipPerProfile = computed(() => {
    const m = new Map<number, SalarySlip>();
    for (const s of this.slips()) {
      const cur = m.get(s.salaryProfileId);
      if (!cur || s.period > cur.period) m.set(s.salaryProfileId, s);
    }
    return m;
  });

  showSlipModal = signal(false);
  editingSlipId = signal<number | null>(null);
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

  showProfileModal = signal(false);
  profileModalMode = signal<'create' | 'edit'>('create');
  editingProfileId = signal<number | null>(null);
  profileName = signal('');
  profileDesc = signal('');
  profileLoading = signal(false);

  readonly itemTypes: Array<{ value: 'income' | 'deduction' | 'tax'; label: string }> = [
    { value: 'income', label: 'Income' },
    { value: 'deduction', label: 'Deduction' },
    { value: 'tax', label: 'Tax' },
  ];

  ngOnInit(): void {
    this.loadAll();
  }

  private loadAll(): void {
    this.loading.set(true);
    let done = 0;
    const check = () => {
      if (++done === 2) this.loading.set(false);
    };
    this.svc.getProfiles().subscribe({
      next: (v) => {
        this.profiles.set(v);
        check();
      },
      error: check,
    });
    this.svc.getSlips().subscribe({
      next: (v) => {
        this.slips.set(v);
        check();
      },
      error: check,
    });
  }

  enterProfile(profileId: number): void {
    this.selectedProfileId.set(profileId);
    this.view.set('profile');
    this.svc.getItemCategories(profileId).subscribe((cats) => this.itemCategories.set(cats));
  }

  exitProfile(): void {
    this.view.set('overview');
    this.selectedProfileId.set(null);
    this.itemCategories.set([]);
  }

  viewSlip(slipId: number): void {
    this.selectedSlipId.set(slipId);
    this.view.set('slip');
  }

  exitSlip(): void {
    this.selectedSlipId.set(null);
    this.view.set('profile');
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

  openSlipEdit(slip: SalarySlip): void {
    this.editingSlipId.set(slip.id);
    this.slipProfileId.set(slip.salaryProfileId);
    this.slipPeriod.set(slip.period.slice(0, 7));
    this.slipGross.set(slip.grossAmount);
    this.slipNet.set(slip.netAmount);
    this.slipNotes.set(slip.notes ?? '');
    this.slipLineItems.set(
      slip.lineItems.map((li) => ({
        salaryItemCategoryId: li.salaryItemCategoryId,
        amount: li.amount,
        sortOrder: li.sortOrder,
        quantity: li.quantity,
        unitValue: li.unitValue,
        percentage: li.percentage,
        incidenciaBase: li.incidenciaBase,
      })),
    );
    this.slipError.set('');
    this.slipPdfPath.set(slip.pdfPath ?? null);
    this.slipSourceFile.set(slip.sourceFile ?? null);
    this.slipBaseAmount.set(slip.baseAmount ?? null);
    this.slipHoursWorked.set(slip.hoursWorked ?? null);
    this.slipHourlyRate.set(slip.hourlyRate ?? null);
    this.slipTotalEspecie.set(slip.totalEspecie ?? null);
    this.showSlipModal.set(true);
  }

  onPdfSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';
    if (!file) return;
    this.svc.uploadSlipPdf(file).subscribe({
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
    this.svc.updateSlip(this.editingSlipId()!, body).subscribe({
      next: () => {
        this.slipLoading.set(false);
        this.showSlipModal.set(false);
        this.svc.getSlips().subscribe((v) => this.slips.set(v));
        this.svc.getProfiles().subscribe((v) => this.profiles.set(v));
      },
      error: (err) => {
        this.slipLoading.set(false);
        this.slipError.set(err.error ?? 'Save failed.');
      },
    });
  }

  deleteSlip(slip: SalarySlip): void {
    if (!confirm(`Delete salary slip for ${this.formatPeriod(slip.period)}?`)) return;
    this.svc.deleteSlip(slip.id).subscribe(() => {
      this.slips.update((s) => s.filter((x) => x.id !== slip.id));
      this.svc.getProfiles().subscribe((v) => this.profiles.set(v));
      if (this.view() === 'slip') this.exitSlip();
    });
  }

  openProfileCreate(): void {
    this.profileModalMode.set('create');
    this.editingProfileId.set(null);
    this.profileName.set('');
    this.profileDesc.set('');
    this.showProfileModal.set(true);
  }

  openProfileEdit(p: SalaryProfile): void {
    this.profileModalMode.set('edit');
    this.editingProfileId.set(p.id);
    this.profileName.set(p.name);
    this.profileDesc.set(p.description ?? '');
    this.showProfileModal.set(true);
  }

  submitProfile(): void {
    if (!this.profileName().trim()) return;
    this.profileLoading.set(true);
    const name = this.profileName().trim();
    const desc = this.profileDesc().trim() || undefined;
    const obs =
      this.profileModalMode() === 'create'
        ? this.svc.createProfile(name, desc)
        : this.svc.updateProfile(this.editingProfileId()!, name, desc);
    obs.subscribe({
      next: () => {
        this.profileLoading.set(false);
        this.showProfileModal.set(false);
        this.svc.getProfiles().subscribe((v) => this.profiles.set(v));
      },
      error: () => this.profileLoading.set(false),
    });
  }

  deleteProfile(p: SalaryProfile): void {
    if (!confirm(`Delete profile "${p.name}"? This will also delete all its salary slips.`)) return;
    this.svc.deleteProfile(p.id).subscribe(() => {
      this.profiles.update((list) => list.filter((x) => x.id !== p.id));
      this.slips.update((list) => list.filter((x) => x.salaryProfileId !== p.id));
      if (this.selectedProfileId() === p.id) this.exitProfile();
    });
  }

  lineItemCatName(id: number | null): string {
    return this.itemCategories().find((c) => c.id === id)?.name ?? '-';
  }

  netForLineItems(items: SalaryLineItem[]): number {
    return items.reduce((sum, li) => {
      const t = li.categoryItemType;
      return sum + (t === 'income' ? li.amount : -li.amount);
    }, 0);
  }

  netFromLineItems(slip: SalarySlip): number {
    if (!slip.lineItems?.length) return slip.netAmount;
    return slip.lineItems.reduce((sum, li) => {
      return sum + (li.categoryItemType === 'income' ? li.amount : -li.amount);
    }, 0);
  }

  private profileNameIncludes(slip: SalarySlip, term: string): boolean {
    return slip.profileName.toLowerCase().includes(term.toLowerCase());
  }

  isDominos(slip: SalarySlip): boolean {
    return this.profileNameIncludes(slip, 'domino') || this.profileNameIncludes(slip, 'domirest');
  }

  isKonkConsulting(slip: SalarySlip): boolean {
    return this.profileNameIncludes(slip, 'konk');
  }

  hoursWorkedLabel(slip: SalarySlip): string {
    return this.isDominos(slip) ? 'Hours Worked' : 'Days Worked';
  }

  workdaysInMonth(period: string): number {
    const d = new Date(period);
    const year = d.getFullYear();
    const month = d.getMonth();
    const daysInMonth = new Date(year, month + 1, 0).getDate();
    let count = 0;
    for (let day = 1; day <= daysInMonth; day++) {
      const weekday = new Date(year, month, day).getDay();
      if (weekday !== 0 && weekday !== 6) count++;
    }
    return count;
  }

  hasTrueHourlyRate(slip: SalarySlip): boolean {
    if (this.isDominos(slip)) return !!slip.hoursWorked && slip.hoursWorked > 0;
    if (this.isKonkConsulting(slip)) return true;
    return !!slip.hoursWorked && slip.hoursWorked > 0;
  }

  trueHourlyRate(slip: SalarySlip): number {
    const net = this.netFromLineItems(slip);
    if (this.isDominos(slip)) return net / slip.hoursWorked!;
    if (this.isKonkConsulting(slip)) return net / (this.workdaysInMonth(slip.period) * 8);
    return net / (slip.hoursWorked! * 8);
  }
}
