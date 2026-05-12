import { Component, inject, signal, computed } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { CategoriesService } from '../../core/services/categories.service';
import { FinanceService } from '../../core/services/finance.service';
import { GroceryCategoriesService } from '../../core/services/grocery-categories.service';
import { GroceriesService } from '../../core/services/groceries.service';
import { Category, CategoryRule } from '../../core/models/statement.model';
import { GroceryCategory, GroceryCategoryRule } from '../../core/models/grocery.model';
import { matchesRule } from '../../core/utils/rule-match';

@Component({
  selector: 'app-rules',
  standalone: true,
  imports: [FormsModule],
  templateUrl: './rules.html',
  styleUrl: './rules.scss',
})
export class RulesComponent {
  catSvc = inject(CategoriesService);
  finance = inject(FinanceService);
  gCatSvc = inject(GroceryCategoriesService);
  groceriesSvc = inject(GroceriesService);

  activeTab = signal<'transactions' | 'groceries'>('transactions');

  catSearch = signal('');

  showAddRule = signal(false);
  newCategoryId = signal<number | null>(null);
  newPattern = signal('');
  newValue = signal<number | null>(null);
  saving = signal(false);

  editingCat = signal<Category | null>(null);
  editName = signal('');
  editColor = signal('');
  editSaving = signal(false);

  editingRule = signal<CategoryRule | null>(null);
  editRulePattern = signal('');
  editRuleValue = signal<number | null>(null);
  editRuleSaving = signal(false);

  newRuleMatchCount = computed(() => {
    const pat = this.newPattern().trim();
    const val = this.newValue();
    if (!pat && val === null) return null;
    return this.finance.allTransactions().filter((tx) => matchesRule(tx, pat, val)).length;
  });

  editRuleMatchCount = computed(() => {
    const pat = this.editRulePattern().trim();
    const val = this.editRuleValue();
    if (!pat && val === null) return null;
    return this.finance.allTransactions().filter((tx) => matchesRule(tx, pat, val)).length;
  });

  showCreateCatModal = signal(false);
  createCatName = signal('');
  createCatColor = signal('#a855f7');
  createCatPattern = signal('');
  createCatValue = signal<number | null>(null);
  createCatLoading = signal(false);

  gCatSearch = signal('');

  gShowAddRule = signal(false);
  gNewCategoryId = signal<number | null>(null);
  gNewPattern = signal('');
  gNewValue = signal<number | null>(null);
  gSaving = signal(false);

  gEditingCat = signal<GroceryCategory | null>(null);
  gEditName = signal('');
  gEditColor = signal('');
  gEditSaving = signal(false);

  gEditingRule = signal<GroceryCategoryRule | null>(null);
  gEditRulePattern = signal('');
  gEditRuleValue = signal<number | null>(null);
  gEditRuleSaving = signal(false);

  gNewRuleMatchCount = computed(() => {
    const pat = this.gNewPattern().trim();
    const val = this.gNewValue();
    if (!pat && val === null) return null;
    return this.groceriesSvc.allItems().filter((item) => matchesRule(item, pat, val)).length;
  });

  gEditRuleMatchCount = computed(() => {
    const pat = this.gEditRulePattern().trim();
    const val = this.gEditRuleValue();
    if (!pat && val === null) return null;
    return this.groceriesSvc.allItems().filter((item) => matchesRule(item, pat, val)).length;
  });

  gShowCreateCatModal = signal(false);
  gCreateCatName = signal('');
  gCreateCatColor = signal('#a855f7');
  gCreateCatPattern = signal('');
  gCreateCatValue = signal<number | null>(null);
  gCreateCatLoading = signal(false);

  submitRule(): void {
    const catId = this.newCategoryId();
    const pat = this.newPattern().trim();
    const val = this.newValue();
    if (!catId || (!pat && val === null)) return;
    this.saving.set(true);
    this.catSvc.createRule(catId, pat, val).subscribe(() => {
      this.saving.set(false);
      this.showAddRule.set(false);
      this.newPattern.set('');
      this.newValue.set(null);
      this.newCategoryId.set(null);
    });
  }

  openEdit(cat: Category): void {
    this.editingCat.set(cat);
    this.editName.set(cat.name);
    this.editColor.set(cat.color);
  }

  submitEdit(): void {
    const cat = this.editingCat();
    if (!cat || !this.editName().trim()) return;
    this.editSaving.set(true);
    this.catSvc.updateCategory(cat.id, this.editName().trim(), this.editColor()).subscribe(() => {
      this.editSaving.set(false);
      this.editingCat.set(null);
      this.finance.reload();
    });
  }

  openEditRule(rule: CategoryRule): void {
    this.editingRule.set(rule);
    this.editRulePattern.set(rule.pattern ?? '');
    this.editRuleValue.set(rule.value ?? null);
  }

  submitEditRule(): void {
    const rule = this.editingRule();
    if (!rule) return;
    const pat = this.editRulePattern().trim() || null;
    const val = this.editRuleValue();
    if (!pat && val === null) return;
    this.editRuleSaving.set(true);
    this.catSvc.updateRule(rule.id, pat, val).subscribe(() => {
      this.editRuleSaving.set(false);
      this.editingRule.set(null);
    });
  }

  openCreateCat(): void {
    this.createCatName.set('');
    this.createCatColor.set('#a855f7');
    this.createCatPattern.set('');
    this.createCatValue.set(null);
    this.showCreateCatModal.set(true);
  }

  submitCreateCat(): void {
    if (!this.createCatName().trim()) return;
    this.createCatLoading.set(true);
    this.catSvc
      .createCategory(
        this.createCatName().trim(),
        this.createCatColor(),
        this.createCatPattern().trim() || undefined,
        this.createCatValue(),
      )
      .subscribe(() => {
        this.createCatLoading.set(false);
        this.showCreateCatModal.set(false);
      });
  }

  deleteRule(id: number): void {
    if (!confirm('Delete this rule? Existing transactions will keep their current category.'))
      return;
    this.catSvc.deleteRule(id).subscribe();
  }

  deleteCategory(cat: Category): void {
    if (
      !confirm(
        `Delete category "${cat.name}"? Rules will be removed and transactions will become uncategorized.`,
      )
    )
      return;
    this.catSvc.deleteCategory(cat.id).subscribe();
  }

  gSubmitRule(): void {
    const catId = this.gNewCategoryId();
    const pat = this.gNewPattern().trim();
    const val = this.gNewValue();
    if (!catId || (!pat && val === null)) return;
    this.gSaving.set(true);
    this.gCatSvc.createRule(catId, pat, val).subscribe(() => {
      this.gSaving.set(false);
      this.gShowAddRule.set(false);
      this.gNewPattern.set('');
      this.gNewValue.set(null);
      this.gNewCategoryId.set(null);
    });
  }

  gOpenEdit(cat: GroceryCategory): void {
    this.gEditingCat.set(cat);
    this.gEditName.set(cat.name);
    this.gEditColor.set(cat.color);
  }

  gSubmitEdit(): void {
    const cat = this.gEditingCat();
    if (!cat || !this.gEditName().trim()) return;
    this.gEditSaving.set(true);
    this.gCatSvc
      .updateCategory(cat.id, this.gEditName().trim(), this.gEditColor())
      .subscribe(() => {
        this.gEditSaving.set(false);
        this.gEditingCat.set(null);
      });
  }

  gOpenEditRule(rule: GroceryCategoryRule): void {
    this.gEditingRule.set(rule);
    this.gEditRulePattern.set(rule.pattern ?? '');
    this.gEditRuleValue.set(rule.value ?? null);
  }

  gSubmitEditRule(): void {
    const rule = this.gEditingRule();
    if (!rule) return;
    const pat = this.gEditRulePattern().trim() || null;
    const val = this.gEditRuleValue();
    if (!pat && val === null) return;
    this.gEditRuleSaving.set(true);
    this.gCatSvc.updateRule(rule.id, pat, val).subscribe(() => {
      this.gEditRuleSaving.set(false);
      this.gEditingRule.set(null);
    });
  }

  gOpenCreateCat(): void {
    this.gCreateCatName.set('');
    this.gCreateCatColor.set('#a855f7');
    this.gCreateCatPattern.set('');
    this.gCreateCatValue.set(null);
    this.gShowCreateCatModal.set(true);
  }

  gSubmitCreateCat(): void {
    if (!this.gCreateCatName().trim()) return;
    this.gCreateCatLoading.set(true);
    this.gCatSvc
      .createCategory(
        this.gCreateCatName().trim(),
        this.gCreateCatColor(),
        this.gCreateCatPattern().trim() || undefined,
        this.gCreateCatValue(),
      )
      .subscribe(() => {
        this.gCreateCatLoading.set(false);
        this.gShowCreateCatModal.set(false);
      });
  }

  gDeleteRule(id: number): void {
    if (!confirm('Delete this rule? Existing items will keep their current category.')) return;
    this.gCatSvc.deleteRule(id).subscribe();
  }

  gDeleteCategory(cat: GroceryCategory): void {
    if (
      !confirm(
        `Delete category "${cat.name}"? Rules will be removed and items will become uncategorized.`,
      )
    )
      return;
    this.gCatSvc.deleteCategory(cat.id).subscribe();
  }
}
