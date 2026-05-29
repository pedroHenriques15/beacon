export interface CalendarColor {
  hex: string;
  label: string;
}

export const GOOGLE_CALENDAR_COLORS: Record<string, CalendarColor> = {
  '1': { hex: '#d50000', label: 'Tomato' },
  '2': { hex: '#e67c73', label: 'Flamingo' },
  '3': { hex: '#f4511e', label: 'Tangerine' },
  '4': { hex: '#f6bf26', label: 'Banana' },
  '5': { hex: '#33b679', label: 'Sage' },
  '6': { hex: '#0b8043', label: 'Basil' },
  '7': { hex: '#039be5', label: 'Peacock' },
  '8': { hex: '#3f51b5', label: 'Blueberry' },
  '9': { hex: '#7986cb', label: 'Lavender' },
  '10': { hex: '#8e24aa', label: 'Grape' },
  '11': { hex: '#616161', label: 'Graphite' },
};

export const GOOGLE_CALENDAR_COLOR_ENTRIES = Object.entries(GOOGLE_CALENDAR_COLORS);
