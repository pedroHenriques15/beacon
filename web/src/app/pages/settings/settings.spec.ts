import { describe, it, expect, beforeEach, vi } from 'vitest';
import { TestBed, ComponentFixture } from '@angular/core/testing';
import { signal } from '@angular/core';
import { provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { SettingsComponent } from './settings';
import { GoogleAuthService } from '../../core/services/google-auth.service';
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

describe('SettingsComponent Google account', () => {
  let fixture: ComponentFixture<SettingsComponent>;
  const status = signal<GoogleAuthStatus | null>(null);
  const googleAuth = { status, loadStatus: vi.fn(), connect: vi.fn(), disconnect: vi.fn() };

  beforeEach(() => {
    status.set(null);
    googleAuth.loadStatus.mockClear();
    googleAuth.connect.mockClear();
    googleAuth.disconnect.mockClear();

    TestBed.configureTestingModule({
      imports: [SettingsComponent],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: GoogleAuthService, useValue: googleAuth },
      ],
    });

    fixture = TestBed.createComponent(SettingsComponent);
    fixture.detectChanges();
  });

  function card(): HTMLElement {
    return fixture.nativeElement.querySelector('.google-card');
  }

  function button(label: string): HTMLButtonElement {
    const buttons = Array.from(card().querySelectorAll('button')) as HTMLButtonElement[];
    const match = buttons.find((b) => b.textContent?.trim() === label);
    if (!match) throw new Error(`No "${label}" button in the Google card.`);
    return match;
  }

  function render(s: GoogleAuthStatus): void {
    status.set(s);
    fixture.detectChanges();
  }

  it('checks the Google connection when it opens', () => {
    expect(googleAuth.loadStatus).toHaveBeenCalledTimes(1);
  });

  it('says the connection expired and offers Reconnect and Disconnect', () => {
    render(makeStatus({ state: 'reconnectRequired', connected: false }));

    expect(card().textContent).toContain('Connection expired');
    expect(card().textContent).not.toContain('Connected');

    button('Reconnect').click();
    expect(googleAuth.connect).toHaveBeenCalledTimes(1);
    button('Disconnect').click();
    expect(googleAuth.disconnect).toHaveBeenCalledTimes(1);
  });

  it('stays connected, with a warning, while Google is not responding', () => {
    render(makeStatus({ state: 'unreachable' }));

    expect(card().textContent).toContain('Connected');
    expect(card().textContent).toContain('Google is not responding right now');
  });

  it('shows no warning while the connection works', () => {
    render(makeStatus({ state: 'connected' }));

    expect(card().textContent).toContain('Connected');
    expect(card().textContent).not.toContain('not responding');
  });

  it('offers to connect when no account is connected', () => {
    render(makeStatus({ state: 'notConnected', connected: false, connectedAt: null }));

    expect(card().textContent).toContain('Not connected');
    button('Connect Google').click();
    expect(googleAuth.connect).toHaveBeenCalledTimes(1);
  });
});
