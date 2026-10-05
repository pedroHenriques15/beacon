import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  afterNextRender,
  computed,
  input,
  linkedSignal,
  output,
  viewChild,
} from '@angular/core';

export interface PickerCategory {
  id: number;
  name: string;
  color: string | null;
}

/** The popover's size, for placing it next to the button that opened it. */
export const PICKER_SIZE = { width: 300, height: 420 };

/**
 * Category picker for one transaction or grocery item: a popover on desktop, a bottom sheet on
 * phones. With `progress` set it is a review step and offers Skip.
 */
@Component({
  selector: 'app-category-picker',
  standalone: true,
  templateUrl: './category-picker.html',
  styleUrl: './category-picker.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    '[style.--_top]': 'top() + "px"',
    '[style.--_left]': 'left() + "px"',
    '[class.review]': 'progress() !== ""',
  },
})
export class CategoryPickerComponent {
  readonly categories = input.required<readonly PickerCategory[]>();
  /** The transaction or item being categorised. */
  readonly rowId = input.required<number>();
  readonly selectedId = input<number | null>(null);
  readonly top = input(0);
  readonly left = input(0);
  readonly description = input('');
  readonly meta = input('');
  readonly amount = input('');
  /** '2 of 5' while reviewing; '' otherwise. */
  readonly progress = input('');

  readonly picked = output<number | null>();
  readonly create = output<void>();
  readonly skipped = output<void>();
  readonly closed = output<void>();

  /** Cleared whenever the picker moves on to another row. */
  readonly search = linkedSignal({ source: this.rowId, computation: () => '' });

  readonly shown = computed(() => {
    const q = this.search().trim().toLowerCase();
    return this.categories().filter((c) => !q || c.name.toLowerCase().includes(q));
  });

  private readonly searchBox = viewChild<ElementRef<HTMLInputElement>>('searchBox');

  constructor() {
    // Desktop only: on a phone, focusing the search would raise the keyboard over the sheet.
    afterNextRender(() => {
      if (window.matchMedia('(min-width: 640px)').matches) this.searchBox()?.nativeElement.focus();
    });
  }

  onSearch(event: Event): void {
    this.search.set((event.target as HTMLInputElement).value);
  }
}
