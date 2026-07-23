import {
  Component,
  Input,
  ViewChild,
  ElementRef,
  AfterViewInit,
  OnChanges,
  OnDestroy,
  SimpleChanges,
} from '@angular/core';
import {
  Chart,
  ArcElement,
  DoughnutController,
  Tooltip,
  Legend,
  type TooltipModel,
} from 'chart.js';
import { SalaryLineItem, SalarySlip } from '../../core/models/statement.model';

Chart.register(ArcElement, DoughnutController, Tooltip, Legend);

@Component({
  selector: 'app-salary-pie-chart',
  standalone: true,
  template: `
    @if (!slip || !slip.lineItems.length) {
      <div class="chart-empty">No line items</div>
    } @else {
      <canvas #canvas></canvas>
    }
  `,
  styles: [
    `
      :host {
        display: block;
        position: relative;
      }
      canvas {
        display: block;
        max-width: 100%;
      }
      .chart-empty {
        color: #475569;
        font-size: 0.82rem;
        text-align: center;
        padding: 2rem;
      }
    `,
  ],
})
export class SalaryPieChartComponent implements AfterViewInit, OnChanges, OnDestroy {
  @Input() slip?: SalarySlip | null;
  @Input() legendPosition: 'top' | 'bottom' | 'left' | 'right' = 'right';
  @Input() showLegend = true;
  @ViewChild('canvas') canvasRef?: ElementRef<HTMLCanvasElement>;
  private chart?: Chart;
  private tooltipEl?: HTMLDivElement;

  constructor(private host: ElementRef<HTMLElement>) {}

  ngAfterViewInit(): void {
    this.renderChart();
  }

  ngOnChanges(changes: SimpleChanges): void {
    if ((changes['slip'] || changes['legendPosition'] || changes['showLegend']) && this.canvasRef) {
      this.renderChart();
    }
  }

  ngOnDestroy(): void {
    this.chart?.destroy();
    this.tooltipEl?.remove();
  }

  private typeColors(items: SalaryLineItem[]): string[] {
    const typeCounts: Record<string, number> = {};
    const typeIndices: Record<string, number> = {};

    for (const li of items) {
      typeCounts[li.categoryItemType] = (typeCounts[li.categoryItemType] ?? 0) + 1;
    }

    return items.map((li) => {
      const type = li.categoryItemType;
      const idx = typeIndices[type] ?? 0;
      typeIndices[type] = idx + 1;
      const count = typeCounts[type];
      const t = count > 1 ? idx / (count - 1) : 0.5;

      if (type === 'income') {
        const l = Math.round(42 + t * 22);
        return `hsl(152, 68%, ${l}%)`;
      } else if (type === 'deduction') {
        const l = Math.round(48 + t * 18);
        return `hsl(0, 68%, ${l}%)`;
      } else {
        const l = Math.round(48 + t * 16);
        return `hsl(38, 90%, ${l}%)`;
      }
    });
  }

  private getOrCreateTooltipEl(): HTMLDivElement {
    if (!this.tooltipEl) {
      const el = document.createElement('div');
      el.style.cssText = [
        'position:absolute',
        'background:var(--surface)',
        'border:1px solid var(--border)',
        'border-radius:8px',
        'padding:0.45rem 0.75rem',
        'font-size:0.8rem',
        'color:var(--text-primary)',
        'pointer-events:none',
        'white-space:nowrap',
        'z-index:10',
        'transition:opacity 0.1s',
      ].join(';');
      this.host.nativeElement.appendChild(el);
      this.tooltipEl = el;
    }
    return this.tooltipEl;
  }

  private externalTooltipHandler = ({
    chart,
    tooltip,
  }: {
    chart: Chart;
    tooltip: TooltipModel<'doughnut'>;
  }): void => {
    const el = this.getOrCreateTooltipEl();

    if (tooltip.opacity === 0) {
      el.style.opacity = '0';
      return;
    }

    if (tooltip.body) {
      const lines = tooltip.body.flatMap((b) => b.lines);
      el.textContent = lines.join(' ');
    }

    const canvas = chart.canvas;
    const rect = canvas.getBoundingClientRect();
    const hostRect = this.host.nativeElement.getBoundingClientRect();

    const x = rect.left - hostRect.left + tooltip.caretX;
    const y = rect.top - hostRect.top + tooltip.caretY;

    el.style.opacity = '1';
    el.style.left = `${x}px`;
    el.style.top = `${y}px`;
    el.style.transform = 'translate(-50%, calc(-100% - 6px))';
  };

  private renderChart(): void {
    this.chart?.destroy();
    const canvas = this.canvasRef?.nativeElement;
    if (!canvas || !this.slip?.lineItems?.length) return;

    const items = this.slip.lineItems;
    this.chart = new Chart(canvas, {
      type: 'doughnut',
      data: {
        labels: items.map((li) => li.categoryName),
        datasets: [
          {
            data: items.map((li) => li.amount),
            backgroundColor: this.typeColors(items),
            borderWidth: 2,
            borderColor: '#1a2536',
          },
        ],
      },
      options: {
        responsive: true,
        maintainAspectRatio: true,
        plugins: {
          legend: {
            display: this.showLegend,
            position: this.legendPosition,
            labels: { color: '#94a3b8', padding: 12, font: { size: 11 } },
          },
          tooltip: {
            enabled: false,
            // eslint-disable-next-line @typescript-eslint/no-explicit-any
            external: this.externalTooltipHandler as any,
            callbacks: {
              label: (ctx) => ` ${ctx.label}: €${(ctx.parsed as number).toFixed(2)}`,
            },
          },
        },
        cutout: '55%',
      },
    });
  }
}
