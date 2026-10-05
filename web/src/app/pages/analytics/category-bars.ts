import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { CATEGORY_UNKNOWN } from '../../core/constants/categories';
import { eur, eurAxis } from '../../core/utils/money';
import { CategoryBar, CategoryBars, signedPct } from './insights';

/**
 * "Where it went": a share bar, then one sorted bar per category with a tick at the previous
 * month's amount. Each row is a button that picks the category.
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
  /** The shown period's name, for the legend and the amount column: 'September'. */
  readonly current = input('');
  /** The compared month's name; null when nothing is compared. */
  readonly previous = input<string | null>(null);
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

  changeText(row: CategoryBar): string {
    if (row.change === null) return '';
    if (row.changePct !== null) return signedPct(row.changePct);
    return 'New';
  }

  rowLabel(row: CategoryBar): string {
    const prev = this.previous();
    let text = `${row.label}: ${eur(row.total)}, ${row.share.toFixed(1)}% of the total`;
    if (prev && row.previous !== null) text += `; ${eur(row.previous)} in ${prev}`;
    return text;
  }
}
