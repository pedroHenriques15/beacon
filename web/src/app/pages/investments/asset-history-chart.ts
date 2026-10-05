import {
  Component,
  ElementRef,
  OnDestroy,
  computed,
  effect,
  input,
  viewChild,
  ChangeDetectionStrategy,
} from '@angular/core';
import {
  Chart,
  LineController,
  LineElement,
  PointElement,
  CategoryScale,
  LinearScale,
  Tooltip,
  Legend,
  Filler,
} from 'chart.js';
import { InvestmentPriceSnapshot } from '../../core/models/statement.model';
import { applyChartTheme, axisOptions, withAlpha } from '../../core/charts/chart-theme';
import { dayValue, eurPrice, eurTick, shortDate, spanDays, timeTick } from './investments-view';

export const HISTORY_RANGES = ['1M', '3M', '1Y', '5Y', 'All'] as const;
export type HistoryRange = (typeof HISTORY_RANGES)[number];

const RANGE_DAYS: Record<Exclude<HistoryRange, 'All'>, number> = {
  '1M': 30,
  '3M': 91,
  '1Y': 365,
  '5Y': 1826,
};

/** The first ISO date inside the range, or null for 'All'. */
export function rangeCutoff(range: HistoryRange): string | null {
  if (range === 'All') return null;
  const cutoff = new Date();
  cutoff.setUTCDate(cutoff.getUTCDate() - RANGE_DAYS[range]);
  return cutoff.toISOString().slice(0, 10);
}

// Legend is registered for its defaults, which applyChartTheme() sets; the legend stays hidden.
Chart.register(
  LineController,
  LineElement,
  PointElement,
  CategoryScale,
  LinearScale,
  Tooltip,
  Legend,
  Filler,
);

/** One asset's price per unit over the chosen range, a line in the money-in colour. */
@Component({
  selector: 'app-asset-history-chart',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.Eager,
  template: `
    @if (points().length > 1) {
      <div class="asset-chart">
        <canvas
          #canvas
          role="img"
          [attr.aria-label]="
            'Price per unit from ' +
            shortDate(points()[0].date) +
            ' to ' +
            shortDate(points()[points().length - 1].date) +
            ', last ' +
            eurPrice(points()[points().length - 1].pricePerUnit)
          "
        ></canvas>
      </div>
    }
  `,
  styles: `
    :host {
      display: block;
    }

    .asset-chart {
      position: relative;
      height: 180px;
    }
  `,
})
export class AssetHistoryChart implements OnDestroy {
  snapshots = input.required<InvestmentPriceSnapshot[]>();
  range = input<HistoryRange>('1Y');

  readonly shortDate = shortDate;
  readonly eurPrice = eurPrice;

  private canvas = viewChild<ElementRef<HTMLCanvasElement>>('canvas');
  private chart?: Chart;

  points = computed(() => {
    const asc = [...this.snapshots()].sort((a, b) => a.date.localeCompare(b.date));
    const cutoff = rangeCutoff(this.range());
    return cutoff == null ? asc : asc.filter((s) => s.date >= cutoff);
  });

  constructor() {
    effect(() => {
      this.canvas();
      this.points();
      this.render();
    });
  }

  ngOnDestroy(): void {
    this.chart?.destroy();
  }

  private render(): void {
    this.chart?.destroy();
    const points = this.points();
    const canvas = this.canvas()?.nativeElement;
    if (points.length < 2 || !canvas) return;

    const theme = applyChartTheme();
    const axis = axisOptions(theme);
    const xAxis = axisOptions(theme, false);
    const longSpan = spanDays(points) > 120;

    this.chart = new Chart(canvas, {
      type: 'line',
      data: {
        datasets: [
          {
            data: points.map((p) => ({ x: dayValue(p.date), y: p.pricePerUnit })),
            borderColor: theme.credit,
            borderWidth: 2,
            backgroundColor: (ctx) => {
              const area = ctx.chart.chartArea;
              if (!area) return withAlpha(theme.credit, 0.1);
              const gradient = ctx.chart.ctx.createLinearGradient(0, area.top, 0, area.bottom);
              gradient.addColorStop(0, withAlpha(theme.credit, 0.25));
              gradient.addColorStop(1, withAlpha(theme.credit, 0));
              return gradient;
            },
            fill: true,
            tension: 0.3,
            pointRadius: 0,
            pointHoverRadius: 4,
            pointBackgroundColor: theme.credit,
          },
        ],
      },
      options: {
        responsive: true,
        maintainAspectRatio: false,
        interaction: { mode: 'index', intersect: false },
        plugins: {
          legend: { display: false },
          tooltip: {
            callbacks: {
              title: (items) => shortDate(points[items[0].dataIndex].date),
              label: (ctx) => ` ${eurPrice(ctx.parsed.y as number)}`,
            },
          },
        },
        scales: {
          // Time-proportional: daily closes and sparse early prices keep their spacing.
          x: {
            ...xAxis,
            type: 'linear',
            min: dayValue(points[0].date),
            max: dayValue(points[points.length - 1].date),
            ticks: {
              ...xAxis.ticks,
              maxTicksLimit: 5,
              maxRotation: 0,
              callback: (_value, index, ticks) => timeTick(ticks, index, longSpan),
            },
          },
          y: {
            ...axis,
            ticks: {
              ...axis.ticks,
              maxTicksLimit: 5,
              callback: (value) => eurTick(Number(value)),
            },
          },
        },
      },
    });
  }
}
