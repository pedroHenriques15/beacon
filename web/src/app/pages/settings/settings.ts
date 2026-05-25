import { Component, inject, OnInit } from '@angular/core';
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
  private route = inject(ActivatedRoute);

  ngOnInit(): void {
    this.route.queryParams.subscribe((params) => {
      if (params['google'] === 'connected' || params['google'] === 'error') {
        this.googleAuth.loadStatus();
      }
    });
  }

  connectGoogle(): void {
    this.googleAuth.connect();
  }

  disconnectGoogle(): void {
    this.googleAuth.disconnect();
  }
}
