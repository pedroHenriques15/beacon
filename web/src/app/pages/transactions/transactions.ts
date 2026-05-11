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
import { CategoriesService } from '../../core/services/categories.service';
import { matchesRule } from '../../core/utils/rule-match';
import { availableMonths } from '../../core/utils/date-utils';
import { CATEGORY_INTERNAL_TRANSFER } from '../../core/constants/categories';

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

  filterBank = signal('');
  filterMonth = signal('');
  filterType = signal('');
  filterCategory = signal('');
  search = signal('');
  showTransfers = signal(false);

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
      .filter((c) => c.name !== CATEGORY_INTERNAL_TRANSFER)
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

  transferCount = computed(() => {
    const bank = this.filterBank().toUpperCase();
    const month = this.filterMonth();
    return this.finance
      .allTransactionsRaw()
      .filter((t) => t.isInternalTransfer)
      .filter((t) => !bank || t.bank === bank)
      .filter((t) => !month || t.month === month).length;
  });

  availableMonths = computed(() => availableMonths(this.finance.allTransactionsRaw()));

  @HostListener('document:click')
  onDocumentClick(): void {
    this.openDropdownId.set(null);
    this.dropdownPos.set(null);
    this.catSearch.set('');
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
          String(this.showTransfers()) +
          this.sortCol() +
          this.sortDir()
        );
        if (!this._filtersReady) return;
        this._resetAndLoad();
      },
      { allowSignalWrites: true },
    );
  }

  ngOnInit(): void {
    this.route.queryParams.subscribe((params) => {
      if (params['filter'] === 'unknown') this.filterCategory.set('unknown');
      if (params['category']) this.filterCategory.set(params['category']);
      if (params['bank']) this.filterBank.set(params['bank']);
      if (params['month']) this.filterMonth.set(params['month']);
      if (params['type']) this.filterType.set(params['type']);
    });
    this._filtersReady = true;
    this._resetAndLoad();
  }

  ngOnDestroy(): void {
    this._currentSub?.unsubscribe();
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
    this._skip = 0;
    this._fetchPage(0, this._visibleTarget);
  }

  private _fetchPage(skip: number, take: number): void {
    this.pageLoading.set(true);
    const cat = this.filterCategory();
    const internalTransferCat = this.catSvc
      .categories()
      .find((c) => c.name === CATEGORY_INTERNAL_TRANSFER);
    const isFilteringByTransfer = !!internalTransferCat && cat === String(internalTransferCat.id);

    this._currentSub = this.finance
      .getTransactions({
        bank: this.filterBank() || undefined,
        month: this.filterMonth() || undefined,
        type: this.filterType() || undefined,
        category: cat || undefined,
        search: this.search() || undefined,
        skip,
        take,
        includeTransfers: this.showTransfers() || isFilteringByTransfer || undefined,
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
            this.finance.reload();
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
          this.finance.reload();
          this._resetAndLoad();
        });
      });
  }

  unmarkTransfer(tx: EnrichedTransaction, e: MouseEvent): void {
    e.stopPropagation();
    this.finance.markTransfers([tx.id], true).subscribe(() => {
      this.finance.reload();
      this._resetAndLoad();
    });
  }

  markTransfer(tx: EnrichedTransaction, e: MouseEvent): void {
    e.stopPropagation();
    this.finance.markTransfers([tx.id]).subscribe(() => {
      this.finance.reload();
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
      this.finance.reload();
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
      .subscribe({ next: () => this._afterTxSave(), error: () => this.txLoading.set(false) });
  }

  private _afterTxSave(): void {
    this.txLoading.set(false);
    this.showTxModal.set(false);
    this.finance.reload();
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
        this.finance.reload();
        this._resetAndLoad();
      });
  }
}
