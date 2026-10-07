import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { CATEGORY_UNKNOWN } from '../../core/constants/categories';
import { eur, eurAxis, signedEur } from '../../core/utils/money';
import { CategoryBar, CategoryBars, Side } from './insights';

/**
 * Sorted bars by category: a share bar, then one bar per category. A row with a side shows its
 * amount signed, + for money in and − for money out. Each row is a button that picks the
 * category.
 */
@Component({
  selector: 'app-category-bars',
  standalone: true,
  templateUrl: './category-bars.html',
  styleUrl: './category-bars.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CategoryBarsComponent {
  readonly bars = input.required<CategoryBars>();
  /** The picked category's label. */
  readonly selected = input<string | null>(null);
  /** The side Unknown was picked from, since it can show on both. */
  readonly selectedSide = input<Side | null>(null);
  /** The shown period's name, for the amount column: 'September'. */
  readonly current = input('');
  /** Whether to show the share bar above the rows (one side only). */
  readonly showShare = input(true);
  readonly picked = output<CategoryBar>();

  readonly eur = eur;
  readonly eurAxis = eurAxis;
  readonly unknown = CATEGORY_UNKNOWN;

  readonly shareLabel = computed(
    () =>
      'Share of the total by category: ' +
      this.bars()
        .rows.filter((r) => r.share > 0)
        .map((r) => `${r.label} ${r.share.toFixed(1)}%`)
        .join(', '),
  );

  isSelected(row: CategoryBar): boolean {
    if (this.selected() !== row.label) return false;
    return row.label !== this.unknown || !this.selectedSide() || row.side === this.selectedSide();
  }

  amount(row: CategoryBar): string {
    if (!row.side) return eur(row.total);
    return signedEur(row.side === 'in' ? row.total : -row.total);
  }

  rowLabel(row: CategoryBar): string {
    const of = row.side === 'in' ? 'money in' : row.side === 'out' ? 'money out' : 'the total';
    let text = `${row.label}: ${this.amount(row)}, ${row.share.toFixed(1)}% of ${of}`;
    if (row.detail) text += `; ${row.detail}`;
    return text;
  }
}
