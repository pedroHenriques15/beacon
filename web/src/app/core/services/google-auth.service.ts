import { Injectable, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { tap } from 'rxjs';
import { GoogleAuthStatus } from '../models/google-auth-status';

@Injectable({ providedIn: 'root' })
export class GoogleAuthService {
  private http = inject(HttpClient);

  status = signal<GoogleAuthStatus | null>(null);

  constructor() {
    this.loadStatus();
  }

  loadStatus(): void {
    this.http
      .get<GoogleAuthStatus>('/api/auth/google/status')
      .subscribe((s) => this.status.set(s));
  }

  connect(): void {
    this.http.get<{ url: string }>('/api/auth/google/login').subscribe(({ url }) => {
      window.location.href = url;
    });
  }

  disconnect(): void {
    this.http
      .delete('/api/auth/google')
      .pipe(tap(() => this.loadStatus()))
      .subscribe();
  }
}
