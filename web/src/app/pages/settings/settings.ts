import { Component, DestroyRef, inject, OnInit, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute } from '@angular/router';
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
}
