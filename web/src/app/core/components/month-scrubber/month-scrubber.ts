import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  afterNextRender,
  computed,
  input,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { MonthCell, monthKeyOf, monthName } from '../../utils/month-totals';
import { eur, signedEur } from '../../utils/money';

/** Months shown on desktop before "Earlier" is pressed. */
const PAGE = 6;

export interface ScrubberCell {
  key: string;
  label: string;
  shortLabel: string;
  kept: string;
  ariaLabel: string;
  inProgress: boolean;
  /** Bar heights as a share of the tallest month, 0–1. */
  inShare: number;
  outShare: number;
}

/**
 * Builds the scrubber's cells from its months, oldest first. The bars scale to the months from
 * `scaleFrom` on, those shown on desktop, so one exceptional month earlier doesn't flatten the
 * rest; earlier months clamp at full height. Exported for tests.
 */
export function scrubberCells(months: MonthCell[], nowKey: string, scaleFrom = 0): ScrubberCell[] {
  const shown = months.slice(scaleFrom);
  const max = Math.max(1, ...shown.flatMap((m) => [m.income, m.expenses]));
  return months.map((m) => {
    const inProgress = m.key === nowKey;
    const [year] = m.key.split('-');
    const name = monthName(m.key);
    const kept = m.income - m.expenses;
    return {
      key: m.key,
      label: name,
      shortLabel: monthName(m.key, 'short'),
      kept: inProgress ? 'In progress' : `${signedEur(kept, true)} ${kept < 0 ? 'over' : 'kept'}`,
      ariaLabel:
        `${name} ${year}${inProgress ? ', in progress' : ''}: ` +
        `in ${eur(m.income)}, out ${eur(m.expenses)}`,
      inProgress,
      inShare: Math.min(1, m.income / max),
      outShare: Math.min(1, m.expenses / max),
    };
  });
}

/**
 * The month picker on Home, Activity and Insights: one button per month with its money in and
 * out as two small bars. Selecting '' means all months, offered when `allowAll` is set.
 */
@Component({
  selector: 'app-month-scrubber',
  standalone: true,
  templateUrl: './month-scrubber.html',
  styleUrl: './month-scrubber.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MonthScrubberComponent {
  readonly months = input.required<MonthCell[]>();
  readonly selected = input<string>('');
  readonly allowAll = input(false);
  readonly selectedChange = output<string>();

  private readonly track = viewChild<ElementRef<HTMLElement>>('track');
  private readonly pages = signal(1);

  /** Index of the oldest month shown on desktop; the selected month is always shown. */
  readonly firstShown = computed(() => {
    const months = this.months();
    const byPage = Math.max(0, months.length - PAGE * this.pages());
    const selectedAt = months.findIndex((m) => m.key === this.selected());
    return selectedAt === -1 ? byPage : Math.min(byPage, selectedAt);
  });

  readonly cells = computed(() =>
    scrubberCells(this.months(), monthKeyOf(new Date()), this.firstShown()),
  );

  constructor() {
    // Phones scroll the chips sideways; start at the newest month.
    afterNextRender(() => {
      const el = this.track()?.nativeElement;
      if (el) el.scrollLeft = el.scrollWidth;
    });
  }

  showEarlier(): void {
    this.pages.update((p) => p + 1);
  }

  select(key: string): void {
    this.selectedChange.emit(key);
  }
}
