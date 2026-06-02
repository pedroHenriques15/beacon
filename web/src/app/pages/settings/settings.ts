import { Component, DestroyRef, inject, OnInit, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { GoogleAuthService } from '../../core/services/google-auth.service';
import { DatePipe } from '@angular/common';

@Component({
  selector: 'app-settings',
  standalone: true,
  imports: [DatePipe],
  templateUrl: './settings.html',
  styleUrl: './settings.scss',
})
export class SettingsComponent implements OnInit {
  googleAuth = inject(GoogleAuthService);
  errorMessage = signal<string | null>(null);
  private route = inject(ActivatedRoute);
  private destroyRef = inject(DestroyRef);
  private http = inject(HttpClient);

  backupState = signal<'idle' | 'running' | 'done' | 'error'>('idle');
  backupMessage = signal('');
  restoreState = signal<'idle' | 'running' | 'done' | 'error'>('idle');
  restoreMessage = signal('');

  ngOnInit(): void {
    this.route.queryParams.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((params) => {
      if (params['google'] === 'connected' || params['google'] === 'error') {
        this.googleAuth.loadStatus();
      }
      if (params['google'] === 'error') {
        this.errorMessage.set('Could not connect to Google. Please try again.');
      }
    });
  }

  connectGoogle(): void {
    this.errorMessage.set(null);
    this.googleAuth.connect();
  }

  disconnectGoogle(): void {
    this.googleAuth.disconnect();
  }

  createBackup(): void {
    this.backupState.set('running');
    this.backupMessage.set('');
    this.http.post<{ message: string; path: string }>('/api/backup', {}).subscribe({
      next: (body) => {
        this.backupState.set('done');
        this.backupMessage.set(body.path ?? 'Backup created.');
      },
      error: (err) => {
        this.backupState.set('error');
        this.backupMessage.set(err.error?.message ?? err.error ?? 'Backup failed.');
      },
    });
  }

  restoreBackup(): void {
    this.restoreState.set('running');
    this.restoreMessage.set('');
    this.http.post<{ message: string }>('/api/backup/restore', {}).subscribe({
      next: (body) => {
        this.restoreState.set('done');
        this.restoreMessage.set(body.message);
      },
      error: (err) => {
        this.restoreState.set('error');
        this.restoreMessage.set(err.error?.message ?? 'Restore failed.');
      },
    });
  }
}
