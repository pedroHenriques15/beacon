import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { ErrorHandler } from '@angular/core';
import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ClientErrorHandler } from './client-error-handler';

describe('ClientErrorHandler', () => {
  let handler: ErrorHandler;
  let http: HttpTestingController;

  beforeEach(() => {
    vi.spyOn(console, 'error').mockImplementation(() => undefined);
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: ErrorHandler, useClass: ClientErrorHandler },
      ],
    });
    handler = TestBed.inject(ErrorHandler);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
    vi.restoreAllMocks();
  });

  it('logs to the console and posts each error once, with its stack and route', () => {
    const error = new TypeError("Cannot read properties of undefined (reading 'id')");

    handler.handleError(error);

    expect(console.error).toHaveBeenCalled();
    const req = http.expectOne('/api/logs/client-errors');
    expect(req.request.method).toBe('POST');
    expect(req.request.body.message).toBe("Cannot read properties of undefined (reading 'id')");
    expect(req.request.body.stack).toContain('TypeError');
    expect(req.request.body.route).toBe(window.location.pathname);
    req.flush(null, { status: 204, statusText: 'No Content' });
  });

  it('posts one report per error', () => {
    handler.handleError(new Error('first'));
    handler.handleError(new Error('second'));

    const reqs = http.match('/api/logs/client-errors');
    expect(reqs.map((r) => r.request.body.message)).toEqual(['first', 'second']);
    reqs.forEach((r) => r.flush(null));
  });

  it('leaves query strings out of the URLs it reports', () => {
    handler.handleError(
      new HttpErrorResponse({ url: '/api/transactions?search=pharmacy', status: 500 }),
    );

    const req = http.expectOne('/api/logs/client-errors');
    expect(req.request.body.message).toContain('/api/transactions');
    expect(req.request.body.message).not.toContain('pharmacy');
    req.flush(null);
  });

  it('drops a report that fails instead of reporting it again', () => {
    handler.handleError(new Error('broken'));

    http
      .expectOne('/api/logs/client-errors')
      .flush('Too many', { status: 429, statusText: 'Too Many Requests' });
    http.expectNone('/api/logs/client-errors');
  });

  it('sends at most ten reports a minute', () => {
    for (let i = 0; i < 15; i++) handler.handleError(new Error(`loop ${i}`));

    const reqs = http.match('/api/logs/client-errors');
    expect(reqs).toHaveLength(ClientErrorHandler.MAX_REPORTS_PER_MINUTE);
    reqs.forEach((r) => r.flush(null));
  });
});
