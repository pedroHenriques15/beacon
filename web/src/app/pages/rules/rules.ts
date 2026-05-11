import { Component, inject, signal, computed } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { CategoriesService } from '../../core/services/categories.service';
import { FinanceService } from '../../core/services/finance.service';
import { Category, CategoryRule } from '../../core/models/statement.model';
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
}
