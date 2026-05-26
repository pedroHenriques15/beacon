export interface CalendarEvent {
  id: string;
  title: string;
  start: string;
  end: string;
  description?: string;
  location?: string;
  isAllDay: boolean;
  colorId?: string;
  calendarId: string;
  calendarColor?: string;
  calendarName?: string;
}

export interface CalendarEventFormData {
  title: string;
  start: string;
  end: string;
  description?: string;
  location?: string;
  isAllDay: boolean;
  colorId?: string;
}
