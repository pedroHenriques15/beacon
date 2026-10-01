import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { tap } from 'rxjs';
import { GoogleAuthService } from '../services/google-auth.service';

/** The codes Calendar and Tasks endpoints send when the Google account must be connected (again). */
const CONNECT_AGAIN_CODES = ['google_reconnect_required', 'google_not_connected'];

/**
 * When a Calendar or Tasks call finds the Google connection unusable, the Google status is
 * reloaded, so the Calendar page and Settings offer to reconnect instead of an empty calendar.
 */
export const googleConnectionInterceptor: HttpInterceptorFn = (req, next) => {
  if (!req.url.startsWith('/api/calendar') && !req.url.startsWith('/api/tasks')) return next(req);
  const googleAuth = inject(GoogleAuthService);
  return next(req).pipe(
    tap({
      error: (err: unknown) => {
        if (err instanceof HttpErrorResponse && CONNECT_AGAIN_CODES.includes(err.error?.code)) {
          googleAuth.connectionRejected();
        }
      },
    }),
  );
};
