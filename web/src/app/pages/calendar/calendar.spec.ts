import { describe, it, expect, beforeEach, vi } from 'vitest';
import { TestBed, ComponentFixture } from '@angular/core/testing';
import { signal } from '@angular/core';
import { provideRouter } from '@angular/router';
import { CalendarPage } from './calendar';
import { CalendarService } from '../../core/services/calendar.service';
import { GoogleAuthService } from '../../core/services/google-auth.service';
import { TasksService } from '../../core/services/tasks.service';
import { GoogleAuthStatus } from '../../core/models/google-auth-status';

function makeStatus(overrides: Partial<GoogleAuthStatus>): GoogleAuthStatus {
  return {
    state: 'connected',
    connected: true,
    expiresAt: '2026-09-30T11:00:00Z',
    connectedAt: '2026-09-01T10:00:00Z',
    ...overrides,
  };
}

describe('CalendarPage Google connection', () => {
  let fixture: ComponentFixture<CalendarPage>;
  const status = signal<GoogleAuthStatus | null>(null);
  const googleAuth = { status, loadStatus: vi.fn(), connect: vi.fn() };

  beforeEach(() => {
    status.set(null);
    googleAuth.loadStatus.mockClear();
    googleAuth.connect.mockClear();

    TestBed.configureTestingModule({
      imports: [CalendarPage],
      providers: [
        provideRouter([]),
        { provide: GoogleAuthService, useValue: googleAuth },
        {
          provide: CalendarService,
          useValue: {
            events: signal([]),
            loading: signal(false),
            error: signal(null),
            calendarList: signal([]),
            loadEvents: vi.fn(),
          },
        },
        {
          provide: TasksService,
          useValue: {
            taskLists: signal([]),
            tasks: signal([]),
            loading: signal(false),
            error: signal(null),
            loadTaskLists: vi.fn(),
            loadAllTasks: vi.fn(),
          },
        },
      ],
    });

    fixture = TestBed.createComponent(CalendarPage);
    fixture.detectChanges();
  });

  function text(): string {
    return (fixture.nativeElement as HTMLElement).textContent ?? '';
  }

  function render(s: GoogleAuthStatus): void {
    status.set(s);
    fixture.detectChanges();
  }

  it('checks the Google connection when it opens', () => {
    expect(googleAuth.loadStatus).toHaveBeenCalledTimes(1);
  });

  it('offers to reconnect, instead of an empty calendar, when Google rejected the token', () => {
    render(makeStatus({ state: 'reconnectRequired', connected: false }));

    expect(text()).toContain('Your Google connection has expired');
    expect(fixture.nativeElement.querySelector('.cal-grid')).toBeNull();

    const button = fixture.nativeElement.querySelector('.cal-not-connected button');
    expect(button.textContent).toContain('Reconnect Google');
    button.click();
    expect(googleAuth.connect).toHaveBeenCalledTimes(1);
  });

  it('sends an account that was never connected to Settings', () => {
    render(makeStatus({ state: 'notConnected', connected: false, connectedAt: null }));

    expect(text()).toContain('Connect your Google account to use the calendar.');
    expect(text()).not.toContain('expired');
    expect(fixture.nativeElement.querySelector('.cal-not-connected a').getAttribute('href')).toBe(
      '/settings',
    );
  });

  it('shows the calendar while connected, and while Google is only unreachable', () => {
    render(makeStatus({ state: 'connected' }));
    expect(fixture.nativeElement.querySelector('.cal-grid')).not.toBeNull();

    render(makeStatus({ state: 'unreachable' }));
    expect(fixture.nativeElement.querySelector('.cal-grid')).not.toBeNull();
  });
});
