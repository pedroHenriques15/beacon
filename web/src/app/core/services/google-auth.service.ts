import { Injectable, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { tap } from 'rxjs';
import { GoogleAuthStatus } from '../models/google-auth-status';

@Injectable({ providedIn: 'root' })
export class GoogleAuthService {
  private http = inject(HttpClient);

  /** Null until the first `loadStatus()`; the pages that show it load it when they open. */
  status = signal<GoogleAuthStatus | null>(null);

  /** The API refreshes an expired token before answering, so a rejected one shows here. */
  loadStatus(): void {
    this.http.get<GoogleAuthStatus>('/api/auth/google/status').subscribe({
      next: (s) => this.status.set(s),
      error: () =>
        this.status.set({
          state: 'notConnected',
          connected: false,
          expiresAt: null,
          connectedAt: null,
        }),
    });
  }

  /**
   * A Calendar or Tasks call answered that the account must be connected (again). If the
   * status still says connected, reload it so the pages offer to reconnect.
   */
  connectionRejected(): void {
    if (this.status()?.connected) this.loadStatus();
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
