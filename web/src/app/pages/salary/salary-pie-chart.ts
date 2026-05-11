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
  type LegendOptions,
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
