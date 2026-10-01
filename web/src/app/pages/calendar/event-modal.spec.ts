import { describe, it, expect } from 'vitest';
import { SimpleChange } from '@angular/core';
import { EventModalComponent } from './event-modal';
import { CalendarEvent } from '../../core/models/calendar-event';

const event: CalendarEvent = {
  id: 'e1',
  calendarId: 'primary',
  title: 'Dentist',
  start: '2026-07-01',
  end: '2026-07-02',
  isAllDay: true,
} as CalendarEvent;

describe('EventModalComponent', () => {
  it('initialises the form when the modal opens', () => {
    const c = new EventModalComponent();
    c.open = true;
    c.event = event;
    c.ngOnChanges({ open: new SimpleChange(false, true, true) });

    expect(c.form.title).toBe('Dentist');
  });

  it('does NOT wipe in-progress edits when only saving/saveError change', () => {
    const c = new EventModalComponent();
    c.open = true;
    c.event = event;
    c.ngOnChanges({ open: new SimpleChange(false, true, true) });

    c.form.title = 'Dentist (rescheduled)';
    c.saving = false;
    c.saveError = 'Failed to save event. Please try again.';
    c.ngOnChanges({
      saving: new SimpleChange(true, false, false),
      saveError: new SimpleChange(null, c.saveError, false),
    });

    expect(c.form.title).toBe('Dentist (rescheduled)');
  });

  it('re-initialises after close and reopen for a new entity', () => {
    const c = new EventModalComponent();
    c.open = true;
    c.event = event;
    c.ngOnChanges({ open: new SimpleChange(false, true, true) });
    c.form.title = 'edited but abandoned';

    c.open = false;
    c.event = null;
    c.ngOnChanges({
      open: new SimpleChange(true, false, false),
      event: new SimpleChange(event, null, false),
    });

    c.open = true;
    c.prefilledDate = '2026-08-01';
    c.ngOnChanges({
      open: new SimpleChange(false, true, false),
      prefilledDate: new SimpleChange('', '2026-08-01', false),
    });

    expect(c.form.title).toBe('');
    expect(c.form.start).toBe('2026-08-01');
  });

  it('re-initialises when a different event is opened', () => {
    const c = new EventModalComponent();
    c.open = true;
    c.event = event;
    c.ngOnChanges({ open: new SimpleChange(false, true, true) });
    c.form.title = 'edited';

    const other = { ...event, id: 'e2', title: 'Gym' } as CalendarEvent;
    c.event = other;
    c.ngOnChanges({ event: new SimpleChange(event, other, false) });

    expect(c.form.title).toBe('Gym');
  });
});
