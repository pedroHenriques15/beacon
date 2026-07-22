import { Injectable, inject, signal } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable, Subject } from 'rxjs';
import { takeUntil } from 'rxjs/operators';
import { CalendarEvent, CalendarEventFormData } from '../models/calendar-event';

export interface CalendarInfo {
  id: string;
  name: string;
  color: string;
}

const CALENDAR_PALETTE = [
  '#6c63ff',
  '#e67c73',
  '#33b679',
  '#f6bf26',
  '#039be5',
  '#8e24aa',
  '#f4511e',
  '#0b8043',
];

@Injectable({ providedIn: 'root' })
export class CalendarService {
  private http = inject(HttpClient);
  private loadCancel$ = new Subject<void>();
  private calendarMeta = new Map<string, CalendarInfo>();

  events = signal<CalendarEvent[]>([]);
  loading = signal(false);
  error = signal<string | null>(null);
  calendarList = signal<CalendarInfo[]>([]);

  loadEvents(year: number, month: number): void {
    const start = new Date(year, month, 1);
    const end = new Date(year, month + 1, 0, 23, 59, 59);
    const params = new HttpParams().set('start', start.toISOString()).set('end', end.toISOString());

    this.loadCancel$.next();
    this.loading.set(true);
    this.error.set(null);

    this.http
      .get<CalendarEvent[]>('/api/calendar/events', { params })
      .pipe(takeUntil(this.loadCancel$))
      .subscribe({
        next: (events) => {
          this.events.set(events);
          let changed = false;
          for (const e of events) {
            if (!this.calendarMeta.has(e.calendarId)) {
              this.calendarMeta.set(e.calendarId, {
                id: e.calendarId,
                name: e.calendarName ?? e.calendarId,
                color:
                  e.calendarColor ??
                  CALENDAR_PALETTE[this.calendarMeta.size % CALENDAR_PALETTE.length],
              });
              changed = true;
            }
          }
          if (changed) {
            this.calendarList.set(
              [...this.calendarMeta.values()].sort((a, b) => a.name.localeCompare(b.name)),
            );
          }
          this.loading.set(false);
        },
        error: () => {
          this.error.set('Failed to load events.');
          this.loading.set(false);
        },
      });
  }

  createEvent(data: CalendarEventFormData): Observable<CalendarEvent> {
    return this.http.post<CalendarEvent>('/api/calendar/events', data);
  }

  updateEvent(
    id: string,
    calendarId: string,
    data: CalendarEventFormData,
  ): Observable<CalendarEvent> {
    return this.http.put<CalendarEvent>(`/api/calendar/events/${id}`, { ...data, calendarId });
  }

  deleteEvent(id: string, calendarId: string): Observable<void> {
    return this.http.delete<void>(`/api/calendar/events/${id}`, {
      params: { calendarId },
    });
  }
}
