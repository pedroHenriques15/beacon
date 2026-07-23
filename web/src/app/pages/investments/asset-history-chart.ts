import {
  Component,
  ElementRef,
  OnDestroy,
  computed,
  effect,
  input,
  viewChild,
} from '@angular/core';
import {
  Chart,
  LineController,
  LineElement,
  PointElement,
  CategoryScale,
  LinearScale,
  Tooltip,
  Filler,
} from 'chart.js';
import { InvestmentPriceSnapshot } from '../../core/models/statement.model';

Chart.register(
  LineController,
  LineElement,
  PointElement,
  CategoryScale,
  LinearScale,
  Tooltip,
  Filler,
);

@Component({
  selector: 'app-asset-history-chart',
  standalone: true,
  template: `
    @if (points().length > 1) {
      <div class="inv-asset-chart"><canvas #canvas></canvas></div>
    }
  `,
})
export class AssetHistoryChart implements OnDestroy {
  snapshots = input.required<InvestmentPriceSnapshot[]>();
  range = input<'1M' | '3M' | '1Y' | 'All'>('1Y');

  private canvas = viewChild<ElementRef<HTMLCanvasElement>>('canvas');
  private chart?: Chart;

  points = computed(() => {
    const asc = [...this.snapshots()].sort((a, b) => a.date.localeCompare(b.date));
    const range = this.range();
    if (range === 'All') return asc;
    const days = range === '1M' ? 30 : range === '3M' ? 91 : 365;
    const cutoff = new Date();
    cutoff.setUTCDate(cutoff.getUTCDate() - days);
    const iso = cutoff.toISOString().slice(0, 10);
    return asc.filter((s) => s.date >= iso);
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

    const ctx = canvas.getContext('2d')!;
    const gradient = ctx.createLinearGradient(0, 0, 0, 160);
    gradient.addColorStop(0, 'rgba(99,102,241,0.3)');
    gradient.addColorStop(1, 'rgba(99,102,241,0)');

    this.chart = new Chart(canvas, {
      type: 'line',
      data: {
        labels: points.map((p) => p.date),
        datasets: [
          {
            data: points.map((p) => p.pricePerUnit),
            borderColor: '#6366f1',
            backgroundColor: gradient,
            fill: true,
            tension: 0.3,
            pointRadius: 0,
            pointHoverRadius: 4,
          },
        ],
      },
      options: {
        responsive: true,
        maintainAspectRatio: false,
        plugins: {
          legend: { display: false },
          tooltip: {
            callbacks: {
              label: (ctx) =>
                ` €${(ctx.parsed.y as number).toLocaleString('en-GB', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`,
            },
          },
        },
        scales: {
          x: {
            grid: { color: 'rgba(30,45,66,0.8)' },
            ticks: { color: '#64748b', font: { size: 10 }, maxTicksLimit: 6 },
          },
          y: {
            grid: { color: 'rgba(30,45,66,0.8)' },
            ticks: { color: '#64748b', font: { size: 10 } },
          },
        },
      },
    });
  }
}
