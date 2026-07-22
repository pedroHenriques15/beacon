import { Component, EventEmitter, Input, OnChanges, Output, SimpleChanges } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { CalendarEvent, CalendarEventFormData } from '../../core/models/calendar-event';
import { GOOGLE_CALENDAR_COLOR_ENTRIES } from '../../core/constants/calendar-colors';

@Component({
  selector: 'app-event-modal',
  standalone: true,
  imports: [FormsModule],
  templateUrl: './event-modal.html',
  styleUrl: './_calendar-modal.scss',
})
export class EventModalComponent implements OnChanges {
  @Input() open = false;
  @Input() event: CalendarEvent | null = null;
  @Input() prefilledDate = '';
  @Input() saving = false;
  @Input() saveError: string | null = null;

  @Output() save = new EventEmitter<CalendarEventFormData>();
  @Output() delete = new EventEmitter<string>();
  @Output() cancel = new EventEmitter<void>();

  form: CalendarEventFormData = {
    title: '',
    start: '',
    end: '',
    description: '',
    location: '',
    isAllDay: true,
  };

  readonly COLOR_ENTRIES = GOOGLE_CALENDAR_COLOR_ENTRIES;

  timeError = '';

  ngOnChanges(changes: SimpleChanges): void {
    if (!this.open) return;
    if (!('open' in changes || 'event' in changes || 'prefilledDate' in changes)) return;
    this.timeError = '';

    if (this.event) {
      this.form = {
        title: this.event.title,
        start: this.event.isAllDay ? this.event.start : toDatetimeLocal(this.event.start),
        end: this.event.isAllDay ? this.event.end : toDatetimeLocal(this.event.end),
        description: this.event.description ?? '',
        location: this.event.location ?? '',
        isAllDay: this.event.isAllDay,
        colorId: this.event.colorId,
      };
    } else {
      const date = this.prefilledDate || toDateStr(new Date());
      this.form = {
        title: '',
        start: date,
        end: date,
        description: '',
        location: '',
        isAllDay: true,
        colorId: undefined,
      };
    }
  }

  onAllDayToggle(): void {
    if (this.form.isAllDay) {
      const date = this.form.start.substring(0, 10);
      this.form.start = `${date}T09:00`;
      this.form.end = `${date}T10:00`;
    } else {
      const date = this.form.start.substring(0, 10);
      this.form.start = date;
      this.form.end = date;
    }
    this.form.isAllDay = !this.form.isAllDay;
  }

  onSave(): void {
    if (!this.form.title.trim()) return;
    if (!this.form.isAllDay && new Date(this.form.start) >= new Date(this.form.end)) {
      this.timeError = 'End time must be after start time.';
      return;
    }
    this.timeError = '';
    this.save.emit({
      ...this.form,
      title: this.form.title.trim(),
      start: this.form.isAllDay ? this.form.start : new Date(this.form.start).toISOString(),
      end: this.form.isAllDay ? this.form.end : new Date(this.form.end).toISOString(),
      description: this.form.description || undefined,
      location: this.form.location || undefined,
    });
  }

  onDelete(): void {
    if (this.event) this.delete.emit(this.event.id);
  }

  onCancel(): void {
    this.cancel.emit();
  }
}

function toDatetimeLocal(isoString: string): string {
  const dt = new Date(isoString);
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${dt.getFullYear()}-${pad(dt.getMonth() + 1)}-${pad(dt.getDate())}T${pad(dt.getHours())}:${pad(dt.getMinutes())}`;
}

function toDateStr(date: Date): string {
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}
