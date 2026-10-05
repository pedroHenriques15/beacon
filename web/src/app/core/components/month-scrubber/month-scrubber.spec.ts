import { describe, it, expect, beforeEach } from 'vitest';
import { TestBed, ComponentFixture } from '@angular/core/testing';
import { MonthScrubberComponent, monthsThatFit, scrubberCells } from './month-scrubber';

describe('scrubberCells', () => {
  it('labels each month with what was kept, and the current month as in progress', () => {
    const cells = scrubberCells(
      [
        { key: '2026-08', income: 2950, expenses: 2104.8 },
        { key: '2026-09', income: 1000, expenses: 1200 },
        { key: '2026-10', income: 10, expenses: 5 },
      ],
      '2026-10',
    );

    expect(cells.map((c) => c.kept)).toEqual(['+€845 kept', '−€200 over', 'In progress']);
    expect(cells[0].ariaLabel).toBe('August 2026: in €2,950.00, out €2,104.80');
    expect(cells[2].inProgress).toBe(true);
  });

  it('scales the bars to the tallest month shown, clamping earlier ones', () => {
    const months = [
      { key: '2025-11', income: 19000, expenses: 12000 },
      { key: '2025-12', income: 4000, expenses: 1000 },
      { key: '2026-01', income: 2000, expenses: 3000 },
    ];

    const cells = scrubberCells(months, '2026-10', 1);

    expect(cells[0].inShare).toBe(1);
    expect(cells[0].outShare).toBe(1);
    expect(cells[1].inShare).toBe(1);
    expect(cells[1].outShare).toBe(0.25);
    expect(cells[2].outShare).toBe(0.75);
  });
});

describe('monthsThatFit', () => {
  it('shows six months on a wide screen and fewer on a narrow desktop', () => {
    expect(monthsThatFit(1376, true)).toBe(6);
    expect(monthsThatFit(1036, true)).toBe(5);
    expect(monthsThatFit(1036, false)).toBe(6);
    expect(monthsThatFit(500, true)).toBe(3);
  });
});

describe('MonthScrubberComponent', () => {
  let fixture: ComponentFixture<MonthScrubberComponent>;
  const months = Array.from({ length: 9 }, (_, i) => ({
    key: `2025-0${i + 1}`,
    income: 10,
    expenses: 5,
  }));

  beforeEach(() => {
    fixture = TestBed.createComponent(MonthScrubberComponent);
    fixture.componentRef.setInput('months', months);
    fixture.componentRef.setInput('selected', '2025-09');
    fixture.detectChanges();
  });

  it('shows the newest six months until Earlier is pressed', () => {
    expect(fixture.componentInstance.firstShown()).toBe(3);
    fixture.componentInstance.showEarlier();
    expect(fixture.componentInstance.firstShown()).toBe(0);
  });

  it('always shows the selected month', () => {
    fixture.componentRef.setInput('selected', '2025-02');
    expect(fixture.componentInstance.firstShown()).toBe(1);
  });

  it('marks the selected month pressed and emits a new choice', () => {
    const el: HTMLElement = fixture.nativeElement;
    const pressed = el.querySelectorAll('.month[aria-pressed="true"]');
    expect(pressed).toHaveLength(1);

    let picked = '';
    fixture.componentInstance.selectedChange.subscribe((m) => (picked = m));
    (el.querySelectorAll('.month')[0] as HTMLButtonElement).click();
    expect(picked).toBe('2025-01');
  });

  it('offers All months when allowed', () => {
    fixture.componentRef.setInput('allowAll', true);
    fixture.detectChanges();
    const all = fixture.nativeElement.querySelector('.month--all') as HTMLButtonElement;
    expect(all).not.toBeNull();

    let picked: string | null = null;
    fixture.componentInstance.selectedChange.subscribe((m) => (picked = m));
    all.click();
    expect(picked).toBe('');
  });
});
