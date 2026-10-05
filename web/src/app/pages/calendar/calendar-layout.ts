import { CalendarEvent } from '../../core/models/calendar-event';
import { Task } from '../../core/models/task';
import { GOOGLE_CALENDAR_COLORS } from '../../core/constants/calendar-colors';

export const MONTH_NAMES = [
  'January',
  'February',
  'March',
  'April',
  'May',
  'June',
  'July',
  'August',
  'September',
  'October',
  'November',
  'December',
];

const WEEKDAY_NAMES = [
  'Sunday',
  'Monday',
  'Tuesday',
  'Wednesday',
  'Thursday',
  'Friday',
  'Saturday',
];

/** An event as a day cell shows it: its colour and, unless it lasts all day, its start time. */
export interface DayEvent {
  event: CalendarEvent;
  color: string | null;
  /** 'HH:MM', or '' for an all-day event. */
  time: string;
}

export interface CalendarDay {
  date: Date;
  dateStr: string;
  /** 'Monday 5 October' */
  label: string;
  /** 'Monday 5 October, today, 3 items': the compact month's spoken label. */
  aria: string;
  isCurrentMonth: boolean;
  isToday: boolean;
  /** Events that start and end on this day; longer ones are spans of the week. */
  events: DayEvent[];
  tasks: Task[];
  /** The compact month's dots: one per colour on the day, spans included, at most three. */
  dots: (string | null)[];
  /** Everything on the day: its events, the spans across it and the tasks due. */
  count: number;
}

/** A multi-day event's bar across one week, placed over the week's cells. */
export interface SpanLayout {
  event: CalendarEvent;
  color: string | null;
  startCol: number;
  endCol: number;
  row: number;
  isStart: boolean;
  isEnd: boolean;
  /** CSS `left` and `width` over a seven-column grid with 4 px gaps. */
  left: string;
  width: string;
  /** Pixels from the top of the week. */
  top: number;
}

export interface WeekRow {
  days: CalendarDay[];
  spans: SpanLayout[];
  maxSpanRow: number;
  /** Pixels the cells keep free under the day number for the span bars. */
  spanSpace: number;
}

export interface MonthInput {
  year: number;
  /** 0 to 11 */
  month: number;
  events: CalendarEvent[];
  /** Tasks with a due date. */
  tasks: Task[];
  hidden: ReadonlySet<string>;
  /** 'YYYY-MM-DD' */
  today: string;
  /** Calendar id to the colour the legend shows for it. */
  colors?: ReadonlyMap<string, string>;
}

// Geometry of the desktop grid, kept in step with _calendar-grid.scss: cells 4 px apart,
// 8 px padding and a 28 px day number with a 5 px gap under it, bars 26 px tall.
const CELL_GAP = 4;
const SPAN_INSET = 6;
const SPAN_TOP = 41;
const SPAN_PITCH = 30;
const MAX_DOTS = 3;

export function toDateStr(date: Date): string {
  const y = date.getFullYear();
  const m = String(date.getMonth() + 1).padStart(2, '0');
  const d = String(date.getDate()).padStart(2, '0');
  return `${y}-${m}-${d}`;
}

function parseDate(dateStr: string): Date {
  const [y, m, d] = dateStr.split('-').map(Number);
  return new Date(y, m - 1, d);
}

/** 'Mon 5 Oct' */
export function dayLabel(dateStr: string): string {
  const d = parseDate(dateStr);
  return `${WEEKDAY_NAMES[d.getDay()].slice(0, 3)} ${d.getDate()} ${MONTH_NAMES[d.getMonth()].slice(0, 3)}`;
}

/** 'Monday 5 October' */
export function longDayLabel(date: Date): string {
  return `${WEEKDAY_NAMES[date.getDay()]} ${date.getDate()} ${MONTH_NAMES[date.getMonth()]}`;
}

/** 'Fri 23 to Sun 25 Oct', 'Wed 30 Sep to Fri 2 Oct', with years only when they differ. */
export function rangeLabel(from: string, to: string): string {
  const a = parseDate(from);
  const b = parseDate(to);
  if (a.getFullYear() !== b.getFullYear()) {
    return `${dayLabel(from)} ${a.getFullYear()} to ${dayLabel(to)} ${b.getFullYear()}`;
  }
  if (a.getMonth() !== b.getMonth()) return `${dayLabel(from)} to ${dayLabel(to)}`;
  return `${WEEKDAY_NAMES[a.getDay()].slice(0, 3)} ${a.getDate()} to ${dayLabel(to)}`;
}

function daysBetween(from: string, to: string): number {
  const a = parseDate(from);
  const b = parseDate(to);
  const utc = (d: Date) => Date.UTC(d.getFullYear(), d.getMonth(), d.getDate());
  return Math.round((utc(b) - utc(a)) / 86_400_000);
}

function startDate(event: CalendarEvent): string {
  return event.start.substring(0, 10);
}

function endDate(event: CalendarEvent): string {
  return event.end.substring(0, 10);
}

export function isMultiDay(event: CalendarEvent): boolean {
  return startDate(event) !== endDate(event);
}

function eventFallsOnDate(event: CalendarEvent, dateStr: string): boolean {
  if (event.isAllDay) {
    return dateStr >= event.start && dateStr <= event.end;
  }
  return startDate(event) === dateStr;
}

/**
 * The event's own Google colour, else its calendar's colour, else the colour the legend
 * gives that calendar; null when none is known (the page then uses its accent).
 */
export function eventColor(
  event: CalendarEvent,
  colors?: ReadonlyMap<string, string>,
): string | null {
  const own = event.colorId ? GOOGLE_CALENDAR_COLORS[event.colorId]?.hex : undefined;
  return own ?? event.calendarColor ?? colors?.get(event.calendarId) ?? null;
}

function byStart(a: CalendarEvent, b: CalendarEvent): number {
  return a.start < b.start ? -1 : a.start > b.start ? 1 : 0;
}

/**
 * The month's weeks, Monday first, from the week of the 1st to the week of the last day.
 * Hidden calendars are left out; events over several days become spans of each week.
 */
export function buildWeeks(input: MonthInput): WeekRow[] {
  const { year, month, today, colors } = input;
  const visible = input.events.filter((e) => !input.hidden.has(e.calendarId)).sort(byStart);
  const single = visible.filter((e) => !isMultiDay(e));
  const spanning = visible.filter(isMultiDay);
  const startDow = (new Date(year, month, 1).getDay() + 6) % 7;

  const weeks: WeekRow[] = [];
  for (let w = 0; w < 6; w++) {
    const days: CalendarDay[] = [];
    for (let i = 0; i < 7; i++) {
      const date = new Date(year, month, 1 - startDow + w * 7 + i);
      const dateStr = toDateStr(date);
      const own = single.filter((e) => eventFallsOnDate(e, dateStr));
      const across = spanning.filter((e) => startDate(e) <= dateStr && endDate(e) >= dateStr);
      const tasks = input.tasks.filter((t) => t.due === dateStr);
      const label = longDayLabel(date);
      const count = own.length + across.length + tasks.length;
      const isToday = dateStr === today;
      days.push({
        date,
        dateStr,
        label,
        aria: dayAria(label, isToday, count),
        isCurrentMonth: date.getMonth() === month,
        isToday,
        events: own.map((event) => ({
          event,
          color: eventColor(event, colors),
          time: event.isAllDay ? '' : event.start.slice(11, 16),
        })),
        tasks,
        dots: distinctColors([...across, ...own], colors).slice(0, MAX_DOTS),
        count,
      });
    }
    if (w > 0 && !days.some((d) => d.isCurrentMonth)) break;
    weeks.push(weekRow(days, spanning, colors));
  }
  return weeks;
}

function dayAria(label: string, isToday: boolean, count: number): string {
  const items = count === 0 ? '' : `${count} ${count === 1 ? 'item' : 'items'}`;
  return [label, isToday ? 'today' : '', items].filter(Boolean).join(', ');
}

function distinctColors(
  events: CalendarEvent[],
  colors?: ReadonlyMap<string, string>,
): (string | null)[] {
  const seen = new Set<string>();
  const result: (string | null)[] = [];
  for (const e of events) {
    const color = eventColor(e, colors);
    const key = color ?? '';
    if (seen.has(key)) continue;
    seen.add(key);
    result.push(color);
  }
  return result;
}

function weekRow(
  days: CalendarDay[],
  spanning: CalendarEvent[],
  colors?: ReadonlyMap<string, string>,
): WeekRow {
  const weekStart = days[0].dateStr;
  const weekEnd = days[6].dateStr;
  const spans: SpanLayout[] = [];
  for (const event of spanning) {
    const start = startDate(event);
    const end = endDate(event);
    if (start > weekEnd || end < weekStart) continue;
    const startIdx = start < weekStart ? 0 : days.findIndex((d) => d.dateStr === start);
    const endIdx = end > weekEnd ? 6 : days.findIndex((d) => d.dateStr === end);
    spans.push({
      event,
      color: eventColor(event, colors),
      startCol: startIdx + 1,
      endCol: endIdx + 1,
      row: 0,
      isStart: start >= weekStart,
      isEnd: end <= weekEnd,
      left: '',
      width: '',
      top: 0,
    });
  }
  assignSpanRows(spans);
  spans.forEach(placeSpan);
  const maxSpanRow = spans.length > 0 ? Math.max(...spans.map((s) => s.row)) : 0;
  return {
    days,
    spans,
    maxSpanRow,
    spanSpace: maxSpanRow > 0 ? maxSpanRow * SPAN_PITCH - CELL_GAP : 0,
  };
}

function assignSpanRows(spans: SpanLayout[]): void {
  spans.sort((a, b) => a.startCol - b.startCol || a.event.start.localeCompare(b.event.start));
  const occupied: boolean[][] = [];
  for (const span of spans) {
    let r = 0;
    while (true) {
      if (!occupied[r]) occupied[r] = Array(7).fill(false);
      if (!occupied[r].slice(span.startCol - 1, span.endCol).some(Boolean)) {
        for (let c = span.startCol - 1; c < span.endCol; c++) occupied[r][c] = true;
        span.row = r + 1;
        break;
      }
      r++;
    }
  }
}

function placeSpan(span: SpanLayout): void {
  const before = span.startCol - 1;
  const length = span.endCol - span.startCol + 1;
  const leftInset = span.isStart ? SPAN_INSET : 0;
  const rightInset = span.isEnd ? SPAN_INSET : 0;
  const columns = `(100% - ${6 * CELL_GAP}px)`;
  span.left = `calc(${columns} * ${before} / 7 + ${before * CELL_GAP + leftInset}px)`;
  span.width = `calc(${columns} * ${length} / 7 + ${(length - 1) * CELL_GAP - leftInset - rightInset}px)`;
  span.top = SPAN_TOP + (span.row - 1) * SPAN_PITCH;
}

/** The day a new event starts on: today in the current month, else the shown month's 1st. */
export function newEventDate(year: number, month: number, today: Date): Date {
  return today.getFullYear() === year && today.getMonth() === month
    ? today
    : new Date(year, month, 1);
}

// ── "Coming up": the phone's agenda ──────────────────────────────────────────

export interface AgendaItem {
  /** Unique across events and tasks. */
  key: string;
  title: string;
  /** '09:30, Personal', 'All day, Bills', '3 days, Personal', 'Task, Finance' */
  meta: string;
  color: string | null;
  done: boolean;
  event?: CalendarEvent;
  task?: Task;
}

export interface AgendaGroup {
  /** 'YYYY-MM-DD' for a day, 'YYYY-MM-DD..YYYY-MM-DD' for an event over several days. */
  key: string;
  /** 'Today, Sun 4 Oct', 'Mon 5 Oct', 'Fri 23 to Sun 25 Oct' */
  label: string;
  isToday: boolean;
  items: AgendaItem[];
}

export interface AgendaInput {
  events: CalendarEvent[];
  /** Tasks with a due date. */
  tasks: Task[];
  hidden: ReadonlySet<string>;
  /** First and last day shown, 'YYYY-MM-DD', inclusive. */
  from: string;
  to: string;
  today: string;
  listTitles?: ReadonlyMap<string, string>;
  colors?: ReadonlyMap<string, string>;
}

/** The days the agenda covers: the rest of the current month, or all of another month. */
export function agendaRange(
  year: number,
  month: number,
  today: string,
): { from: string; to: string } {
  const first = toDateStr(new Date(year, month, 1));
  const last = toDateStr(new Date(year, month + 1, 0));
  return { from: today > first && today <= last ? today : first, to: last };
}

/** 'Coming up' for the current month, 'Coming up in November' ahead, 'In September' behind. */
export function agendaTitle(year: number, month: number, today: Date): string {
  const shown = year * 12 + month;
  const current = today.getFullYear() * 12 + today.getMonth();
  if (shown === current) return 'Coming up';
  return shown > current ? `Coming up in ${MONTH_NAMES[month]}` : `In ${MONTH_NAMES[month]}`;
}

/**
 * The agenda, one group per day with something on it, in date order. Within a day, all-day
 * events come first, then timed events by start, then tasks. An event over several days is a
 * group of its own, after the day it starts on (or the first day shown). Today always has a
 * group while it is in range, even an empty one.
 */
export function agendaGroups(input: AgendaInput): AgendaGroup[] {
  const { from, to, today, colors, listTitles } = input;
  // Each group with the key it sorts by: its day, then 0 for a day or 1 for a longer event.
  const groups = new Map<string, { sort: string; group: AgendaGroup }>();

  const group = (key: string, sort: string, label: string, isToday = false) => {
    let entry = groups.get(key);
    if (!entry) {
      entry = { sort, group: { key, label, isToday, items: [] } };
      groups.set(key, entry);
    }
    return entry.group;
  };
  const dayGroup = (date: string) =>
    date === today
      ? group(date, `${date} 0`, `Today, ${dayLabel(date)}`, true)
      : group(date, `${date} 0`, dayLabel(date));

  if (from <= today && today <= to) dayGroup(today);

  const events = input.events.filter((e) => !input.hidden.has(e.calendarId)).sort(byStart);
  for (const event of events) {
    const start = startDate(event);
    const end = endDate(event);
    if (end < from || start > to) continue;
    if (isMultiDay(event)) {
      const sort = `${start < from ? from : start} 1 ${end}`;
      group(`${start}..${end}`, sort, rangeLabel(start, end)).items.push(
        eventItem(event, `${daysBetween(start, end) + 1} days`, colors),
      );
    } else {
      const when = event.isAllDay ? 'All day' : event.start.slice(11, 16);
      dayGroup(start).items.push(eventItem(event, when, colors));
    }
  }

  for (const task of input.tasks) {
    if (!task.due || task.due < from || task.due > to) continue;
    const meta = [task.completed ? 'Done' : 'Task', listTitles?.get(task.taskListId)]
      .filter(Boolean)
      .join(', ');
    dayGroup(task.due).items.push({
      key: `t:${task.id}`,
      title: task.title,
      meta,
      color: null,
      done: task.completed,
      task,
    });
  }

  return [...groups.values()]
    .sort((a, b) => (a.sort < b.sort ? -1 : a.sort > b.sort ? 1 : 0))
    .map((entry) => entry.group);
}

function eventItem(
  event: CalendarEvent,
  when: string,
  colors?: ReadonlyMap<string, string>,
): AgendaItem {
  return {
    key: `e:${event.id}`,
    title: event.title,
    meta: [when, event.calendarName].filter(Boolean).join(', '),
    color: eventColor(event, colors),
    done: false,
    event,
  };
}
