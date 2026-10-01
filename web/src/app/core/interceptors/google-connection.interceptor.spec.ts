import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { googleConnectionInterceptor } from './google-connection.interceptor';
import { GoogleAuthService } from '../services/google-auth.service';
import { GoogleAuthStatus } from '../models/google-auth-status';

const CONNECTED: GoogleAuthStatus = {
  state: 'connected',
  connected: true,
  expiresAt: '2026-09-30T11:00:00Z',
  connectedAt: '2026-09-01T10:00:00Z',
};
const EXPIRED: GoogleAuthStatus = {
  state: 'reconnectRequired',
  connected: false,
  expiresAt: '2026-09-22T11:00:00Z',
  connectedAt: '2026-09-01T10:00:00Z',
};
const UNAUTHORIZED = { status: 401, statusText: 'Unauthorized' };

describe('googleConnectionInterceptor', () => {
  let http: HttpClient;
  let controller: HttpTestingController;
  let googleAuth: GoogleAuthService;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([googleConnectionInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    http = TestBed.inject(HttpClient);
    controller = TestBed.inject(HttpTestingController);
    googleAuth = TestBed.inject(GoogleAuthService);
    googleAuth.status.set(CONNECTED);
  });

  afterEach(() => controller.verify());

  it('reloads the status when a calendar call answers google_reconnect_required', () => {
    http.get('/api/calendar/events').subscribe({ error: () => {} });
    controller
      .expectOne('/api/calendar/events')
      .flush({ code: 'google_reconnect_required', error: 'expired' }, UNAUTHORIZED);

    controller.expectOne('/api/auth/google/status').flush(EXPIRED);
    expect(googleAuth.status()?.state).toBe('reconnectRequired');
  });

  it('reloads the status when a tasks call answers google_not_connected', () => {
    http.get('/api/tasks/lists').subscribe({ error: () => {} });
    controller
      .expectOne('/api/tasks/lists')
      .flush({ code: 'google_not_connected', error: 'not connected' }, UNAUTHORIZED);

    controller.expectOne('/api/auth/google/status').flush({ ...EXPIRED, state: 'notConnected' });
    expect(googleAuth.status()?.connected).toBe(false);
  });

  it('still lets the caller see the error', () => {
    let status = 0;
    http.get('/api/calendar/events').subscribe({ error: (err) => (status = err.status) });
    controller
      .expectOne('/api/calendar/events')
      .flush({ code: 'google_reconnect_required', error: 'expired' }, UNAUTHORIZED);
    controller.expectOne('/api/auth/google/status').flush(EXPIRED);

    expect(status).toBe(401);
  });

  it('leaves the status alone when Google is only unreachable', () => {
    http.get('/api/calendar/events').subscribe({ error: () => {} });
    controller
      .expectOne('/api/calendar/events')
      .flush(
        { code: 'google_unreachable', error: 'no answer' },
        { status: 503, statusText: 'Service Unavailable' },
      );

    controller.expectNone('/api/auth/google/status');
    expect(googleAuth.status()).toEqual(CONNECTED);
  });

  it('does not reload a status that already asks to reconnect', () => {
    googleAuth.status.set(EXPIRED);
    http.get('/api/calendar/events').subscribe({ error: () => {} });
    controller
      .expectOne('/api/calendar/events')
      .flush({ code: 'google_reconnect_required', error: 'expired' }, UNAUTHORIZED);

    controller.expectNone('/api/auth/google/status');
  });

  it('ignores calls outside Calendar and Tasks', () => {
    http.get('/api/statements').subscribe({ error: () => {} });
    controller
      .expectOne('/api/statements')
      .flush({ code: 'google_reconnect_required', error: 'expired' }, UNAUTHORIZED);

    controller.expectNone('/api/auth/google/status');
  });
});
