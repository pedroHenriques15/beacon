import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  OnInit,
  afterRenderEffect,
  computed,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { SalaryService } from '../../core/services/salary.service';
import {
  HourlyRateFormula,
  SalaryItemCategory,
  SalaryProfile,
  SalarySlip,
} from '../../core/models/statement.model';
import { ConfirmDialogComponent } from '../../core/components/confirm-dialog/confirm-dialog';
import { eur } from '../../core/utils/money';
import { monthName, monthYearLabel } from '../../core/utils/month-totals';
import { SlipFlowComponent } from './slip-flow';
import {
  defaultProfileId,
  firstShownIndex,
  periodKey,
  slipChips,
  slipLines,
  slipNet,
  slipTiles,
  takeHomeHistory,
} from './salary-figures';

/** Slips shown in the desktop picker before "Earlier" is pressed. */
const SLIP_PAGE = 12;

interface LineItemDraft {
  salaryItemCategoryId: number | null;
  amount: number | null;
  sortOrder: number;
  quantity?: number | null;
  unitValue?: number | null;
  percentage?: number | null;
  incidenciaBase?: number | null;
}

/** Scrolls the picker so the selected slip's chip sits in the middle. */
function centreSelectedChip(track: HTMLElement): void {
  const chip = track.querySelector<HTMLElement>('[aria-pressed="true"]');
  if (!chip) return;
  track.scrollLeft = chip.offsetLeft - (track.clientWidth - chip.offsetWidth) / 2;
}

@Component({
  selector: 'app-salary',
  standalone: true,
  imports: [FormsModule, RouterLink, ConfirmDialogComponent, SlipFlowComponent],
  templateUrl: './salary.html',
  changeDetection: ChangeDetectionStrategy.Eager,
  styleUrl: './salary.scss',
})
export class SalaryComponent implements OnInit {
  private svc = inject(SalaryService);

  readonly eur = eur;
  readonly monthName = monthName;
  readonly monthYearLabel = monthYearLabel;
  readonly periodKey = periodKey;
  readonly netOf = slipNet;

  profiles = signal<SalaryProfile[]>([]);
  itemCategories = signal<SalaryItemCategory[]>([]);
  slips = signal<SalarySlip[]>([]);
  loading = signal(true);

  confirmPending = signal<{ message: string; action: () => void } | null>(null);

  /** The segmented control's choice: a profile, 'all' for the overview, or null for the default. */
  private profileChoice = signal<number | 'all' | null>(null);
  /** The slip picked in the picker; null shows the profile's latest. */
  selectedSlipId = signal<number | null>(null);
  private slipPages = signal(1);
  /** The profile whose item categories are loaded or loading. */
  private categoriesFor: number | null = null;

  /** The profile shown, or null for the overview of every profile. */
  activeProfileId = computed<number | null>(() => {
    const choice = this.profileChoice();
    if (choice === 'all') return null;
    if (choice !== null && this.profiles().some((p) => p.id === choice)) return choice;
    return defaultProfileId(this.profiles(), this.slips());
  });

  view = computed<'empty' | 'overview' | 'profile'>(() => {
    if (this.profiles().length === 0) return 'empty';
    return this.activeProfileId() === null ? 'overview' : 'profile';
  });

  selectedProfile = computed(
    () => this.profiles().find((p) => p.id === this.activeProfileId()) ?? null,
  );

  /** The shown profile's slips, newest first. */
  profileSlips = computed(() =>
    this.slips()
      .filter((s) => s.salaryProfileId === this.activeProfileId())
      .sort((a, b) => b.period.localeCompare(a.period)),
  );

  /** The same slips, oldest first, as the picker and the history read them. */
  slipTimeline = computed(() => [...this.profileSlips()].reverse());

  selectedSlip = computed(() => {
    const slips = this.profileSlips();
    return slips.find((s) => s.id === this.selectedSlipId()) ?? slips[0] ?? null;
  });

  private profileFormulaMap = computed(
    () => new Map(this.profiles().map((p) => [p.id, p.hourlyRateFormula])),
  );

  latestSlipPerProfile = computed(() => {
    const m = new Map<number, SalarySlip>();
    for (const s of this.slips()) {
      const cur = m.get(s.salaryProfileId);
      if (!cur || s.period > cur.period) m.set(s.salaryProfileId, s);
    }
    return m;
  });

  /** One card per profile on the overview: its latest slip and how that slip split. */
  overviewCards = computed(() => {
    const latest = this.latestSlipPerProfile();
    return this.profiles().map((profile) => {
      const slip = latest.get(profile.id) ?? null;
      const net = slip ? slipNet(slip) : 0;
      const whole = slip ? Math.max(slip.grossAmount, net) : 0;
      const netPct = whole > 0 ? Math.round(Math.max(0, Math.min(1, net / whole)) * 100) : 0;
      return { profile, slip, net, netPct };
    });
  });

  chipFirstShown = computed(() => {
    const timeline = this.slipTimeline();
    const selectedId = this.selectedSlip()?.id;
    const at = timeline.findIndex((s) => s.id === selectedId);
    return firstShownIndex(timeline.length, at, this.slipPages(), SLIP_PAGE);
  });

  chips = computed(() =>
    slipChips(this.slipTimeline(), this.selectedSlip()?.id ?? null, this.chipFirstShown()),
  );

  tiles = computed(() => {
    const slip = this.selectedSlip();
    if (!slip) return [];
    const timeline = this.slipTimeline();
    return slipTiles(slip, this.formulaFor(slip), timeline.length, timeline[0]?.period ?? null);
  });

  lines = computed(() => slipLines(this.selectedSlip() ?? { lineItems: [] }));

  history = computed(() => takeHomeHistory(this.slipTimeline(), this.selectedSlip()?.id ?? null));

  private readonly slipTrack = viewChild<ElementRef<HTMLElement>>('slipTrack');

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
  profileFormula = signal<HourlyRateFormula>('days');
  profileLoading = signal(false);

  readonly itemTypes: Array<{ value: 'income' | 'deduction' | 'tax'; label: string }> = [
    { value: 'income', label: 'Income' },
    { value: 'deduction', label: 'Deduction' },
    { value: 'tax', label: 'Tax' },
  ];

  constructor() {
    // A profile's picker opens on its selected slip, the newest, which sits at the right end.
    afterRenderEffect(() => {
      this.activeProfileId();
      const track = this.slipTrack()?.nativeElement;
      if (track) centreSelectedChip(track);
    });
  }

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

  showOverview(): void {
    this.profileChoice.set('all');
    this.selectedSlipId.set(null);
  }

  enterProfile(profileId: number): void {
    if (this.activeProfileId() !== profileId) {
      this.selectedSlipId.set(null);
      this.slipPages.set(1);
    }
    this.profileChoice.set(profileId);
  }

  viewSlip(slipId: number): void {
    this.selectedSlipId.set(slipId);
  }

  showEarlierSlips(): void {
    this.slipPages.update((p) => p + 1);
  }

  formatPeriod(period: string): string {
    return period ? monthYearLabel(periodKey(period)) : '';
  }

  /** A slip's item categories of one type; only the slip's own profile's (ADR-009). */
  catsByType(type: string): SalaryItemCategory[] {
    return this.slipCategories().filter((c) => c.itemType === type);
  }

  private slipCategories(): SalaryItemCategory[] {
    const profileId = this.slipProfileId();
    return this.itemCategories().filter((c) => c.profileId === profileId);
  }

  private loadItemCategories(profileId: number): void {
    if (this.categoriesFor === profileId) return;
    this.categoriesFor = profileId;
    this.itemCategories.set([]);
    this.svc.getItemCategories(profileId).subscribe({
      next: (cats) => {
        // A late answer for another profile must not fill this slip's category lists.
        if (this.categoriesFor === profileId) this.itemCategories.set(cats);
      },
      error: () => {
        if (this.categoriesFor === profileId) this.categoriesFor = null;
      },
    });
  }

  openSlipEdit(slip: SalarySlip): void {
    this.loadItemCategories(slip.salaryProfileId);
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

  removePdf(): void {
    this.slipPdfPath.set(null);
    this.slipSourceFile.set(null);
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
    // Salary item categories belong to one profile (ADR-009): once this profile's are loaded,
    // refuse a line that points elsewhere. The API checks the same.
    const allowed = new Set(this.slipCategories().map((c) => c.id));
    if (allowed.size > 0 && lineItems.some((li) => !allowed.has(li.salaryItemCategoryId))) {
      this.slipError.set('Every line item needs a category of this slip’s profile.');
      return;
    }
    this.slipLoading.set(true);
    this.slipError.set('');
    const body = {
      period: this.slipPeriod() + '-01',
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
        this.slipError.set(typeof err.error === 'string' && err.error ? err.error : 'Save failed.');
      },
    });
  }

  deleteSlip(slip: SalarySlip): void {
    this.confirmPending.set({
      message: `Delete salary slip for ${this.formatPeriod(slip.period)}?`,
      action: () =>
        this.svc.deleteSlip(slip.id).subscribe(() => {
          this.slips.update((s) => s.filter((x) => x.id !== slip.id));
          this.svc.getProfiles().subscribe((v) => this.profiles.set(v));
          if (this.selectedSlipId() === slip.id) this.selectedSlipId.set(null);
        }),
    });
  }

  openProfileCreate(): void {
    this.profileModalMode.set('create');
    this.editingProfileId.set(null);
    this.profileName.set('');
    this.profileDesc.set('');
    this.profileFormula.set('days');
    this.showProfileModal.set(true);
  }

  openProfileEdit(p: SalaryProfile): void {
    this.profileModalMode.set('edit');
    this.editingProfileId.set(p.id);
    this.profileName.set(p.name);
    this.profileDesc.set(p.description ?? '');
    this.profileFormula.set(p.hourlyRateFormula ?? 'days');
    this.showProfileModal.set(true);
  }

  submitProfile(): void {
    if (!this.profileName().trim()) return;
    this.profileLoading.set(true);
    const name = this.profileName().trim();
    const desc = this.profileDesc().trim() || undefined;
    const obs =
      this.profileModalMode() === 'create'
        ? this.svc.createProfile(name, desc, this.profileFormula())
        : this.svc.updateProfile(this.editingProfileId()!, name, desc, this.profileFormula());
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
    this.confirmPending.set({
      message: `Delete profile "${p.name}"? This will also delete all its salary slips.`,
      action: () =>
        this.svc.deleteProfile(p.id).subscribe(() => {
          this.profiles.update((list) => list.filter((x) => x.id !== p.id));
          this.slips.update((list) => list.filter((x) => x.salaryProfileId !== p.id));
          if (this.activeProfileId() === p.id) this.profileChoice.set(null);
        }),
    });
  }

  onConfirmPending(): void {
    const pending = this.confirmPending();
    this.confirmPending.set(null);
    pending?.action();
  }

  private formulaFor(slip: SalarySlip): HourlyRateFormula {
    return this.profileFormulaMap().get(slip.salaryProfileId) ?? 'days';
  }
}
