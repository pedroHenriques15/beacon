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
import { CurrencyPipe, DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { Subscription } from 'rxjs';
import { GroceriesService } from '../../core/services/groceries.service';
import { GroceryCategoriesService } from '../../core/services/grocery-categories.service';
import { GroceryItem, GroceryReceiptSummary } from '../../core/models/grocery.model';
import { matchesRule } from '../../core/utils/rule-match';

interface PendingChange {
  item: GroceryItem;
  newCategoryId: number | null;
  ruleId: number;
  pattern: string;
}

interface PendingRuleCreate {
  item: GroceryItem;
  categoryId: number;
  categoryName: string;
}

type SortCol = 'date' | 'store' | 'description' | 'category' | 'amount' | 'quantity';

@Component({
  selector: 'app-groceries',
  standalone: true,
  imports: [CurrencyPipe, DatePipe, FormsModule],
  templateUrl: './groceries.html',
  styleUrl: './groceries.scss',
})
export class GroceriesComponent implements OnInit, OnDestroy {
  groceriesSvc = inject(GroceriesService);
  groceryCatSvc = inject(GroceryCategoriesService);
  private route = inject(ActivatedRoute);

  filterStore = signal('');
  filterMonth = signal('');
  filterCategory = signal('');
  search = signal('');

  sortCol = signal<SortCol>('date');
  sortDir = signal<'asc' | 'desc'>('desc');

  loadedItems = signal<GroceryItem[]>([]);
  totalCount = signal(0);
  totalAmount = signal(0);
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
    return this.groceryCatSvc.categories().filter((c) => !q || c.name.toLowerCase().includes(q));
  });
  pendingChange = signal<PendingChange | null>(null);

  pendingRuleCreate = signal<PendingRuleCreate | null>(null);
  ruleCreatePattern = signal('');
  ruleCreateValue = signal<number | null>(null);
  ruleCreateLoading = signal(false);

  showCreateModal = signal(false);
  createItem = signal<GroceryItem | null>(null);
  createName = signal('');
  createColor = signal('#a855f7');
  createPattern = signal('');
  createValue = signal<number | null>(null);
  createLoading = signal(false);

  ruleCreateMatchCount = computed(() => {
    const pat = this.ruleCreatePattern().trim();
    const val = this.ruleCreateValue();
    if (!pat && val === null) return null;
    return this.groceriesSvc.allItems().filter((item) => matchesRule(item, pat, val)).length;
  });

  createMatchCount = computed(() => {
    const pat = this.createPattern().trim();
    const val = this.createValue();
    if (!pat && val === null) return null;
    return this.groceriesSvc.allItems().filter((item) => matchesRule(item, pat, val)).length;
  });

  confirmDeleteItem = signal<GroceryItem | null>(null);

  confirmDeleteReceipt = signal<GroceryReceiptSummary | null>(null);

  showItemModal = signal(false);
  editingItemId = signal<number | null>(null);
  itemDescription = signal('');
  itemAmount = signal<number | null>(null);
  itemQuantity = signal<number>(1);
  itemLoading = signal(false);

  showCreateItemModal = signal(false);
  newItemReceiptId = signal<number | null>(null);
  newItemDescription = signal('');
  newItemAmount = signal<number | null>(null);
  newItemQuantity = signal<number>(1);
  newItemLoading = signal(false);

  itemFormValid = computed(() => {
    const amount = this.itemAmount();
    return !!(this.itemDescription().trim() && amount !== null && !isNaN(amount) && amount >= 0);
  });

  newItemFormValid = computed(() => {
    const amount = this.newItemAmount();
    return !!(
      this.newItemReceiptId() !== null &&
      this.newItemDescription().trim() &&
      amount !== null &&
      !isNaN(amount) &&
      amount >= 0
    );
  });

  availableMonths = computed(() => {
    const months = this.groceriesSvc.allItems().map((item) => item.receiptDate.slice(0, 7));
    return [...new Set(months)].sort().reverse();
  });

  unknownCount = computed(
    () => this.groceriesSvc.allItems().filter((item) => item.categoryId === null).length,
  );

  filtered = computed<GroceryItem[]>(() => {
    let items = this.loadedItems();
    if (this.filterCategory() === 'unknown') {
      items = items.filter((item) => item.categoryId === null);
    }
    const col = this.sortCol(),
      dir = this.sortDir();
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

  hasMore = computed(() => this.loadedItems().length < this.totalCount());

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
          this.filterStore() +
          this.filterMonth() +
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
  }

  ngOnInit(): void {
    const qp = this.route.snapshot.queryParamMap;
    const cat = qp.get('categoryId');
    const mon = qp.get('month');
    if (cat) this.filterCategory.set(cat);
    if (mon) this.filterMonth.set(mon);
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
    this.totalAmount.set(0);
    this._skip = 0;
    this._fetchPage(0, this._visibleTarget);
  }

  private _fetchPage(skip: number, take: number): void {
    this.pageLoading.set(true);
    const params: Record<string, string | number> = { skip, take };
    if (this.filterStore()) params['store'] = this.filterStore();
    if (this.filterMonth()) params['month'] = this.filterMonth();
    const cat = this.filterCategory();
    if (cat && cat !== 'unknown') params['categoryId'] = cat;
    if (this.search()) params['search'] = this.search();
    params['sortCol'] = this.sortCol();
    params['sortDir'] = this.sortDir();

    this._currentSub = this.groceriesSvc.getItems(params).subscribe({
      next: (res) => {
        this.loadedItems.update((items) => [...items, ...res.items]);
        this.totalCount.set(res.totalCount);
        if (skip === 0) {
          this.totalAmount.set(res.totalAmount);
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

  toggleDropdown(itemId: number, e: MouseEvent): void {
    e.stopPropagation();
    const willClose = this.openDropdownId() === itemId;
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
      this.openDropdownId.set(itemId);
    }
  }

  selectCategory(item: GroceryItem, categoryId: number | null, e: MouseEvent): void {
    e.stopPropagation();
    this.openDropdownId.set(null);
    if (categoryId === item.categoryId) return;
    const wasAutoAssigned = !item.categorySetManually && item.categoryId !== null;
    const ruleForItem = wasAutoAssigned
      ? this.groceryCatSvc
          .rules()
          .find((r) => r.categoryId === item.categoryId && matchesRule(item, r.pattern, r.value))
      : null;

    if (wasAutoAssigned && ruleForItem) {
      this.pendingChange.set({
        item,
        newCategoryId: categoryId,
        ruleId: ruleForItem.id,
        pattern: ruleForItem.pattern ?? '',
      });
    } else if (categoryId !== null) {
      const cat = this.groceryCatSvc.categories().find((c) => c.id === categoryId);
      this.ruleCreatePattern.set(item.description);
      this.ruleCreateValue.set(null);
      this.pendingRuleCreate.set({ item, categoryId, categoryName: cat?.name ?? '' });
    } else {
      this.applyCategory(item.id, null, null);
    }
  }

  confirmPending(deleteRule: boolean): void {
    const p = this.pendingChange();
    if (!p) return;
    this.pendingChange.set(null);
    this.applyCategory(p.item.id, p.newCategoryId, deleteRule ? p.ruleId : null);
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
      this.groceryCatSvc
        .createRule(p.categoryId, this.ruleCreatePattern().trim(), this.ruleCreateValue())
        .subscribe(() => {
          this.groceryCatSvc.setItemCategory(p.item.id, p.categoryId).subscribe(() => {
            this.ruleCreateLoading.set(false);
            this.groceriesSvc.loadAllItems();
            this._resetAndLoad();
          });
        });
    } else {
      this.applyCategory(p.item.id, p.categoryId, null);
    }
  }

  openCreate(item: GroceryItem, e: MouseEvent): void {
    e.stopPropagation();
    this.openDropdownId.set(null);
    this.createItem.set(item);
    this.createName.set('');
    this.createColor.set('#a855f7');
    this.createPattern.set(item.description);
    this.createValue.set(null);
    this.showCreateModal.set(true);
  }

  submitCreate(): void {
    const item = this.createItem();
    if (!item || !this.createName().trim()) return;
    this.createLoading.set(true);
    this.groceryCatSvc
      .createCategory(
        this.createName().trim(),
        this.createColor(),
        this.createPattern().trim() || undefined,
        this.createValue(),
      )
      .subscribe((cat) => {
        this.groceryCatSvc.setItemCategory(item.id, cat.id).subscribe(() => {
          this.createLoading.set(false);
          this.showCreateModal.set(false);
          this.groceriesSvc.loadAllItems();
          this._resetAndLoad();
        });
      });
  }

  requestDeleteItem(item: GroceryItem, e: MouseEvent): void {
    e.stopPropagation();
    this.confirmDeleteItem.set(item);
  }

  confirmDelete(): void {
    const item = this.confirmDeleteItem();
    if (!item) return;
    this.confirmDeleteItem.set(null);
    this.groceriesSvc.deleteItem(item.id).subscribe(() => {
      this.groceriesSvc.reload();
      this.groceriesSvc.loadAllItems();
      this._resetAndLoad();
    });
  }

  openItemEdit(item: GroceryItem, e: MouseEvent): void {
    e.stopPropagation();
    this.editingItemId.set(item.id);
    this.itemDescription.set(item.description);
    this.itemAmount.set(item.amount);
    this.itemQuantity.set(item.quantity);
    this.showItemModal.set(true);
  }

  submitItemModal(): void {
    if (!this.itemFormValid()) return;
    this.itemLoading.set(true);
    this.groceriesSvc
      .updateItem(this.editingItemId()!, {
        description: this.itemDescription().trim(),
        amount: this.itemAmount()!,
        quantity: this.itemQuantity(),
      })
      .subscribe({
        next: () => {
          this.itemLoading.set(false);
          this.showItemModal.set(false);
          this.groceriesSvc.loadAllItems();
          this._resetAndLoad();
        },
        error: () => this.itemLoading.set(false),
      });
  }

  openCreateItem(): void {
    this.newItemReceiptId.set(null);
    this.newItemDescription.set('');
    this.newItemAmount.set(null);
    this.newItemQuantity.set(1);
    this.showCreateItemModal.set(true);
  }

  submitNewItem(): void {
    if (!this.newItemFormValid()) return;
    this.newItemLoading.set(true);
    this.groceriesSvc
      .createItem({
        receiptId: this.newItemReceiptId()!,
        description: this.newItemDescription().trim(),
        amount: this.newItemAmount()!,
        quantity: this.newItemQuantity(),
      })
      .subscribe({
        next: () => {
          this.newItemLoading.set(false);
          this.showCreateItemModal.set(false);
          this.groceriesSvc.reload();
          this.groceriesSvc.loadAllItems();
          this._resetAndLoad();
        },
        error: () => this.newItemLoading.set(false),
      });
  }

  requestDeleteReceipt(receipt: GroceryReceiptSummary, e: MouseEvent): void {
    e.stopPropagation();
    this.confirmDeleteReceipt.set(receipt);
  }

  confirmReceiptDelete(): void {
    const receipt = this.confirmDeleteReceipt();
    if (!receipt) return;
    this.confirmDeleteReceipt.set(null);
    this.groceriesSvc.deleteReceipt(receipt.id).subscribe(() => {
      this.groceriesSvc.reload();
      this.groceriesSvc.loadAllItems();
      this._resetAndLoad();
    });
  }

  private applyCategory(
    itemId: number,
    categoryId: number | null,
    deleteRuleId: number | null,
  ): void {
    this.groceryCatSvc
      .setItemCategory(itemId, categoryId, deleteRuleId ?? undefined)
      .subscribe(() => {
        this.groceriesSvc.loadAllItems();
        this._resetAndLoad();
      });
  }
}
