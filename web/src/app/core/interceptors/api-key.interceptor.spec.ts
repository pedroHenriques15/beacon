import { describe, it, expect, beforeEach } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { apiKeyInterceptor } from './api-key.interceptor';
import { environment } from '../../../environments/environment';

const EXPECTED_API_KEY = environment.apiKey;

describe('apiKeyInterceptor', () => {
  let http: HttpClient;
  let controller: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([apiKeyInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    http = TestBed.inject(HttpClient);
    controller = TestBed.inject(HttpTestingController);
  });

  it('adds X-Api-Key header to /api requests', () => {
    http.get('/api/statements').subscribe();

    const req = controller.expectOne('/api/statements');
    expect(req.request.headers.get('X-Api-Key')).toBe(EXPECTED_API_KEY);
    req.flush([]);
  });

  it('does not add X-Api-Key to non-api external requests', () => {
    http.get('https://example.com/data').subscribe();

    const req = controller.expectOne('https://example.com/data');
    expect(req.request.headers.has('X-Api-Key')).toBe(false);
    req.flush({});
  });

  it('does not add X-Api-Key to relative non-api paths', () => {
    http.get('/assets/logo.png').subscribe();

    const req = controller.expectOne('/assets/logo.png');
    expect(req.request.headers.has('X-Api-Key')).toBe(false);
    req.flush(null);
  });

  it('adds header to all /api sub-paths', () => {
    const paths = [
      '/api/categories',
      '/api/transactions',
      '/api/statements/upload',
      '/api/categories/rules',
    ];

    for (const path of paths) {
      http.get(path).subscribe();
      const req = controller.expectOne(path);
      expect(req.request.headers.get('X-Api-Key')).toBe(EXPECTED_API_KEY);
      req.flush([]);
    }
  });

  it('passes through requests without modifying the URL', () => {
    http.get('/api/test').subscribe();

    const req = controller.expectOne('/api/test');
    expect(req.request.url).toBe('/api/test');
    req.flush(null);
  });

  it('X-Api-Key value matches the environment apiKey', () => {
    http.get('/api/backup').subscribe();

    const req = controller.expectOne('/api/backup');
    const header = req.request.headers.get('X-Api-Key');
    expect(header).not.toBeNull();
    expect(header).toBe(environment.apiKey);
    req.flush({});
  });
});
