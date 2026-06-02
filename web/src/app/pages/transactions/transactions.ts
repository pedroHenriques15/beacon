import {
  Component,
  inject,
  signal,
  computed,
  HostListener,
  OnInit,
  OnDestroy,
  effect,
} from '@angular/core';
import { CurrencyPipe, DatePipe, NgClass } from '@angular/common';
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

@Component({
  selector: 'app-transactions',
  standalone: true,
  imports: [CurrencyPipe, DatePipe, NgClass, FormsModule],
  templateUrl: './transactions.html',
  styleUrl: './transactions.scss',
})
export class TransactionsComponent implements OnInit, OnDestroy {
  private route = inject(ActivatedRoute);
  finance = inject(FinanceService);
  catSvc = inject(CategoriesService);
  groceriesSvc = inject(GroceriesService);
  groceryCatSvc = inject(GroceryCategoriesService);

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
  totalCreditAll = signal(0);
  totalDebitAll = signal(0);
  pageLoading = signal(false);
  private _skip = 0;
  private _visibleTarget = 20;
  private _filtersReady = false;
  private _currentSub?: Subscription;

  openDropdownId = signal<number | null>(null);
  dropdownPos = signal<{ top: number; left: number } | null>(null);
  catSearch = signal('');
  filteredCats = computed(() => {
    const q = this.catSearch().toLowerCase();
    return this.catSvc
      .categories()
      .filter((c) => c.name !== CATEGORY_EXCLUDED)
      .filter((c) => !q || c.name.toLowerCase().includes(q));
  });
  pendingChange = signal<PendingChange | null>(null);

  pendingRuleCreate = signal<PendingRuleCreate | null>(null);
  ruleCreatePattern = signal('');
  ruleCreateValue = signal<number | null>(null);
  ruleCreateLoading = signal(false);

  showCreateModal = signal(false);
  createTx = signal<EnrichedTransaction | null>(null);
  createName = signal('');
  createColor = signal('#a855f7');
  createPattern = signal('');
  createValue = signal<number | null>(null);
  createLoading = signal(false);

  ruleCreateMatchCount = computed(() => {
    const pat = this.ruleCreatePattern().trim();
    const val = this.ruleCreateValue();
    if (!pat && val === null) return null;
    return this.finance.allTransactions().filter((tx) => matchesRule(tx, pat, val)).length;
  });

  createMatchCount = computed(() => {
    const pat = this.createPattern().trim();
    const val = this.createValue();
    if (!pat && val === null) return null;
    return this.finance.allTransactions().filter((tx) => matchesRule(tx, pat, val)).length;
  });

  confirmDeleteTx = signal<EnrichedTransaction | null>(null);

  selectedIds = signal<Set<number>>(new Set());
  hasSelection = computed(() => this.selectedIds().size > 0);
  allSelected = computed(
    () =>
      this.filtered().length > 0 &&
      this.filtered().every((tx) => this.selectedIds().has(tx.id)),
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

  totalCredit = this.totalCreditAll;
  totalDebit = this.totalDebitAll;

  hasMore = computed(() => this.loadedItems().length < this.totalCount());

  unknownCount = computed(
    () => this.finance.allTransactions().filter((t) => t.categoryId === null).length,
  );

  availableMonths = computed(() => availableMonths(this.finance.allTransactionsRaw()));

  gFilterStore = signal('');
  gFilterMonth = signal('');
  gFilterCategory = signal('');
  gSearch = signal('');

  gSortCol = signal<GrocerySortCol>('date');
  gSortDir = signal<'asc' | 'desc'>('desc');

  gLoadedItems = signal<GroceryItem[]>([]);
  gTotalCount = signal(0);
  gTotalAmount = signal(0);
  gPageLoading = signal(false);
  private _gSkip = 0;
  private _gVisibleTarget = 20;
  private _gFiltersReady = false;
  private _gCurrentSub?: Subscription;
  private _paramsSub?: Subscription;

  gOpenDropdownId = signal<number | null>(null);
  gDropdownPos = signal<{ top: number; left: number } | null>(null);
  gCatSearch = signal('');
  gFilteredCats = computed(() => {
    const q = this.gCatSearch().toLowerCase();
    return this.groceryCatSvc.categories().filter((c) => !q || c.name.toLowerCase().includes(q));
  });

  gPendingChange = signal<GPendingChange | null>(null);

  gPendingRuleCreate = signal<GPendingRuleCreate | null>(null);
  gRuleCreatePattern = signal('');
  gRuleCreateValue = signal<number | null>(null);
  gRuleCreateLoading = signal(false);

  gShowCreateModal = signal(false);
  gCreateItem = signal<GroceryItem | null>(null);
  gCreateName = signal('');
  gCreateColor = signal('#a855f7');
  gCreatePattern = signal('');
  gCreateValue = signal<number | null>(null);
  gCreateLoading = signal(false);

  gRuleCreateMatchCount = computed(() => {
    const pat = this.gRuleCreatePattern().trim();
    const val = this.gRuleCreateValue();
    if (!pat && val === null) return null;
    return this.groceriesSvc.allItems().filter((item) => matchesRule(item, pat, val)).length;
  });

  gCreateMatchCount = computed(() => {
    const pat = this.gCreatePattern().trim();
    const val = this.gCreateValue();
    if (!pat && val === null) return null;
    return this.groceriesSvc.allItems().filter((item) => matchesRule(item, pat, val)).length;
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

  gUnknownCount = computed(
    () => this.groceriesSvc.allItems().filter((item) => item.categoryId === null).length,
  );

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

  @HostListener('document:click')
  onDocumentClick(): void {
    this.openDropdownId.set(null);
    this.dropdownPos.set(null);
    this.catSearch.set('');
    this.gOpenDropdownId.set(null);
    this.gDropdownPos.set(null);
    this.gCatSearch.set('');
  }

  constructor() {
    effect(
      () => {
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
      },
      { allowSignalWrites: true },
    );

    effect(
      () => {
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
      },
      { allowSignalWrites: true },
    );
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

  gFindReceipt(receiptId: number): GroceryReceiptSummary | null {
    return this.groceriesSvc.receipts().find((r) => r.id === receiptId) ?? null;
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
    this.totalCreditAll.set(0);
    this.totalDebitAll.set(0);
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
          this.loadedItems.update((items) => [...items, ...res.items]);
          this.totalCount.set(res.totalCount);
          if (skip === 0) {
            this.totalCreditAll.set(res.totalCredit);
            this.totalDebitAll.set(res.totalDebit);
          }
          this._skip = this.loadedItems().length;
          this.pageLoading.set(false);
        },
        error: () => this.pageLoading.set(false),
      });
  }

  formatMonth(m: string): string {
    const [y, mo] = m.split('-');
    return new Date(+y, +mo - 1, 1).toLocaleString('default', { month: 'long', year: 'numeric' });
  }

  sort(col: SortCol): void {
    if (this.sortCol() === col) {
      this.sortDir.update((d) => (d === 'asc' ? 'desc' : 'asc'));
    } else {
      this.sortCol.set(col);
      this.sortDir.set('asc');
    }
  }

  sortIcon(col: SortCol): string {
    if (this.sortCol() !== col) return '';
    return this.sortDir() === 'asc' ? ' ↑' : ' ↓';
  }

  toggleDropdown(txId: number, e: MouseEvent): void {
    e.stopPropagation();
    const willClose = this.openDropdownId() === txId;
    if (willClose) {
      this.openDropdownId.set(null);
      this.dropdownPos.set(null);
      this.catSearch.set('');
    } else {
      const btn = e.currentTarget as HTMLElement;
      const rect = btn.getBoundingClientRect();
      const estimatedHeight = 360;
      const top =
        window.innerHeight - rect.bottom >= estimatedHeight
          ? rect.bottom + 4
          : rect.top - estimatedHeight - 4;
      this.dropdownPos.set({ top, left: rect.left });
      this.openDropdownId.set(txId);
    }
  }

  selectCategory(tx: EnrichedTransaction, categoryId: number | null, e: MouseEvent): void {
    e.stopPropagation();
    this.openDropdownId.set(null);
    if (categoryId === tx.categoryId) return;
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
  }

  confirmRuleCreate(createRule: boolean): void {
    const p = this.pendingRuleCreate();
    if (!p) return;
    this.pendingRuleCreate.set(null);
    if (createRule && (this.ruleCreatePattern().trim() || this.ruleCreateValue() !== null)) {
      this.ruleCreateLoading.set(true);
      this.catSvc
        .createRule(p.categoryId, this.ruleCreatePattern().trim(), this.ruleCreateValue())
        .subscribe(() => {
          this.catSvc.setTransactionCategory(p.tx.id, p.categoryId).subscribe(() => {
            this.ruleCreateLoading.set(false);
            const category = this.catSvc.categories().find((c) => c.id === p.categoryId) ?? null;
            this.finance.updateTransactionLocally(p.tx.id, { categoryId: p.categoryId, category });
            this._resetAndLoad();
          });
        });
    } else {
      this.applyCategory(p.tx.id, p.categoryId, null);
    }
  }

  openCreate(tx: EnrichedTransaction, e: MouseEvent): void {
    e.stopPropagation();
    this.openDropdownId.set(null);
    this.createTx.set(tx);
    this.createName.set('');
    this.createColor.set('#a855f7');
    this.createPattern.set(tx.description);
    this.createValue.set(null);
    this.showCreateModal.set(true);
  }

  submitCreate(): void {
    const tx = this.createTx();
    if (!tx || !this.createName().trim()) return;
    this.createLoading.set(true);
    this.catSvc
      .createCategory(
        this.createName().trim(),
        this.createColor(),
        this.createPattern().trim() || undefined,
        this.createValue(),
      )
      .subscribe((cat) => {
        this.catSvc.setTransactionCategory(tx.id, cat.id).subscribe(() => {
          this.createLoading.set(false);
          this.showCreateModal.set(false);
          this.finance.updateTransactionLocally(tx.id, { categoryId: cat.id, category: cat });
          this._resetAndLoad();
        });
      });
  }

  includeTransaction(tx: EnrichedTransaction, e: MouseEvent): void {
    e.stopPropagation();
    this.finance.markTransfers([tx.id], true).subscribe(() => {
      this.finance.updateTransactionLocally(tx.id, {
        isExcluded: false,
        categoryId: null,
        category: null,
        categorySetManually: false,
        categoryRuleId: null,
      });
      this._resetAndLoad();
    });
  }

  excludeTransaction(tx: EnrichedTransaction, e: MouseEvent): void {
    e.stopPropagation();
    const excludedCat = this.catSvc.categories().find((c) => c.name === CATEGORY_EXCLUDED) ?? null;
    this.finance.markTransfers([tx.id]).subscribe(() => {
      this.finance.updateTransactionLocally(tx.id, {
        isExcluded: true,
        categoryId: excludedCat?.id ?? null,
        category: excludedCat,
        categorySetManually: false,
        categoryRuleId: null,
      });
      this._resetAndLoad();
    });
  }

  requestDeleteTransfer(tx: EnrichedTransaction, e: MouseEvent): void {
    e.stopPropagation();
    this.confirmDeleteTx.set(tx);
  }

  confirmDelete(): void {
    const tx = this.confirmDeleteTx();
    if (!tx) return;
    this.confirmDeleteTx.set(null);
    this.finance.deleteTransaction(tx.id).subscribe(() => {
      this.finance.removeTransactionLocally(tx.id);
      this._resetAndLoad();
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
    const ids = [...this.selectedIds()];
    const excludedCat = this.catSvc.categories().find((c) => c.name === CATEGORY_EXCLUDED) ?? null;
    this.finance.markTransfers(ids).subscribe(() => {
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
    });
  }

  bulkDelete(): void {
    this.confirmBulkDelete.set(true);
  }

  confirmBulkDeleteAction(): void {
    const ids = [...this.selectedIds()];
    this.confirmBulkDelete.set(false);
    this.finance.deleteTransactions(ids).subscribe(() => {
      this.clearSelection();
      ids.forEach((id) => this.finance.removeTransactionLocally(id));
      this._resetAndLoad();
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
      .subscribe({ next: (updated) => this._afterTxSave(updated), error: () => this.txLoading.set(false) });
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
    this.catSvc
      .setTransactionCategory(txId, categoryId, deleteRuleId ?? undefined)
      .subscribe(() => {
        const category = categoryId
          ? (this.catSvc.categories().find((c) => c.id === categoryId) ?? null)
          : null;
        this.finance.updateTransactionLocally(txId, { categoryId, category });
        this._resetAndLoad();
      });
  }

  gSort(col: GrocerySortCol): void {
    if (this.gSortCol() === col) {
      this.gSortDir.update((d) => (d === 'asc' ? 'desc' : 'asc'));
    } else {
      this.gSortCol.set(col);
      this.gSortDir.set('asc');
    }
  }

  gSortIcon(col: GrocerySortCol): string {
    if (this.gSortCol() !== col) return '';
    return this.gSortDir() === 'asc' ? ' ↑' : ' ↓';
  }

  gToggleDropdown(itemId: number, e: MouseEvent): void {
    e.stopPropagation();
    const willClose = this.gOpenDropdownId() === itemId;
    if (willClose) {
      this.gOpenDropdownId.set(null);
      this.gDropdownPos.set(null);
      this.gCatSearch.set('');
    } else {
      const btn = e.currentTarget as HTMLElement;
      const rect = btn.getBoundingClientRect();
      const estimatedHeight = 360;
      const top =
        window.innerHeight - rect.bottom >= estimatedHeight
          ? rect.bottom + 4
          : rect.top - estimatedHeight - 4;
      this.gDropdownPos.set({ top, left: rect.left });
      this.gOpenDropdownId.set(itemId);
    }
  }

  gSelectCategory(item: GroceryItem, categoryId: number | null, e: MouseEvent): void {
    e.stopPropagation();
    this.gOpenDropdownId.set(null);
    if (categoryId === item.categoryId) return;
    const wasAutoAssigned = !item.categorySetManually && item.categoryId !== null;
    const ruleForItem = wasAutoAssigned
      ? this.groceryCatSvc
          .rules()
          .find((r) => r.categoryId === item.categoryId && matchesRule(item, r.pattern, r.value))
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
  }

  gConfirmRuleCreate(createRule: boolean): void {
    const p = this.gPendingRuleCreate();
    if (!p) return;
    this.gPendingRuleCreate.set(null);
    if (createRule && (this.gRuleCreatePattern().trim() || this.gRuleCreateValue() !== null)) {
      this.gRuleCreateLoading.set(true);
      this.groceryCatSvc
        .createRule(p.categoryId, this.gRuleCreatePattern().trim(), this.gRuleCreateValue())
        .subscribe(() => {
          this.groceryCatSvc.setItemCategory(p.item.id, p.categoryId).subscribe(() => {
            this.gRuleCreateLoading.set(false);
            this.groceriesSvc.loadAllItems();
            this._gResetAndLoad();
          });
        });
    } else {
      this._gApplyCategory(p.item.id, p.categoryId, null);
    }
  }

  gOpenCreate(item: GroceryItem, e: MouseEvent): void {
    e.stopPropagation();
    this.gOpenDropdownId.set(null);
    this.gCreateItem.set(item);
    this.gCreateName.set('');
    this.gCreateColor.set('#a855f7');
    this.gCreatePattern.set(item.description);
    this.gCreateValue.set(null);
    this.gShowCreateModal.set(true);
  }

  gSubmitCreate(): void {
    const item = this.gCreateItem();
    if (!item || !this.gCreateName().trim()) return;
    this.gCreateLoading.set(true);
    this.groceryCatSvc
      .createCategory(
        this.gCreateName().trim(),
        this.gCreateColor(),
        this.gCreatePattern().trim() || undefined,
        this.gCreateValue(),
      )
      .subscribe((cat) => {
        this.groceryCatSvc.setItemCategory(item.id, cat.id).subscribe(() => {
          this.gCreateLoading.set(false);
          this.gShowCreateModal.set(false);
          this.groceriesSvc.loadAllItems();
          this._gResetAndLoad();
        });
      });
  }

  gRequestDeleteItem(item: GroceryItem, e: MouseEvent): void {
    e.stopPropagation();
    this.gConfirmDeleteItem.set(item);
  }

  gConfirmDelete(): void {
    const item = this.gConfirmDeleteItem();
    if (!item) return;
    this.gConfirmDeleteItem.set(null);
    this.groceriesSvc.deleteItem(item.id).subscribe(() => {
      this.groceriesSvc.reload();
      this.groceriesSvc.loadAllItems();
      this._gResetAndLoad();
    });
  }

  gOpenItemEdit(item: GroceryItem, e: MouseEvent): void {
    e.stopPropagation();
    this.gEditingItemId.set(item.id);
    this.gItemDescription.set(item.description);
    this.gItemAmount.set(item.amount);
    this.gItemQuantity.set(item.quantity);
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
        error: () => this.gItemLoading.set(false),
      });
  }

  gOpenCreateItem(): void {
    this.gNewItemReceiptId.set(null);
    this.gNewItemDescription.set('');
    this.gNewItemAmount.set(null);
    this.gNewItemQuantity.set(1);
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
        error: () => this.gNewItemLoading.set(false),
      });
  }

  gRequestDeleteReceipt(receipt: GroceryReceiptSummary, e: MouseEvent): void {
    e.stopPropagation();
    this.gConfirmDeleteReceipt.set(receipt);
  }

  gConfirmReceiptDelete(): void {
    const receipt = this.gConfirmDeleteReceipt();
    if (!receipt) return;
    this.gConfirmDeleteReceipt.set(null);
    this.groceriesSvc.deleteReceipt(receipt.id).subscribe(() => {
      this.groceriesSvc.reload();
      this.groceriesSvc.loadAllItems();
      this._gResetAndLoad();
    });
  }

  private _gApplyCategory(
    itemId: number,
    categoryId: number | null,
    deleteRuleId: number | null,
  ): void {
    this.groceryCatSvc
      .setItemCategory(itemId, categoryId, deleteRuleId ?? undefined)
      .subscribe(() => {
        this.groceriesSvc.loadAllItems();
        this._gResetAndLoad();
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
    this.gTotalAmount.set(0);
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
        this.gLoadedItems.update((items) => [...items, ...res.items]);
        this.gTotalCount.set(res.totalCount);
        if (skip === 0) {
          this.gTotalAmount.set(res.totalAmount);
        }
        this._gSkip = this.gLoadedItems().length;
        this.gPageLoading.set(false);
      },
      error: () => this.gPageLoading.set(false),
    });
  }
}
