import { describe, it, expect, afterEach } from 'vitest';
import { categoryColor, readChartTheme, withAlpha } from './chart-theme';

describe('chart-theme', () => {
  afterEach(() => {
    document.documentElement.removeAttribute('style');
  });

  it('reads the tokens from the CSS variables', () => {
    document.documentElement.style.setProperty('--credit', '#7fe3c4');
    document.documentElement.style.setProperty('--chart-grid', '#16223a');

    const theme = readChartTheme();

    expect(theme.credit).toBe('#7fe3c4');
    expect(theme.grid).toBe('#16223a');
  });

  it('falls back to --category-fallback for a category without colour', () => {
    document.documentElement.style.setProperty('--category-fallback', '#5c6880');

    expect(categoryColor(null)).toBe('#5c6880');
    expect(categoryColor('')).toBe('#5c6880');
    expect(categoryColor('#ff0000')).toBe('#ff0000');
  });

  it('adds an alpha to hex colours and leaves others alone', () => {
    expect(withAlpha('#7fe3c4', 0.1)).toBe('rgba(127, 227, 196, 0.1)');
    expect(withAlpha('#fff', 0.5)).toBe('rgba(255, 255, 255, 0.5)');
    expect(withAlpha('rgba(1, 2, 3, 0.4)', 0.1)).toBe('rgba(1, 2, 3, 0.4)');
  });
});
