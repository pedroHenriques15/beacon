import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  afterNextRender,
  computed,
  inject,
  input,
  signal,
} from '@angular/core';
import { SalarySlip } from '../../core/models/statement.model';
import { flowLayout, flowSummary, slipFlowParts } from './salary-flow';

/**
 * A slip as a flow: its income lines into the gross bar, and the gross into take-home and each
 * deduction and tax line, every band as thick as its amount. Hand-drawn SVG, sized to its
 * container; below 600 px it drops the income column.
 */
@Component({
  selector: 'app-slip-flow',
  standalone: true,
  templateUrl: './slip-flow.html',
  styleUrl: './slip-flow.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SlipFlowComponent {
  readonly slip = input.required<SalarySlip>();

  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  readonly width = signal(820);

  readonly parts = computed(() => slipFlowParts(this.slip()));
  readonly layout = computed(() => flowLayout(this.parts(), this.width()));
  readonly summary = computed(() => flowSummary(this.parts()));

  constructor() {
    const destroyRef = inject(DestroyRef);
    afterNextRender(() => {
      const el = this.host.nativeElement;
      const measure = () => this.width.set(Math.max(260, Math.round(el.clientWidth)));
      measure();
      if (typeof ResizeObserver === 'undefined') return;
      const observer = new ResizeObserver(measure);
      observer.observe(el);
      destroyRef.onDestroy(() => observer.disconnect());
    });
  }
}
