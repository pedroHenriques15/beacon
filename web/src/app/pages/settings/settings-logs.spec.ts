import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import { TestBed, ComponentFixture } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { SettingsLogsComponent } from './settings-logs';
import { LogEntry, LogsPage } from '../../core/models/log-entry';

function entry(overrides: Partial<LogEntry>): LogEntry {
  return {
    timestamp: '2026-10-01T09:00:00Z',
    level: 'Information',
    message: 'HTTP GET /api/transactions responded 200 in 12 ms',
    source: 'Beacon.Api.Middleware.RequestLoggingMiddleware',
    details: null,
    ...overrides,
  };
}

describe('SettingsLogsComponent', () => {
  let fixture: ComponentFixture<SettingsLogsComponent>;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [SettingsLogsComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(SettingsLogsComponent);
    fixture.detectChanges();
  });

  afterEach(() => http.verify());

  function answer(page: LogsPage): void {
    http.expectOne((r) => r.url === '/api/logs').flush(page);
    fixture.detectChanges();
  }

  function text(): string {
    return fixture.nativeElement.textContent;
  }

  function levelButtons(): HTMLButtonElement[] {
    return Array.from(
      fixture.nativeElement.querySelectorAll('[aria-label="Level"] button'),
    ) as HTMLButtonElement[];
  }

  function levelButton(label: string): HTMLButtonElement {
    const match = levelButtons().find((b) => b.textContent?.trim() === label);
    if (!match) throw new Error(`No "${label}" level.`);
    return match;
  }

  it('asks for the last 24 hours at every level when it opens', () => {
    const req = http.expectOne((r) => r.url === '/api/logs');
    expect(req.request.params.get('minLevel')).toBe('Information');
    expect(req.request.params.get('limit')).toBe('200');
    const from = Date.parse(req.request.params.get('from')!);
    expect(Date.now() - from).toBeGreaterThan(23.9 * 3_600_000);
    expect(Date.now() - from).toBeLessThan(24.1 * 3_600_000);
    req.flush({ enabled: true, entries: [], more: false });
  });

  it('shows the level and the window it asked for in its filters', () => {
    answer({ enabled: true, entries: [], more: false });

    const pressed = levelButtons().filter((b) => b.getAttribute('aria-pressed') === 'true');
    expect(pressed).toHaveLength(1);
    expect(pressed[0].textContent?.trim()).toBe('All levels');
    const timeWindow: HTMLSelectElement = fixture.nativeElement.querySelector('select');
    expect(timeWindow.selectedOptions[0].textContent?.trim()).toBe('Last 24 hours');
  });

  it('lists entries newest first, with errors highlighted and their details', () => {
    answer({
      enabled: true,
      more: false,
      entries: [
        entry({
          level: 'Error',
          message: 'Client error on /investments: boom',
          source: 'Beacon.Client',
          details: 'TypeError: boom\n    at toggleExpand',
        }),
        entry({}),
      ],
    });

    const items = fixture.nativeElement.querySelectorAll('.log-entry');
    expect(items).toHaveLength(2);
    expect(items[0].classList).toContain('log-entry--error');
    expect(items[0].textContent).toContain('Client error on /investments: boom');
    expect(items[0].textContent).toContain('Client');
    expect(items[0].querySelector('details pre').textContent).toContain('at toggleExpand');
    expect(items[1].classList).not.toContain('log-entry--error');
    expect(items[1].textContent).toContain('RequestLoggingMiddleware');
  });

  it('asks again when the level or the search changes', () => {
    answer({ enabled: true, entries: [], more: false });

    levelButton('Errors only').click();
    const byLevel = http.expectOne((r) => r.url === '/api/logs');
    expect(byLevel.request.params.get('minLevel')).toBe('Error');
    byLevel.flush({ enabled: true, entries: [], more: false });
    fixture.detectChanges();
    expect(levelButton('Errors only').getAttribute('aria-pressed')).toBe('true');
    expect(levelButton('All levels').getAttribute('aria-pressed')).toBe('false');

    const search: HTMLInputElement = fixture.nativeElement.querySelector('input');
    search.value = 'google';
    search.dispatchEvent(new KeyboardEvent('keyup', { key: 'Enter' }));
    const bySearch = http.expectOne((r) => r.url === '/api/logs');
    expect(bySearch.request.params.get('search')).toBe('google');
    expect(bySearch.request.params.get('minLevel')).toBe('Error');
    bySearch.flush({ enabled: true, entries: [], more: false });
  });

  it('says when the server writes no log files', () => {
    answer({ enabled: false, entries: [], more: false });

    expect(text()).toContain('This server writes no log files');
  });

  it('says when older entries were left out', () => {
    answer({ enabled: true, entries: [entry({})], more: true });

    expect(text()).toContain('Showing the newest 200 entries');
  });
});
