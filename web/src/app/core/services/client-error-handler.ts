import { ErrorHandler, Injectable, Injector, inject } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { LogsService } from './logs.service';

/** Caps that match the API's, so a report is never cut twice. */
const MAX_MESSAGE = 1000;
const MAX_STACK = 8000;

/**
 * Logs uncaught errors to the console as Angular does, and reports each one to the API, which
 * writes it to the server's log at Error. At most MAX_REPORTS_PER_MINUTE are sent, so a page
 * stuck in an error loop can't flood the logs; a report that fails is dropped, never reported.
 */
@Injectable()
export class ClientErrorHandler extends ErrorHandler {
  static readonly MAX_REPORTS_PER_MINUTE = 10;

  // Resolved on first use: HttpClient's interceptors must not be built while the app starts.
  private injector = inject(Injector);
  private sentAt: number[] = [];

  override handleError(error: unknown): void {
    super.handleError(error);
    this.report(error);
  }

  private report(error: unknown): void {
    const now = Date.now();
    this.sentAt = this.sentAt.filter((t) => now - t < 60_000);
    if (this.sentAt.length >= ClientErrorHandler.MAX_REPORTS_PER_MINUTE) return;
    this.sentAt.push(now);

    const { message, stack } = describe(error);
    try {
      this.injector
        .get(LogsService)
        .reportClientError({
          message: withoutQueryStrings(message).slice(0, MAX_MESSAGE),
          stack: stack ? withoutQueryStrings(stack).slice(0, MAX_STACK) : null,
          route: window.location.pathname,
        })
        .subscribe({ error: () => undefined });
    } catch {
      // Reporting must never throw from the error handler.
    }
  }
}

function describe(error: unknown): { message: string; stack: string | null } {
  if (error instanceof HttpErrorResponse) return { message: error.message, stack: null };
  if (error instanceof Error)
    return { message: error.message || error.name, stack: error.stack ?? null };
  if (typeof error === 'string') return { message: error, stack: null };
  try {
    return { message: JSON.stringify(error) ?? String(error), stack: null };
  } catch {
    return { message: String(error), stack: null };
  }
}

/** URLs in a message keep their path: a query string can carry search terms. */
function withoutQueryStrings(text: string): string {
  return text.replace(/((?:https?:\/\/|\/api\/)[^\s?#]*)[?#]\S*/g, '$1');
}
