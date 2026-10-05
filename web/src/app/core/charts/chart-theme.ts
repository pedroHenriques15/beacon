import { Chart } from 'chart.js';

/** The design tokens a chart needs, read from the CSS variables on `:root`. */
export interface ChartTheme {
  text: string;
  textPrimary: string;
  grid: string;
  border: string;
  surface: string;
  raised: string;
  credit: string;
  debit: string;
  primary: string;
  neutral: string;
  fallback: string;
  fontBody: string;
  fontFigures: string;
}

const VARIABLES: Record<keyof ChartTheme, string> = {
  text: '--text-secondary',
  textPrimary: '--text-primary',
  grid: '--chart-grid',
  border: '--border',
  surface: '--surface',
  raised: '--surface-raised',
  credit: '--credit',
  debit: '--debit',
  primary: '--primary',
  neutral: '--neutral-mark',
  fallback: '--category-fallback',
  fontBody: '--font-body',
  fontFigures: '--font-figures',
};

export function readChartTheme(root: Element = document.documentElement): ChartTheme {
  const style = getComputedStyle(root);
  const theme = {} as ChartTheme;
  for (const [key, variable] of Object.entries(VARIABLES)) {
    theme[key as keyof ChartTheme] = style.getPropertyValue(variable).trim();
  }
  return theme;
}

/** A category's own colour, or the "Other" colour when it has none. */
export function categoryColor(color: string | null | undefined, theme = readChartTheme()): string {
  return color || theme.fallback;
}

/** `#rrggbb` (or `#rgb`) with an alpha, as canvas needs it; other colours pass through. */
export function withAlpha(color: string, alpha: number): string {
  const hex = color.trim().replace('#', '');
  if (!/^[0-9a-f]{3}([0-9a-f]{3})?$/i.test(hex)) return color;
  const full =
    hex.length === 3
      ? hex
          .split('')
          .map((c) => c + c)
          .join('')
      : hex;
  const [r, g, b] = [0, 2, 4].map((i) => parseInt(full.slice(i, i + 2), 16));
  return `rgba(${r}, ${g}, ${b}, ${alpha})`;
}

/** Ticks in secondary text, gridlines in --chart-grid, no axis border. */
export function axisOptions(theme: ChartTheme, gridLines = true) {
  return {
    grid: { color: theme.grid, display: gridLines },
    border: { display: false },
    ticks: { color: theme.text, font: { family: theme.fontBody, size: 11 } },
  };
}

/** Sets chart.js defaults from the tokens. Call before creating a chart; it is cheap. */
export function applyChartTheme(): ChartTheme {
  const theme = readChartTheme();
  Chart.defaults.color = theme.text;
  Chart.defaults.borderColor = theme.grid;
  Chart.defaults.font.family = theme.fontBody;
  Chart.defaults.font.size = 12;

  const legend = Chart.defaults.plugins.legend.labels;
  legend.color = theme.text;
  legend.usePointStyle = true;
  legend.boxWidth = 8;

  const tooltip = Chart.defaults.plugins.tooltip;
  tooltip.backgroundColor = theme.raised;
  tooltip.titleColor = theme.textPrimary;
  tooltip.bodyColor = theme.textPrimary;
  tooltip.borderColor = theme.border;
  tooltip.borderWidth = 1;
  tooltip.padding = 10;
  tooltip.cornerRadius = 12;
  tooltip.titleFont = { family: theme.fontBody, weight: 'bold', size: 12 };
  tooltip.bodyFont = { family: theme.fontFigures, size: 12 };
  return theme;
}
