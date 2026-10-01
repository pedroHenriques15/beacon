import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { LogEntry, LogLevel, LogsPage } from '../../core/models/log-entry';
import { LogsService } from '../../core/services/logs.service';

/** Entries shown at a time; the API answers with `more` when older ones matched. */
const LIMIT = 200;

/** The server's recent log entries, newest first, with a level filter, a time window and a search. */
@Component({
  selector: 'app-settings-logs',
  standalone: true,
  imports: [DatePipe],
  templateUrl: './settings-logs.html',
  styleUrl: './settings-logs.scss',
  changeDetection: ChangeDetectionStrategy.Eager,
})
export class SettingsLogsComponent implements OnInit {
  private logs = inject(LogsService);

  readonly limit = LIMIT;
  readonly levels: { value: LogLevel; label: string }[] = [
    { value: 'Information', label: 'All levels' },
    { value: 'Warning', label: 'Warnings and errors' },
    { value: 'Error', label: 'Errors only' },
  ];
  readonly windows: { hours: number | null; label: string }[] = [
    { hours: 1, label: 'Last hour' },
    { hours: 24, label: 'Last 24 hours' },
    { hours: 24 * 7, label: 'Last 7 days' },
    { hours: null, label: 'Everything kept' },
  ];

  minLevel = signal<LogLevel>('Information');
  windowHours = signal<number | null>(24);
  search = signal('');

  page = signal<LogsPage | null>(null);
  loading = signal(false);
  error = signal<string | null>(null);

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    const hours = this.windowHours();
    this.loading.set(true);
    this.error.set(null);
    this.logs
      .getLogs({
        minLevel: this.minLevel(),
        from: hours == null ? null : new Date(Date.now() - hours * 3_600_000).toISOString(),
        search: this.search(),
        limit: LIMIT,
      })
      .subscribe({
        next: (page) => {
          this.page.set(page);
          this.loading.set(false);
        },
        error: () => {
          this.error.set('Could not load the logs.');
          this.loading.set(false);
        },
      });
  }

  setLevel(value: string): void {
    this.minLevel.set(value as LogLevel);
    this.load();
  }

  setWindow(value: string): void {
    this.windowHours.set(value === '' ? null : Number(value));
    this.load();
  }

  setSearch(value: string): void {
    this.search.set(value);
    this.load();
  }

  levelClass(entry: LogEntry): string {
    switch (entry.level) {
      case 'Fatal':
      case 'Error':
        return 'log-entry--error';
      case 'Warning':
        return 'log-entry--warning';
      default:
        return '';
    }
  }

  /** The class name without its namespace: RequestLoggingMiddleware, Client. */
  shortSource(entry: LogEntry): string {
    return entry.source?.split('.').pop() ?? '';
  }
}
