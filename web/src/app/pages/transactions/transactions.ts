import {
  Component,
  inject,
  signal,
  computed,
  HostListener,
  OnInit,
  OnDestroy,
  effect,
  ChangeDetectionStrategy,
  ElementRef,
  viewChild,
} from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { Subscription } from 'rxjs';
import { FinanceService, EnrichedTransaction } from '../../core/services/finance.service';
import { Transaction } from '../../core/models/statement.model';
import { CategoriesService } from '../../core/services/categories.service';
import { GroceriesService } from '../../core/services/groceries.service';
import { GroceryCategoriesService } from '../../core/services/grocery-categories.service';
import { GroceryItem, GroceryReceiptSummary } from '../../core/models/grocery.model';
import { matchesRule } from '../../core/utils/rule-match';
import { availableMonths } from '../../core/utils/date-utils';
import { CATEGORY_EXCLUDED } from '../../core/constants/categories';
import { MonthScrubberComponent } from '../../core/components/month-scrubber/month-scrubber';
import { monthCells, monthYearLabel } from '../../core/utils/month-totals';
import { eur, signedEur } from '../../core/utils/money';
import { bankInitials } from '../../core/utils/bank';
import { CategoryPickerComponent, PICKER_SIZE } from './category-picker';
import {
  ItemFilter,
  TxFilter,
  countedAmount,
  dayGroups,
  flowTotals,
  isExcludedItem,
  isExcludedTx,
  matchesItemFilter,
  matchesTxFilter,
  mostBought,
  popoverPosition,
  storeTotals,
  totalsByBank,
  txAmountText,
  withMonths,
} from './activity';

interface PendingChange {
  tx: EnrichedTransaction;
  newCategoryId: number | null;
  ruleId: number;
  pattern: string;
}

interface PendingRuleCreate {
  tx: EnrichedTransaction;
  categoryId: number;
  categoryName: string;
}

type SortCol = 'date' | 'bank' | 'description' | 'category' | 'amount' | 'balance';

interface GPendingChange {
  item: GroceryItem;
  newCategoryId: number | null;
  ruleId: number;
  pattern: string;
}

interface GPendingRuleCreate {
  item: GroceryItem;
  categoryId: number;
  categoryName: string;
}

type GrocerySortCol = 'date' | 'store' | 'description' | 'category' | 'amount' | 'quantity';

/** The sort control's choices, as 'column-direction'. */
const TX_SORTS: { key: `${SortCol}-${'asc' | 'desc'}`; label: string }[] = [
  { key: 'date-desc', label: 'Newest first' },
  { key: 'date-asc', label: 'Oldest first' },
  { key: 'amount-desc', label: 'Largest first' },
  { key: 'amount-asc', label: 'Smallest first' },
  { key: 'description-asc', label: 'Description, A to Z' },
  { key: 'category-asc', label: 'Category, A to Z' },
  { key: 'bank-asc', label: 'Bank, A to Z' },
  { key: 'balance-desc', label: 'Balance, highest first' },
];

const ITEM_SORTS: { key: `${GrocerySortCol}-${'asc' | 'desc'}`; label: string }[] = [
  { key: 'date-desc', label: 'Newest first' },
  { key: 'date-asc', label: 'Oldest first' },
  { key: 'amount-desc', label: 'Largest first' },
  { key: 'amount-asc', label: 'Smallest first' },
  { key: 'description-asc', label: 'Description, A to Z' },
  { key: 'category-asc', label: 'Category, A to Z' },
  { key: 'store-asc', label: 'Store, A to Z' },
  { key: 'quantity-desc', label: 'Quantity, most first' },
];

/** The row actions menu, for placing it next to its button. */
const MENU_SIZE = { width: 230, height: 5 * 44 + 14 };

interface Popover {
  top: number;
  left: number;
}

@Component({
  selector: 'app-transactions',
  standalone: true,
  imports: [DatePipe, FormsModule, MonthScrubberComponent, CategoryPickerComponent],
  templateUrl: './transactions.html',
  changeDetection: ChangeDetectionStrategy.Eager,
  styleUrl: './transactions.scss',
})
export class TransactionsComponent implements OnInit, OnDestroy {
  private route = inject(ActivatedRoute);
  finance = inject(FinanceService);
  catSvc = inject(CategoriesService);
  groceriesSvc = inject(GroceriesService);
  groceryCatSvc = inject(GroceryCategoriesService);

  private static readonly dates = new DatePipe('en-US');

  readonly eur = eur;
  readonly signedEur = signedEur;
  readonly bankInitials = bankInitials;
  readonly txAmountText = txAmountText;
  readonly isExcludedTx = isExcludedTx;
  readonly isExcludedItem = isExcludedItem;
  readonly txSorts = TX_SORTS;
  readonly itemSorts = ITEM_SORTS;

  activeTab = signal<'transactions' | 'groceries'>('transactions');

  filterBank = signal('');
  filterMonth = signal('');
  filterType = signal('');
  filterCategory = signal('');
  search = signal('');

  sortCol = signal<SortCol>('date');
  sortDir = signal<'asc' | 'desc'>('desc');

  loadedItems = signal<EnrichedTransaction[]>([]);
  totalCount = signal(0);
  pageLoading = signal(false);
  fetchError = signal('');
  gFetchError = signal('');
  actionError = signal('');
  modalError = signal('');
  private _skip = 0;
  private _visibleTarget = 20;
  private _filtersReady = false;
  private _currentSub?: Subscription;

  /** The category picker, row actions menu and review open at the moment (one at a time). */
  txPicker = signal<({ tx: EnrichedTransaction } & Popover) | null>(null);
  itemPicker = signal<({ item: GroceryItem } & Popover) | null>(null);
  txMenu = signal<({ tx: EnrichedTransaction } & Popover) | null>(null);
  itemMenu = signal<({ item: GroceryItem } & Popover) | null>(null);
  reviewing = signal<'tx' | 'item' | null>(null);
  private reviewed = signal<ReadonlySet<number>>(new Set());
  private reviewTotal = signal(0);
  private reviewAnchor: Popover = { top: 0, left: 0 };
  /** The button that opened the picker or menu, focused again when Escape closes it. */
  private lastTrigger: HTMLElement | null = null;
  private readonly rowMenu = viewChild<ElementRef<HTMLElement>>('rowMenu');
  reviewProgress = computed(() => {
    const total = this.reviewTotal();
    return `${Math.min(this.reviewed().size + 1, total)} of ${total}`;
  });

  // Excluding is its own action (it also sets isExcluded), so the Excluded category is never
  // offered as a plain category pick.
  assignableCats = computed(() =>
    this.catSvc.categories().filter((c) => c.name !== CATEGORY_EXCLUDED),
  );
  excludedCategoryId = computed(
    () => this.catSvc.categories().find((c) => c.name === CATEGORY_EXCLUDED)?.id ?? null,
  );
  pendingChange = signal<PendingChange | null>(null);

  pendingRuleCreate = signal<PendingRuleCreate | null>(null);
  ruleCreatePattern = signal('');
  ruleCreateWhole = signal(true);
  ruleCreateValue = signal<number | null>(null);
  ruleCreateLoading = signal(false);

  pendingExclude = signal<{ tx: EnrichedTransaction } | null>(null);
  excludeRulePattern = signal('');
  excludeRuleWhole = signal(true);
  excludeRuleValue = signal<number | null>(null);
  excludeRuleLoading = signal(false);

  gPendingExclude = signal<{ item: GroceryItem } | null>(null);
  gExcludeRulePattern = signal('');
  gExcludeRuleWhole = signal(true);
  gExcludeRuleValue = signal<number | null>(null);
  gExcludeRuleLoading = signal(false);

  showCreateModal = signal(false);
  createTx = signal<EnrichedTransaction | null>(null);
  createName = signal('');
  createColor = signal('#a855f7');
  createPattern = signal('');
  createWhole = signal(true);
  createValue = signal<number | null>(null);
  createLoading = signal(false);

  ruleCreateMatchCount = computed(() => {
    const pat = this.ruleCreatePattern().trim();
    const val = this.ruleCreateValue();
    const whole = this.ruleCreateWhole();
    if (!pat && val === null) return null;
    return this.finance.allTransactions().filter((tx) => matchesRule(tx, pat, val, whole)).length;
  });

  excludeRuleMatchCount = computed(() => {
    const pat = this.excludeRulePattern().trim();
    const val = this.excludeRuleValue();
    const whole = this.excludeRuleWhole();
    if (!pat && val === null) return null;
    return this.finance.allTransactions().filter((tx) => matchesRule(tx, pat, val, whole)).length;
  });

  gExcludeRuleMatchCount = computed(() => {
    const pat = this.gExcludeRulePattern().trim();
    const val = this.gExcludeRuleValue();
    const whole = this.gExcludeRuleWhole();
    if (!pat && val === null) return null;
    return this.groceriesSvc.countedItems().filter((i) => matchesRule(i, pat, val, whole)).length;
  });

  createMatchCount = computed(() => {
    const pat = this.createPattern().trim();
    const val = this.createValue();
    const whole = this.createWhole();
    if (!pat && val === null) return null;
    return this.finance.allTransactions().filter((tx) => matchesRule(tx, pat, val, whole)).length;
  });

  confirmDeleteTx = signal<EnrichedTransaction | null>(null);

  /** Select mode shows a checkbox on every row and the bulk actions. */
  selectMode = signal(false);
  selectedIds = signal<Set<number>>(new Set());
  hasSelection = computed(() => this.selectedIds().size > 0);
  allSelected = computed(
    () =>
      this.filtered().length > 0 && this.filtered().every((tx) => this.selectedIds().has(tx.id)),
  );
  confirmBulkDelete = signal(false);

  showTxModal = signal(false);
  private _editingTxRef = signal<EnrichedTransaction | null>(null);
  editingTxId = signal<number | null>(null);
  txDatePosting = signal('');
  txDateValue = signal('');
  txDescription = signal('');
  txAmount = signal<number | null>(null);
  txType = signal<'credit' | 'debit'>('debit');
  txBalance = signal<number | null>(null);
  txCategoryId = signal<number | null>(null);
  txLoading = signal(false);

  txFormValid = computed(() => {
    const amount = this.txAmount();
    const balance = this.txBalance();
    return !!(
      this.txDatePosting() &&
      this.txDateValue() &&
      this.txDescription().trim() &&
      amount !== null &&
      !isNaN(amount) &&
      balance !== null &&
      !isNaN(balance)
    );
  });

  filtered = computed<EnrichedTransaction[]>(() => {
    const items = this.loadedItems();
    const col = this.sortCol(),
      dir = this.sortDir();
    return [...items].sort((a, b) => {
      let cmp = 0;
      switch (col) {
        case 'date':
          cmp = a.datePosting.localeCompare(b.datePosting);
          break;
        case 'bank':
          cmp = a.bank.localeCompare(b.bank);
          break;
        case 'description':
          cmp = a.description.localeCompare(b.description);
          break;
        case 'category':
          cmp = (a.category?.name ?? 'zzz').localeCompare(b.category?.name ?? 'zzz');
          break;
        case 'amount':
          cmp = a.amount - b.amount;
          break;
        case 'balance':
          cmp = a.balance - b.balance;
          break;
      }
      return dir === 'asc' ? cmp : -cmp;
    });
  });

  hasMore = computed(() => this.loadedItems().length < this.totalCount());

  sortKey = computed(() => `${this.sortCol()}-${this.sortDir()}`);
  sortLabel = computed(
    () => TX_SORTS.find((s) => s.key === this.sortKey())?.label.toLowerCase() ?? '',
  );

  /** The loaded rows as a timeline: by day when sorted by date, else one undated run. */
  txDays = computed(() =>
    dayGroups(this.filtered(), this.sortCol() === 'date', (tx) => tx.datePosting, countedAmount),
  );

  private txFilter = computed<TxFilter>(() => ({
    bank: this.filterBank(),
    month: this.filterMonth(),
    type: this.filterType(),
    category: this.filterCategory(),
    search: this.search(),
  }));

  /** Every counted row (never an excluded one) that matches the filters, loaded or not. */
  private txScope = computed(() => {
    const f = this.txFilter();
    return this.finance.allTransactions().filter((tx) => matchesTxFilter(tx, f));
  });

  /** In, Out and Kept for the filters (invariant 4: counted rows only). */
  txTotals = computed(() => flowTotals(this.txScope()));
  bankTotals = computed(() => totalsByBank(this.txScope()));

  /** The filters' rows, whatever category is picked, that have none: the review queue. */
  txNeedsCategory = computed(() => {
    const f = { ...this.txFilter(), category: '' };
    return this.finance
      .allTransactions()
      .filter((tx) => tx.categoryId === null && matchesTxFilter(tx, f));
  });

  /**
   * The rows In, Out and Kept leave out, to list them: excluded rows (which only
   * allTransactionsRaw holds) and rows of an unclassified type. None is added to any total.
   */
  txLeftOut = computed(() => {
    const f = this.txFilter();
    const excluded = this.finance
      .allTransactionsRaw()
      .filter((tx) => isExcludedTx(tx) && matchesTxFilter(tx, f));
    const unclassified = this.txScope().filter((tx) => tx.type === 'unknown');
    const rows = [...excluded, ...unclassified].sort((a, b) =>
      b.datePosting.localeCompare(a.datePosting),
    );
    return {
      excluded: excluded.length,
      unclassified: unclassified.length,
      rows: rows.slice(0, 4),
      more: Math.max(0, rows.length - 4),
    };
  });

  txLeftOutNote = computed(() => {
    const { excluded, unclassified } = this.txLeftOut();
    const parts: string[] = [];
    if (excluded > 0) parts.push(`${excluded} excluded`);
    if (unclassified > 0) parts.push(`${unclassified} of an unclassified type`);
    const them = excluded + unclassified === 1 ? 'it' : 'them';
    return `${parts.join(' and ')}: In, Out and Kept leave ${them} out.`;
  });

  availableMonths = computed(() => availableMonths(this.finance.allTransactionsRaw()));

  /** The month the active tab shows, '' for all months. */
  activeMonth = computed(() =>
    this.activeTab() === 'transactions' ? this.filterMonth() : this.gFilterMonth(),
  );

  /** The scrubber's months, plus any month the active tab has rows in. */
  scrubberMonths = computed(() => {
    const extra =
      this.activeTab() === 'transactions' ? this.availableMonths() : this.gAvailableMonths();
    const selected = this.activeMonth();
    return withMonths(
      monthCells(this.finance.monthTotals()),
      selected ? [...extra, selected] : extra,
    );
  });

  heading = computed(() => {
    const month = this.activeMonth();
    return month ? `${monthYearLabel(month)} activity` : 'Activity';
  });

  /** Dates in the timeline carry the year when it spans every month. */
  dayFormat = computed(() => (this.activeMonth() ? 'd MMM' : 'd MMM y'));

  /** What the totals cover: 'September 2026, BPI, filtered'. */
  scopeLabel = computed(() => {
    const onTx = this.activeTab() === 'transactions';
    const month = this.activeMonth();
    const where = onTx ? this.filterBank() : this.gFilterStore();
    const narrowed = onTx
      ? !!(this.filterType() || this.filterCategory() || this.search())
      : !!(this.gFilterCategory() || this.gSearch());
    return [month ? monthYearLabel(month) : 'All months', where, narrowed ? 'filtered' : '']
      .filter(Boolean)
      .join(', ');
  });

  gFilterStore = signal('');
  gFilterMonth = signal('');
  gFilterCategory = signal('');
  gSearch = signal('');

  gSortCol = signal<GrocerySortCol>('date');
  gSortDir = signal<'asc' | 'desc'>('desc');

  gLoadedItems = signal<GroceryItem[]>([]);
  gTotalCount = signal(0);
  gPageLoading = signal(false);
  private _gSkip = 0;
  private _gVisibleTarget = 20;
  private _gFiltersReady = false;
  private _gCurrentSub?: Subscription;
  private _paramsSub?: Subscription;

  // Same as assignableCats: excluding an item is its own action, not a category pick.
  gAssignableCats = computed(() =>
    this.groceryCatSvc.categories().filter((c) => c.name !== CATEGORY_EXCLUDED),
  );
  gExcludedCategoryId = computed(
    () => this.groceryCatSvc.categories().find((c) => c.name === CATEGORY_EXCLUDED)?.id ?? null,
  );

  gPendingChange = signal<GPendingChange | null>(null);

  gPendingRuleCreate = signal<GPendingRuleCreate | null>(null);
  gRuleCreatePattern = signal('');
  gRuleCreateWhole = signal(true);
  gRuleCreateValue = signal<number | null>(null);
  gRuleCreateLoading = signal(false);

  gShowCreateModal = signal(false);
  gCreateItem = signal<GroceryItem | null>(null);
  gCreateName = signal('');
  gCreateColor = signal('#a855f7');
  gCreatePattern = signal('');
  gCreateWhole = signal(true);
  gCreateValue = signal<number | null>(null);
  gCreateLoading = signal(false);

  gRuleCreateMatchCount = computed(() => {
    const pat = this.gRuleCreatePattern().trim();
    const val = this.gRuleCreateValue();
    const whole = this.gRuleCreateWhole();
    if (!pat && val === null) return null;
    return this.groceriesSvc.countedItems().filter((item) => matchesRule(item, pat, val, whole))
      .length;
  });

  gCreateMatchCount = computed(() => {
    const pat = this.gCreatePattern().trim();
    const val = this.gCreateValue();
    const whole = this.gCreateWhole();
    if (!pat && val === null) return null;
    return this.groceriesSvc.countedItems().filter((item) => matchesRule(item, pat, val, whole))
      .length;
  });

  gConfirmDeleteItem = signal<GroceryItem | null>(null);
  gConfirmDeleteReceipt = signal<GroceryReceiptSummary | null>(null);

  gShowItemModal = signal(false);
  gEditingItemId = signal<number | null>(null);
  gItemDescription = signal('');
  gItemAmount = signal<number | null>(null);
  gItemQuantity = signal<number>(1);
  gItemLoading = signal(false);

  gShowCreateItemModal = signal(false);
  gNewItemReceiptId = signal<number | null>(null);
  gNewItemDescription = signal('');
  gNewItemAmount = signal<number | null>(null);
  gNewItemQuantity = signal<number>(1);
  gNewItemLoading = signal(false);

  gItemFormValid = computed(() => {
    const amount = this.gItemAmount();
    return !!(this.gItemDescription().trim() && amount !== null && !isNaN(amount) && amount >= 0);
  });

  gNewItemFormValid = computed(() => {
    const amount = this.gNewItemAmount();
    return !!(
      this.gNewItemReceiptId() !== null &&
      this.gNewItemDescription().trim() &&
      amount !== null &&
      !isNaN(amount) &&
      amount >= 0
    );
  });

  gAvailableMonths = computed(() => {
    const months = this.groceriesSvc.allItems().map((item) => item.receiptDate.slice(0, 7));
    return [...new Set(months)].sort().reverse();
  });

  gSortKey = computed(() => `${this.gSortCol()}-${this.gSortDir()}`);
  gSortLabel = computed(
    () => ITEM_SORTS.find((s) => s.key === this.gSortKey())?.label.toLowerCase() ?? '',
  );

  private gFilter = computed<ItemFilter>(() => ({
    store: this.gFilterStore(),
    month: this.gFilterMonth(),
    category: this.gFilterCategory(),
    search: this.gSearch(),
  }));

  /** Every counted item (invariant 4: never an excluded one) that matches the filters. */
  private gScope = computed(() => {
    const f = this.gFilter();
    return this.groceriesSvc.countedItems().filter((item) => matchesItemFilter(item, f));
  });

  gSummary = computed(() => {
    const items = this.gScope();
    return {
      amount: items.reduce((sum, item) => sum + item.amount, 0),
      items: items.length,
      receipts: new Set(items.map((item) => item.receiptId)).size,
    };
  });
  gStores = computed(() => storeTotals(this.gScope()));
  gTopItems = computed(() => mostBought(this.gScope()));

  /** The filters' items, whatever category is picked, that have none: the review queue. */
  gNeedsCategory = computed(() => {
    const f = { ...this.gFilter(), category: '' };
    return this.groceriesSvc
      .countedItems()
      .filter((item) => item.categoryId === null && matchesItemFilter(item, f));
  });

  /** The excluded items the total leaves out, to list them (allItems holds them); never summed. */
  gLeftOut = computed(() => {
    const f = this.gFilter();
    const rows = this.groceriesSvc
      .allItems()
      .filter((item) => isExcludedItem(item) && matchesItemFilter(item, f));
    return { count: rows.length, rows: rows.slice(0, 4) };
  });

  gFiltered = computed<GroceryItem[]>(() => {
    let items = this.gLoadedItems();
    if (this.gFilterCategory() === 'unknown') {
      items = items.filter((item) => item.categoryId === null);
    }
    const col = this.gSortCol(),
      dir = this.gSortDir();
    return [...items].sort((a, b) => {
      let cmp = 0;
      switch (col) {
        case 'date':
          cmp = a.receiptDate.localeCompare(b.receiptDate);
          break;
        case 'store':
          cmp = a.storeName.localeCompare(b.storeName);
          break;
        case 'description':
          cmp = a.description.localeCompare(b.description);
          break;
        case 'category':
          cmp = (a.categoryName ?? 'zzz').localeCompare(b.categoryName ?? 'zzz');
          break;
        case 'amount':
          cmp = a.amount - b.amount;
          break;
        case 'quantity':
          cmp = a.quantity - b.quantity;
          break;
      }
      return dir === 'asc' ? cmp : -cmp;
    });
  });

  gHasMore = computed(() => this.gLoadedItems().length < this.gTotalCount());

  gDays = computed(() =>
    dayGroups(
      this.gFiltered(),
      this.gSortCol() === 'date',
      (item) => item.receiptDate,
      (item) => (isExcludedItem(item) ? null : item.amount),
    ),
  );

  @HostListener('document:click')
  onDocumentClick(): void {
    this.closePopovers();
  }

  @HostListener('document:keydown.escape')
  onEscape(): void {
    const open = this.txPicker() || this.itemPicker() || this.txMenu() || this.itemMenu();
    this.closePopovers();
    if (open) this.lastTrigger?.focus();
  }

  /** Closes the category picker and the actions menu, and ends a review. */
  closePopovers(): void {
    this.txPicker.set(null);
    this.itemPicker.set(null);
    this.txMenu.set(null);
    this.itemMenu.set(null);
    this.reviewing.set(null);
  }

  private anchorFor(e: Event, size: { width: number; height: number }, alignRight = false) {
    const el = e.currentTarget as HTMLElement;
    // From the menu, focus goes back to the row's menu button, not to the menu that closes.
    if (!el.closest('.row-menu')) this.lastTrigger = el;
    const rect = el.getBoundingClientRect();
    return popoverPosition(
      rect,
      size,
      { width: window.innerWidth, height: window.innerHeight },
      alignRight,
    );
  }

  openTxPicker(tx: EnrichedTransaction, e: MouseEvent): void {
    e.stopPropagation();
    const isOpen = this.txPicker()?.tx.id === tx.id && !this.reviewing();
    this.closePopovers();
    if (!isOpen) this.txPicker.set({ tx, ...this.anchorFor(e, PICKER_SIZE) });
  }

  openItemPicker(item: GroceryItem, e: MouseEvent): void {
    e.stopPropagation();
    const isOpen = this.itemPicker()?.item.id === item.id && !this.reviewing();
    this.closePopovers();
    if (!isOpen) this.itemPicker.set({ item, ...this.anchorFor(e, PICKER_SIZE) });
  }

  openTxMenu(tx: EnrichedTransaction, e: MouseEvent): void {
    e.stopPropagation();
    const isOpen = this.txMenu()?.tx.id === tx.id;
    this.closePopovers();
    if (!isOpen) this.txMenu.set({ tx, ...this.anchorFor(e, MENU_SIZE, true) });
  }

  openItemMenu(item: GroceryItem, e: MouseEvent): void {
    e.stopPropagation();
    const isOpen = this.itemMenu()?.item.id === item.id;
    this.closePopovers();
    if (!isOpen) this.itemMenu.set({ item, ...this.anchorFor(e, MENU_SIZE, true) });
  }

  /** Steps through the rows without a category, newest first, one picker at a time. */
  startReview(kind: 'tx' | 'item', e: MouseEvent): void {
    e.stopPropagation();
    this.closePopovers();
    const total = kind === 'tx' ? this.txNeedsCategory().length : this.gNeedsCategory().length;
    if (total === 0) return;
    this.reviewAnchor = this.anchorFor(e, PICKER_SIZE);
    this.reviewed.set(new Set());
    this.reviewTotal.set(total);
    this.reviewing.set(kind);
    this.nextReview();
  }

  skipReview(): void {
    const id = this.txPicker()?.tx.id ?? this.itemPicker()?.item.id;
    this.txPicker.set(null);
    this.itemPicker.set(null);
    if (id !== undefined) this.reviewedRow(id);
  }

  /** During a review, marks a row done (categorised or skipped) and opens the next one. */
  private reviewedRow(id: number): void {
    if (!this.reviewing()) return;
    this.reviewed.update((done) => new Set(done).add(id));
    this.nextReview();
  }

  private nextReview(): void {
    const done = this.reviewed();
    if (this.reviewing() === 'tx') {
      const tx = this.txNeedsCategory().find((t) => !done.has(t.id));
      if (tx) {
        this.txPicker.set({ tx, ...this.reviewAnchor });
        return;
      }
    } else if (this.reviewing() === 'item') {
      const item = this.gNeedsCategory().find((i) => !done.has(i.id));
      if (item) {
        this.itemPicker.set({ item, ...this.reviewAnchor });
        return;
      }
    }
    this.reviewing.set(null);
  }

  setTab(tab: 'transactions' | 'groceries'): void {
    this.closePopovers();
    this.activeTab.set(tab);
  }

  /** The scrubber sets the month of whichever tab is showing. */
  setMonth(month: string): void {
    if (this.activeTab() === 'transactions') this.filterMonth.set(month);
    else this.gFilterMonth.set(month);
  }

  setSort(key: string): void {
    const sort = TX_SORTS.find((s) => s.key === key);
    if (!sort) return;
    const [col, dir] = sort.key.split('-') as [SortCol, 'asc' | 'desc'];
    this.sortCol.set(col);
    this.sortDir.set(dir);
  }

  gSetSort(key: string): void {
    const sort = ITEM_SORTS.find((s) => s.key === key);
    if (!sort) return;
    const [col, dir] = sort.key.split('-') as [GrocerySortCol, 'asc' | 'desc'];
    this.gSortCol.set(col);
    this.gSortDir.set(dir);
  }

  toggleSelectMode(): void {
    this.selectMode.update((on) => !on);
    if (!this.selectMode()) this.clearSelection();
  }

  toggleNeedsFilter(): void {
    this.filterCategory.set(this.filterCategory() === 'unknown' ? '' : 'unknown');
  }

  gToggleNeedsFilter(): void {
    this.gFilterCategory.set(this.gFilterCategory() === 'unknown' ? '' : 'unknown');
  }

  toggleBank(bank: string): void {
    this.filterBank.set(this.filterBank() === bank ? '' : bank);
  }

  showExcluded(): void {
    const id = this.excludedCategoryId();
    if (id !== null) this.filterCategory.set(String(id));
  }

  gShowExcluded(): void {
    const id = this.gExcludedCategoryId();
    if (id !== null) this.gFilterCategory.set(String(id));
  }

  /** 'Wed 30 Sep, BPI' */
  rowMeta(date: string, where: string): string {
    return `${TransactionsComponent.dates.transform(date, 'EEE d MMM')}, ${where}`;
  }

  balanceText(balance: number): string {
    return (balance < 0 ? '−' : '') + eur(balance);
  }

  /** Grocery amounts are prices, shown without a sign; a discount line keeps its minus. */
  itemAmountText(amount: number): string {
    return amount < 0 ? signedEur(amount) : eur(amount);
  }

  constructor() {
    effect(() => {
      void (
        this.filterBank() +
        this.filterMonth() +
        this.filterType() +
        this.filterCategory() +
        this.search() +
        this.sortCol() +
        this.sortDir()
      );
      if (!this._filtersReady) return;
      this._resetAndLoad();
    });

    // Keyboard users land on the first action when a row's menu opens.
    effect(() => this.rowMenu()?.nativeElement.querySelector('button')?.focus());

    effect(() => {
      void (
        this.gFilterStore() +
        this.gFilterMonth() +
        this.gFilterCategory() +
        this.gSearch() +
        this.gSortCol() +
        this.gSortDir()
      );
      if (!this._gFiltersReady) return;
      this._gResetAndLoad();
    });
  }

  ngOnInit(): void {
    this._paramsSub = this.route.queryParams.subscribe((params) => {
      if (params['tab'] === 'groceries') {
        this.activeTab.set('groceries');
        if (params['categoryId']) this.gFilterCategory.set(params['categoryId']);
        if (params['month']) this.gFilterMonth.set(params['month']);
      } else {
        if (params['filter'] === 'unknown') this.filterCategory.set('unknown');
        if (params['category']) this.filterCategory.set(params['category']);
        if (params['bank']) this.filterBank.set(params['bank']);
        if (params['month']) this.filterMonth.set(params['month']);
        if (params['type']) this.filterType.set(params['type']);
      }
    });
    this._filtersReady = true;
    this._resetAndLoad();
    this._gFiltersReady = true;
    this._gResetAndLoad();
  }

  ngOnDestroy(): void {
    this._currentSub?.unsubscribe();
    this._gCurrentSub?.unsubscribe();
    this._paramsSub?.unsubscribe();
  }

  private receiptsById = computed(
    () => new Map(this.groceriesSvc.receipts().map((r) => [r.id, r])),
  );

  gFindReceipt(receiptId: number) {
    return this.receiptsById().get(receiptId) ?? null;
  }

  loadMore(): void {
    const toLoad = 100;
    this._visibleTarget = this._skip + toLoad;
    this._fetchPage(this._skip, toLoad);
  }

  showLess(): void {
    this._visibleTarget = 20;
    this._skip = 20;
    this.loadedItems.update((items) => items.slice(0, 20));
  }

  private _resetAndLoad(): void {
    this._currentSub?.unsubscribe();
    this.loadedItems.set([]);
    this.totalCount.set(0);
    this.selectedIds.set(new Set());
    this._skip = 0;
    this._fetchPage(0, this._visibleTarget);
  }

  private _fetchPage(skip: number, take: number): void {
    this.pageLoading.set(true);
    const cat = this.filterCategory();

    this._currentSub = this.finance
      .getTransactions({
        bank: this.filterBank() || undefined,
        month: this.filterMonth() || undefined,
        type: this.filterType() || undefined,
        category: cat || undefined,
        search: this.search() || undefined,
        skip,
        take,
        sortBy: this.sortCol(),
        sortDir: this.sortDir(),
      })
      .subscribe({
        next: (res) => {
          this.fetchError.set('');
          this.loadedItems.update((items) => [...items, ...res.items]);
          this.totalCount.set(res.totalCount);
          this._skip = this.loadedItems().length;
          this.pageLoading.set(false);
        },
        error: () => {
          this.pageLoading.set(false);
          this.fetchError.set('Could not load transactions - the server may be unavailable.');
        },
      });
  }

  /** A pick from the category picker; during a review, picking the same category skips. */
  selectCategory(tx: EnrichedTransaction, categoryId: number | null): void {
    this.txPicker.set(null);
    if (categoryId === tx.categoryId) {
      this.reviewedRow(tx.id);
      return;
    }
    const wasAutoAssigned = !tx.categorySetManually && tx.categoryRuleId != null;
    if (wasAutoAssigned) {
      const rule = this.catSvc.rules().find((r) => r.id === tx.categoryRuleId);
      this.pendingChange.set({
        tx,
        newCategoryId: categoryId,
        ruleId: tx.categoryRuleId!,
        pattern: rule?.pattern ?? '',
      });
    } else if (categoryId !== null) {
      const cat = this.catSvc.categories().find((c) => c.id === categoryId);
      this.ruleCreatePattern.set(tx.description);
      this.ruleCreateWhole.set(true);
      this.ruleCreateValue.set(null);
      this.pendingRuleCreate.set({ tx, categoryId, categoryName: cat?.name ?? '' });
    } else {
      this.applyCategory(tx.id, null, null);
    }
  }

  confirmPending(deleteRule: boolean): void {
    const p = this.pendingChange();
    if (!p) return;
    this.pendingChange.set(null);
    this.applyCategory(p.tx.id, p.newCategoryId, deleteRule ? p.ruleId : null);
  }

  cancelRuleCreate(): void {
    this.pendingRuleCreate.set(null);
    this.reviewing.set(null);
  }

  confirmRuleCreate(createRule: boolean): void {
    const p = this.pendingRuleCreate();
    if (!p) return;
    this.pendingRuleCreate.set(null);
    if (createRule && (this.ruleCreatePattern().trim() || this.ruleCreateValue() !== null)) {
      this.ruleCreateLoading.set(true);
      this.actionError.set('');
      const ruleFailed = () => {
        this.ruleCreateLoading.set(false);
        this.reviewing.set(null);
        this.actionError.set('Could not create the rule. Please try again.');
      };
      this.catSvc
        .createRule(
          p.categoryId,
          this.ruleCreatePattern().trim(),
          this.ruleCreateValue(),
          this.ruleCreateWhole(),
        )
        .subscribe({
          next: () => {
            this.catSvc.setTransactionCategory(p.tx.id, p.categoryId).subscribe({
              next: () => {
                this.ruleCreateLoading.set(false);
                const category =
                  this.catSvc.categories().find((c) => c.id === p.categoryId) ?? null;
                this.finance.updateTransactionLocally(p.tx.id, {
                  categoryId: p.categoryId,
                  category,
                });
                this.finance.reload();
                this._resetAndLoad();
                this.reviewedRow(p.tx.id);
              },
              error: ruleFailed,
            });
          },
          error: ruleFailed,
        });
    } else {
      this.applyCategory(p.tx.id, p.categoryId, null);
    }
  }

  openCreate(tx: EnrichedTransaction): void {
    this.closePopovers();
    this.createTx.set(tx);
    this.createName.set('');
    this.createColor.set('#a855f7');
    this.createPattern.set(tx.description);
    this.createWhole.set(true);
    this.createValue.set(null);
    this.showCreateModal.set(true);
  }

  submitCreate(): void {
    const tx = this.createTx();
    if (!tx || !this.createName().trim()) return;
    this.createLoading.set(true);
    this.actionError.set('');
    this.catSvc
      .createCategory(
        this.createName().trim(),
        this.createColor(),
        this.createPattern().trim() || undefined,
        this.createValue(),
        this.createWhole(),
      )
      .subscribe({
        next: (cat) => {
          this.catSvc.setTransactionCategory(tx.id, cat.id).subscribe({
            next: () => {
              this.createLoading.set(false);
              this.showCreateModal.set(false);
              this.finance.updateTransactionLocally(tx.id, { categoryId: cat.id, category: cat });
              this.finance.reload();
              this._resetAndLoad();
            },
            error: () => {
              this.createLoading.set(false);
              this.actionError.set('Category created but could not be assigned. Please retry.');
            },
          });
        },
        error: () => {
          this.createLoading.set(false);
          this.actionError.set('Could not create the category. Please try again.');
        },
      });
  }

  includeTransaction(tx: EnrichedTransaction, e: MouseEvent): void {
    e.stopPropagation();
    this.actionError.set('');
    this.finance.markTransfers([tx.id], true).subscribe({
      next: () => {
        this.finance.updateTransactionLocally(tx.id, {
          isExcluded: false,
          categoryId: null,
          category: null,
          categorySetManually: false,
          categoryRuleId: null,
        });
        this._resetAndLoad();
      },
      error: () => this.actionError.set('Could not include the transaction. Please try again.'),
    });
  }

  excludeTransaction(tx: EnrichedTransaction, e: MouseEvent): void {
    e.stopPropagation();
    this.excludeRulePattern.set(tx.description);
    this.excludeRuleWhole.set(true);
    this.excludeRuleValue.set(null);
    this.pendingExclude.set({ tx });
  }

  cancelExclude(): void {
    this.pendingExclude.set(null);
  }

  confirmExclude(createRule: boolean): void {
    this.actionError.set('');
    const p = this.pendingExclude();
    if (!p) return;
    this.pendingExclude.set(null);
    const excludedCat = this.catSvc.categories().find((c) => c.name === CATEGORY_EXCLUDED) ?? null;

    const doExclude = () => {
      this.finance.markTransfers([p.tx.id]).subscribe({
        next: () => {
          this.excludeRuleLoading.set(false);
          this.finance.updateTransactionLocally(p.tx.id, {
            isExcluded: true,
            categoryId: excludedCat?.id ?? null,
            category: excludedCat,
            categorySetManually: false,
            categoryRuleId: null,
          });
          this.finance.reload();
          this._resetAndLoad();
        },
        error: () => {
          this.excludeRuleLoading.set(false);
          this.actionError.set('Could not exclude the transaction. Please try again.');
        },
      });
    };

    if (
      createRule &&
      excludedCat &&
      (this.excludeRulePattern().trim() || this.excludeRuleValue() !== null)
    ) {
      this.excludeRuleLoading.set(true);
      this.catSvc
        .createRule(
          excludedCat.id,
          this.excludeRulePattern().trim(),
          this.excludeRuleValue(),
          this.excludeRuleWhole(),
        )
        .subscribe({
          next: () => doExclude(),
          error: () => {
            this.excludeRuleLoading.set(false);
            this.actionError.set('Could not create the exclude rule. Please try again.');
          },
        });
    } else {
      doExclude();
    }
  }

  gExcludeItem(item: GroceryItem, e: MouseEvent): void {
    e.stopPropagation();
    this.gExcludeRulePattern.set(item.description);
    this.gExcludeRuleWhole.set(true);
    this.gExcludeRuleValue.set(null);
    this.gPendingExclude.set({ item });
  }

  gIncludeItem(item: GroceryItem, e: MouseEvent): void {
    e.stopPropagation();
    this.actionError.set('');
    this.groceriesSvc.markItemsExcluded([item.id], true).subscribe({
      next: () => {
        this.groceriesSvc.loadAllItems();
        this._gResetAndLoad();
      },
      error: () => this.actionError.set('Could not include the item. Please try again.'),
    });
  }

  gCancelExclude(): void {
    this.gPendingExclude.set(null);
  }

  gConfirmExclude(createRule: boolean): void {
    this.actionError.set('');
    const p = this.gPendingExclude();
    if (!p) return;
    this.gPendingExclude.set(null);
    const excludedCat =
      this.groceryCatSvc.categories().find((c) => c.name === CATEGORY_EXCLUDED) ?? null;

    const doExclude = () => {
      this.groceriesSvc.markItemsExcluded([p.item.id]).subscribe({
        next: () => {
          this.gExcludeRuleLoading.set(false);
          this.groceriesSvc.loadAllItems();
          this._gResetAndLoad();
        },
        error: () => {
          this.gExcludeRuleLoading.set(false);
          this.actionError.set('Could not exclude the item. Please try again.');
        },
      });
    };

    if (
      createRule &&
      excludedCat &&
      (this.gExcludeRulePattern().trim() || this.gExcludeRuleValue() !== null)
    ) {
      this.gExcludeRuleLoading.set(true);
      this.groceryCatSvc
        .createRule(
          excludedCat.id,
          this.gExcludeRulePattern().trim(),
          this.gExcludeRuleValue(),
          this.gExcludeRuleWhole(),
        )
        .subscribe({
          next: () => doExclude(),
          error: () => {
            this.gExcludeRuleLoading.set(false);
            this.actionError.set('Could not create the exclude rule. Please try again.');
          },
        });
    } else {
      doExclude();
    }
  }

  requestDeleteTransfer(tx: EnrichedTransaction, e: MouseEvent): void {
    e.stopPropagation();
    this.confirmDeleteTx.set(tx);
  }

  confirmDelete(): void {
    this.actionError.set('');
    const tx = this.confirmDeleteTx();
    if (!tx) return;
    this.confirmDeleteTx.set(null);
    this.finance.deleteTransaction(tx.id).subscribe({
      next: () => {
        this.finance.removeTransactionLocally(tx.id);
        this._resetAndLoad();
      },
      error: () => this.actionError.set('Could not delete the transaction. Please try again.'),
    });
  }

  toggleRow(id: number): void {
    this.selectedIds.update((s) => {
      const next = new Set(s);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  }

  toggleAll(): void {
    if (this.allSelected()) {
      this.selectedIds.set(new Set());
    } else {
      this.selectedIds.set(new Set(this.filtered().map((tx) => tx.id)));
    }
  }

  clearSelection(): void {
    this.selectedIds.set(new Set());
  }

  bulkExclude(): void {
    this.actionError.set('');
    const ids = [...this.selectedIds()];
    const excludedCat = this.catSvc.categories().find((c) => c.name === CATEGORY_EXCLUDED) ?? null;
    this.finance.markTransfers(ids).subscribe({
      next: () => {
        this.clearSelection();
        ids.forEach((id) =>
          this.finance.updateTransactionLocally(id, {
            isExcluded: true,
            categoryId: excludedCat?.id ?? null,
            category: excludedCat,
            categorySetManually: false,
            categoryRuleId: null,
          }),
        );
        this._resetAndLoad();
      },
      error: () => this.actionError.set('Could not exclude the selected transactions.'),
    });
  }

  bulkDelete(): void {
    this.confirmBulkDelete.set(true);
  }

  confirmBulkDeleteAction(): void {
    this.actionError.set('');
    const ids = [...this.selectedIds()];
    this.confirmBulkDelete.set(false);
    this.finance.deleteTransactions(ids).subscribe({
      next: () => {
        this.clearSelection();
        ids.forEach((id) => this.finance.removeTransactionLocally(id));
        this._resetAndLoad();
      },
      error: () => this.actionError.set('Could not delete the selected transactions.'),
    });
  }

  openTxEdit(tx: EnrichedTransaction, e: MouseEvent): void {
    e.stopPropagation();
    this._editingTxRef.set(tx);
    this.editingTxId.set(tx.id);
    this.txDatePosting.set(tx.datePosting);
    this.txDateValue.set(tx.dateValue);
    this.txDescription.set(tx.description);
    this.txAmount.set(tx.amount);
    this.txType.set(tx.type as 'credit' | 'debit');
    this.txBalance.set(tx.balance);
    this.txCategoryId.set(tx.categoryId);
    this.modalError.set('');
    this.showTxModal.set(true);
  }

  submitTxModal(): void {
    if (!this.txFormValid()) return;
    this.txLoading.set(true);
    this.finance
      .updateTransaction(this.editingTxId()!, {
        datePosting: this.txDatePosting() || null,
        dateValue: this.txDateValue() || null,
        description: this.txDescription().trim() || null,
        amount: this.txAmount(),
        type: this.txType(),
        balance: this.txBalance(),
        categoryId: this.txCategoryId(),
        categorySetManually: true,
      })
      .subscribe({
        next: (updated) => this._afterTxSave(updated),
        error: () => {
          this.txLoading.set(false);
          this.modalError.set('Could not save the transaction. Please try again.');
        },
      });
  }

  private _afterTxSave(updated: Transaction): void {
    this.txLoading.set(false);
    this.showTxModal.set(false);
    const category = updated.categoryId
      ? (this.catSvc.categories().find((c) => c.id === updated.categoryId) ?? null)
      : null;
    this.finance.updateTransactionLocally(updated.id, { ...updated, category });
    this._resetAndLoad();
  }

  private applyCategory(
    txId: number,
    categoryId: number | null,
    deleteRuleId: number | null,
  ): void {
    this.actionError.set('');
    this.catSvc.setTransactionCategory(txId, categoryId, deleteRuleId ?? undefined).subscribe({
      next: () => {
        const category = categoryId
          ? (this.catSvc.categories().find((c) => c.id === categoryId) ?? null)
          : null;
        this.finance.updateTransactionLocally(txId, { categoryId, category });
        this._resetAndLoad();
        this.reviewedRow(txId);
      },
      error: () => {
        this.reviewing.set(null);
        this.actionError.set('Could not change the category. Please try again.');
      },
    });
  }

  /** A pick from the category picker; during a review, picking the same category skips. */
  gSelectCategory(item: GroceryItem, categoryId: number | null): void {
    this.itemPicker.set(null);
    if (categoryId === item.categoryId) {
      this.reviewedRow(item.id);
      return;
    }
    const wasAutoAssigned = !item.categorySetManually && item.categoryId !== null;
    const ruleForItem = wasAutoAssigned
      ? this.groceryCatSvc
          .rules()
          .find(
            (r) =>
              r.categoryId === item.categoryId &&
              matchesRule(item, r.pattern, r.value, r.matchWholeDescription),
          )
      : null;

    if (wasAutoAssigned && ruleForItem) {
      this.gPendingChange.set({
        item,
        newCategoryId: categoryId,
        ruleId: ruleForItem.id,
        pattern: ruleForItem.pattern ?? '',
      });
    } else if (categoryId !== null) {
      const cat = this.groceryCatSvc.categories().find((c) => c.id === categoryId);
      this.gRuleCreatePattern.set(item.description);
      this.gRuleCreateWhole.set(true);
      this.gRuleCreateValue.set(null);
      this.gPendingRuleCreate.set({ item, categoryId, categoryName: cat?.name ?? '' });
    } else {
      this._gApplyCategory(item.id, null, null);
    }
  }

  gConfirmPending(deleteRule: boolean): void {
    const p = this.gPendingChange();
    if (!p) return;
    this.gPendingChange.set(null);
    this._gApplyCategory(p.item.id, p.newCategoryId, deleteRule ? p.ruleId : null);
  }

  gCancelRuleCreate(): void {
    this.gPendingRuleCreate.set(null);
    this.reviewing.set(null);
  }

  gConfirmRuleCreate(createRule: boolean): void {
    const p = this.gPendingRuleCreate();
    if (!p) return;
    this.gPendingRuleCreate.set(null);
    if (createRule && (this.gRuleCreatePattern().trim() || this.gRuleCreateValue() !== null)) {
      this.gRuleCreateLoading.set(true);
      this.actionError.set('');
      const gRuleFailed = () => {
        this.gRuleCreateLoading.set(false);
        this.reviewing.set(null);
        this.actionError.set('Could not create the rule. Please try again.');
      };
      this.groceryCatSvc
        .createRule(
          p.categoryId,
          this.gRuleCreatePattern().trim(),
          this.gRuleCreateValue(),
          this.gRuleCreateWhole(),
        )
        .subscribe({
          next: () => {
            this.groceryCatSvc.setItemCategory(p.item.id, p.categoryId).subscribe({
              next: () => {
                this.gRuleCreateLoading.set(false);
                this.groceriesSvc.loadAllItems();
                this._gResetAndLoad();
                this.reviewedRow(p.item.id);
              },
              error: gRuleFailed,
            });
          },
          error: gRuleFailed,
        });
    } else {
      this._gApplyCategory(p.item.id, p.categoryId, null);
    }
  }

  gOpenCreate(item: GroceryItem): void {
    this.closePopovers();
    this.gCreateItem.set(item);
    this.gCreateName.set('');
    this.gCreateColor.set('#a855f7');
    this.gCreatePattern.set(item.description);
    this.gCreateWhole.set(true);
    this.gCreateValue.set(null);
    this.gShowCreateModal.set(true);
  }

  gSubmitCreate(): void {
    const item = this.gCreateItem();
    if (!item || !this.gCreateName().trim()) return;
    this.gCreateLoading.set(true);
    this.actionError.set('');
    this.groceryCatSvc
      .createCategory(
        this.gCreateName().trim(),
        this.gCreateColor(),
        this.gCreatePattern().trim() || undefined,
        this.gCreateValue(),
        this.gCreateWhole(),
      )
      .subscribe({
        next: (cat) => {
          this.groceryCatSvc.setItemCategory(item.id, cat.id).subscribe({
            next: () => {
              this.gCreateLoading.set(false);
              this.gShowCreateModal.set(false);
              this.groceriesSvc.loadAllItems();
              this._gResetAndLoad();
            },
            error: () => {
              this.gCreateLoading.set(false);
              this.actionError.set('Category created but could not be assigned. Please retry.');
            },
          });
        },
        error: () => {
          this.gCreateLoading.set(false);
          this.actionError.set('Could not create the category. Please try again.');
        },
      });
  }

  gRequestDeleteItem(item: GroceryItem, e: MouseEvent): void {
    e.stopPropagation();
    this.gConfirmDeleteItem.set(item);
  }

  gConfirmDelete(): void {
    this.actionError.set('');
    const item = this.gConfirmDeleteItem();
    if (!item) return;
    this.gConfirmDeleteItem.set(null);
    this.groceriesSvc.deleteItem(item.id).subscribe({
      next: () => {
        this.groceriesSvc.reload();
        this.groceriesSvc.loadAllItems();
        this._gResetAndLoad();
      },
      error: () => this.actionError.set('Could not delete the item. Please try again.'),
    });
  }

  gOpenItemEdit(item: GroceryItem, e: MouseEvent): void {
    e.stopPropagation();
    this.gEditingItemId.set(item.id);
    this.gItemDescription.set(item.description);
    this.gItemAmount.set(item.amount);
    this.gItemQuantity.set(item.quantity);
    this.modalError.set('');
    this.gShowItemModal.set(true);
  }

  gSubmitItemModal(): void {
    if (!this.gItemFormValid()) return;
    this.gItemLoading.set(true);
    this.groceriesSvc
      .updateItem(this.gEditingItemId()!, {
        description: this.gItemDescription().trim(),
        amount: this.gItemAmount()!,
        quantity: this.gItemQuantity(),
      })
      .subscribe({
        next: () => {
          this.gItemLoading.set(false);
          this.gShowItemModal.set(false);
          this.groceriesSvc.loadAllItems();
          this._gResetAndLoad();
        },
        error: () => {
          this.gItemLoading.set(false);
          this.modalError.set('Could not save the item. Please try again.');
        },
      });
  }

  gOpenCreateItem(): void {
    this.gNewItemReceiptId.set(null);
    this.gNewItemDescription.set('');
    this.gNewItemAmount.set(null);
    this.gNewItemQuantity.set(1);
    this.modalError.set('');
    this.gShowCreateItemModal.set(true);
  }

  gSubmitNewItem(): void {
    if (!this.gNewItemFormValid()) return;
    this.gNewItemLoading.set(true);
    this.groceriesSvc
      .createItem({
        receiptId: this.gNewItemReceiptId()!,
        description: this.gNewItemDescription().trim(),
        amount: this.gNewItemAmount()!,
        quantity: this.gNewItemQuantity(),
      })
      .subscribe({
        next: () => {
          this.gNewItemLoading.set(false);
          this.gShowCreateItemModal.set(false);
          this.groceriesSvc.reload();
          this.groceriesSvc.loadAllItems();
          this._gResetAndLoad();
        },
        error: () => {
          this.gNewItemLoading.set(false);
          this.modalError.set('Could not create the item. Please try again.');
        },
      });
  }

  gRequestDeleteReceipt(receipt: GroceryReceiptSummary, e: MouseEvent): void {
    e.stopPropagation();
    this.gConfirmDeleteReceipt.set(receipt);
  }

  gConfirmReceiptDelete(): void {
    this.actionError.set('');
    const receipt = this.gConfirmDeleteReceipt();
    if (!receipt) return;
    this.gConfirmDeleteReceipt.set(null);
    this.groceriesSvc.deleteReceipt(receipt.id).subscribe({
      next: () => {
        this.groceriesSvc.reload();
        this.groceriesSvc.loadAllItems();
        this._gResetAndLoad();
      },
      error: () => this.actionError.set('Could not delete the receipt. Please try again.'),
    });
  }

  private _gApplyCategory(
    itemId: number,
    categoryId: number | null,
    deleteRuleId: number | null,
  ): void {
    this.actionError.set('');
    this.groceryCatSvc.setItemCategory(itemId, categoryId, deleteRuleId ?? undefined).subscribe({
      next: () => {
        this.groceriesSvc.loadAllItems();
        this._gResetAndLoad();
        this.reviewedRow(itemId);
      },
      error: () => {
        this.reviewing.set(null);
        this.actionError.set('Could not change the category. Please try again.');
      },
    });
  }

  gLoadMore(): void {
    const toLoad = 100;
    this._gVisibleTarget = this._gSkip + toLoad;
    this._gFetchPage(this._gSkip, toLoad);
  }

  gShowLess(): void {
    this._gVisibleTarget = 20;
    this._gSkip = 20;
    this.gLoadedItems.update((items) => items.slice(0, 20));
  }

  private _gResetAndLoad(): void {
    this._gCurrentSub?.unsubscribe();
    this.gLoadedItems.set([]);
    this.gTotalCount.set(0);
    this._gSkip = 0;
    this._gFetchPage(0, this._gVisibleTarget);
  }

  private _gFetchPage(skip: number, take: number): void {
    this.gPageLoading.set(true);
    const params: Record<string, string | number> = { skip, take };
    if (this.gFilterStore()) params['store'] = this.gFilterStore();
    if (this.gFilterMonth()) params['month'] = this.gFilterMonth();
    const cat = this.gFilterCategory();
    if (cat && cat !== 'unknown') params['categoryId'] = cat;
    if (this.gSearch()) params['search'] = this.gSearch();
    params['sortCol'] = this.gSortCol();
    params['sortDir'] = this.gSortDir();

    this._gCurrentSub = this.groceriesSvc.getItems(params).subscribe({
      next: (res) => {
        this.gFetchError.set('');
        this.gLoadedItems.update((items) => [...items, ...res.items]);
        this.gTotalCount.set(res.totalCount);
        this._gSkip = this.gLoadedItems().length;
        this.gPageLoading.set(false);
      },
      error: () => {
        this.gPageLoading.set(false);
        this.gFetchError.set('Could not load grocery items - the server may be unavailable.');
      },
    });
  }
}
