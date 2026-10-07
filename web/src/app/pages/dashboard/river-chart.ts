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
import { daysInMonth, monthName } from '../../core/utils/month-totals';
import { eur, eurAxis, signedEur } from '../../core/utils/money';
import { RiverMark, RiverSeries, markRadius, niceScale, smoothPath } from './river';

interface Dot {
  id: number;
  cx: number;
  cy: number;
  r: number;
  hollow: boolean;
  tip: string;
}

interface Label {
  x: number;
  y: number;
  anchor: 'start' | 'end';
  text: string;
}

function shortName(mark: RiverMark): string {
  const name = (mark.category ?? mark.description).trim();
  return name.length > 22 ? name.slice(0, 21) + '…' : name;
}

/**
 * "Spent in <month>, day by day": cumulative counted spending through the month against the
 * previous one, a dot per outflow sized by amount (hollow when it needs a category) and the
 * month's money in on a strip below. Hand-drawn SVG, sized to its container.
 */
@Component({
  selector: 'app-river-chart',
  standalone: true,
  templateUrl: './river-chart.html',
  styleUrl: './river-chart.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class RiverChartComponent {
  readonly series = input.required<RiverSeries>();

  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  readonly width = signal(960);

  constructor() {
    const destroyRef = inject(DestroyRef);
    afterNextRender(() => {
      const el = this.host.nativeElement;
      const measure = () => this.width.set(Math.max(280, Math.round(el.clientWidth)));
      measure();
      if (typeof ResizeObserver === 'undefined') return;
      const observer = new ResizeObserver(measure);
      observer.observe(el);
      destroyRef.onDestroy(() => observer.disconnect());
    });
  }

  readonly compact = computed(() => this.width() < 600);

  readonly geometry = computed(() => {
    const w = this.width();
    const compact = this.compact();
    const s = this.series();
    const box = compact
      ? { h: 196, left: 34, right: w - 10, top: 22, bottom: 146, axis: 163, strip: 182 }
      : { h: 360, left: 64, right: w - 130, top: 20, bottom: 262, axis: 288, strip: 326 };
    // Paybacks take the line down, so its top can come before the month's end.
    const scale = niceScale(Math.max(1, ...s.current, ...s.last));
    const x = (day: number) => box.left + ((day - 1) / 30) * (box.right - box.left);
    // A payback before its expense can take the line below zero; it is drawn at zero.
    const y = (v: number) => box.bottom - (Math.max(0, v) / scale.top) * (box.bottom - box.top);
    const rScale = compact ? 0.72 : 1;

    const linePts = s.current.map((v, i): [number, number] => [x(i + 1), y(v)]);
    const line = smoothPath(linePts);
    const lastPts = s.last.map((v, i): [number, number] => [x(i + 1), y(v)]);
    const area =
      linePts.length > 1
        ? `${line} L${linePts[linePts.length - 1][0].toFixed(1)} ${box.bottom} L${box.left} ${box.bottom} Z`
        : '';

    const grid: { y: number; label: string }[] = [];
    for (let v = 0; v <= scale.top + 0.001; v += scale.step) {
      grid.push({ y: y(v), label: eurAxis(v) });
    }

    const day = (d: number) => `${d} ${monthName(s.month, 'short')}`;
    const outDots: Dot[] = s.outflows
      .map((o) => ({
        id: o.id,
        cx: x(o.day),
        cy: y(o.at),
        r: markRadius(o.amount, rScale, compact ? 9 : 14),
        hollow: o.needsCategory,
        tip: `${o.description}, ${day(o.day)}, ${signedEur(-o.amount)}${
          o.needsCategory ? ', needs a category' : ''
        }`,
      }))
      // Large dots first, so small ones stay visible on top.
      .sort((a, b) => b.r - a.r);
    const inDots: Dot[] = s.inflows.map((o) => ({
      id: o.id,
      cx: x(o.day),
      cy: box.strip,
      r: markRadius(o.amount, rScale, compact ? 9 : 14),
      hollow: false,
      tip: `${o.description}, ${day(o.day)}, ${signedEur(o.amount)}`,
    }));

    const labels: Label[] = [];
    if (!compact) {
      const mid = (box.left + box.right) / 2;
      // Beside a money-in dot on the strip.
      const place = (cx: number, cy: number, r: number, text: string): Label =>
        cx < mid
          ? { x: cx + r + 8, y: cy + 4, anchor: 'start', text }
          : { x: cx - r - 8, y: cy + 4, anchor: 'end', text };
      // Off the rising line: below-right of the dot early on, above-left later.
      const placeOff = (cx: number, cy: number, r: number, text: string): Label =>
        cx < mid
          ? { x: cx + r * 0.6, y: cy + r + 16, anchor: 'start', text }
          : { x: cx - r * 0.6, y: cy - r - 8, anchor: 'end', text };
      const biggestOut = [...s.outflows].sort((a, b) => b.amount - a.amount)[0];
      if (biggestOut) {
        const dot = outDots.find((d) => d.id === biggestOut.id)!;
        labels.push(
          placeOff(
            dot.cx,
            dot.cy,
            dot.r,
            `${shortName(biggestOut)} ${signedEur(-biggestOut.amount)}`,
          ),
        );
      }
      const bigIns = [...s.inflows].sort((a, b) => b.amount - a.amount).slice(0, 2);
      bigIns.forEach((mark, i) => {
        const dot = inDots.find((d) => d.id === mark.id)!;
        if (i === 1 && Math.abs(dot.cx - inDots.find((d) => d.id === bigIns[0].id)!.cx) < 260) {
          return;
        }
        labels.push(place(dot.cx, dot.cy, dot.r, `${shortName(mark)} ${signedEur(mark.amount)}`));
      });
    }

    const days = daysInMonth(s.month);
    const ticks = [1, 8, 15, 22, days].map((d) => ({
      x: x(d),
      label: d === 1 ? day(1) : String(d),
      anchor: d === 1 ? 'start' : 'middle',
    }));

    return {
      w,
      ...box,
      grid,
      ticks,
      line,
      area,
      lastLine: smoothPath(lastPts),
      outDots,
      inDots,
      labels,
      endLabels: compact
        ? []
        : [
            { y: 35, name: monthName(s.previous, 'short'), value: eur(s.lastTotal) },
            { y: 62, name: monthName(s.month, 'short'), value: eur(s.total) },
          ],
    };
  });

  readonly summary = computed(() => {
    const s = this.series();
    const uncategorised = s.outflows.filter((o) => o.needsCategory).length;
    return (
      `Cumulative spending through ${monthName(s.month)} against ${monthName(s.previous)}. ` +
      `${monthName(s.month)} is at ${eur(s.total)}, ${monthName(s.previous)} ended at ${eur(s.lastTotal)}. ` +
      `Each dot is a transaction sized by amount` +
      (uncategorised ? `; ${uncategorised} hollow dots still need a category.` : '.') +
      ` Money in is shown on the lower strip.`
    );
  });
}
