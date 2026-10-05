import { describe, it, expect } from 'vitest';
import { CalendarEvent } from '../../core/models/calendar-event';
import { Task } from '../../core/models/task';
import {
  CalendarDay,
  MonthInput,
  WeekRow,
  agendaGroups,
  agendaRange,
  agendaTitle,
  buildWeeks,
  dayLabel,
  eventColor,
  newEventDate,
  rangeLabel,
} from './calendar-layout';

let nextId = 1;

function ev(overrides: Partial<CalendarEvent>): CalendarEvent {
  return {
    id: `e${nextId++}`,
    title: 'Event',
    start: '2026-10-06',
    end: '2026-10-06',
    isAllDay: true,
    calendarId: 'personal',
    ...overrides,
  };
}

function task(overrides: Partial<Task>): Task {
  return {
    id: `t${nextId++}`,
    taskListId: 'finance',
    title: 'Task',
    completed: false,
    ...overrides,
  };
}

// October 2026: the 1st is a Thursday; "today" is Sunday 4 October.
function month(overrides: Partial<MonthInput> = {}): MonthInput {
  return {
    year: 2026,
    month: 9,
    events: [],
    tasks: [],
    hidden: new Set<string>(),
    today: '2026-10-04',
    ...overrides,
  };
}

function day(weeks: WeekRow[], dateStr: string): CalendarDay {
  const found = weeks.flatMap((w) => w.days).find((d) => d.dateStr === dateStr);
  if (!found) throw new Error(`${dateStr} is not in the grid`);
  return found;
}

describe('buildWeeks', () => {
  it('starts on the Monday before the 1st and stops after the week of the last day', () => {
    const weeks = buildWeeks(month());

    expect(weeks).toHaveLength(5);
    expect(weeks[0].days[0].dateStr).toBe('2026-09-28');
    expect(weeks[0].days[0].isCurrentMonth).toBe(false);
    expect(weeks[0].days[3]).toMatchObject({
      dateStr: '2026-10-01',
      isCurrentMonth: true,
      label: 'Thursday 1 October',
    });
    expect(weeks[4].days[6].dateStr).toBe('2026-11-01');
    expect(day(weeks, '2026-10-04')).toMatchObject({
      isToday: true,
      aria: 'Sunday 4 October, today',
    });
  });

  it('shows four weeks for a February that starts on a Monday', () => {
    const weeks = buildWeeks(month({ year: 2027, month: 1 }));

    expect(weeks).toHaveLength(4);
    expect(weeks[0].days[0].dateStr).toBe('2027-02-01');
    expect(weeks[3].days[6].dateStr).toBe('2027-02-28');
  });

  it('puts events and due tasks on their day, all-day first, hidden calendars left out', () => {
    const weeks = buildWeeks(
      month({
        events: [
          ev({
            title: 'Dentist',
            start: '2026-10-06T09:30:00+01:00',
            end: '2026-10-06T10:30:00+01:00',
            isAllDay: false,
          }),
          ev({ title: 'Rent due', calendarId: 'bills' }),
          ev({ title: 'Sprint review', calendarId: 'work' }),
        ],
        tasks: [task({ title: 'Book boiler service', due: '2026-10-06' })],
        hidden: new Set(['work']),
      }),
    );
    const tuesday = day(weeks, '2026-10-06');

    expect(tuesday.events.map((e) => e.event.title)).toEqual(['Rent due', 'Dentist']);
    expect(tuesday.events.map((e) => e.time)).toEqual(['', '09:30']);
    expect(tuesday.tasks.map((t) => t.title)).toEqual(['Book boiler service']);
    expect(tuesday.count).toBe(3);
    expect(tuesday.aria).toBe('Tuesday 6 October, 3 items');
  });

  it('turns an event over several days into a span bar for each week it crosses', () => {
    // Friday 23 to Tuesday 27 October: the end of one week and the start of the next.
    const weeks = buildWeeks(month({ events: [ev({ start: '2026-10-23', end: '2026-10-27' })] }));

    expect(weeks[3].spans).toHaveLength(1);
    expect(weeks[3].spans[0]).toMatchObject({
      startCol: 5,
      endCol: 7,
      row: 1,
      isStart: true,
      isEnd: false,
      top: 41,
      left: 'calc((100% - 24px) * 4 / 7 + 22px)',
      width: 'calc((100% - 24px) * 3 / 7 + 2px)',
    });
    expect(weeks[4].spans[0]).toMatchObject({
      startCol: 1,
      endCol: 2,
      isStart: false,
      isEnd: true,
    });
    expect(weeks[3].spanSpace).toBe(26);
    expect(weeks[2].spanSpace).toBe(0);
    expect(day(weeks, '2026-10-24').events).toEqual([]);
    expect(day(weeks, '2026-10-24').count).toBe(1);
  });

  it('stacks overlapping spans in rows', () => {
    const weeks = buildWeeks(
      month({
        events: [
          ev({ start: '2026-10-05', end: '2026-10-07' }),
          ev({ start: '2026-10-06', end: '2026-10-08' }),
          ev({ start: '2026-10-09', end: '2026-10-10' }),
        ],
      }),
    );

    expect(weeks[1].spans.map((s) => s.row)).toEqual([1, 2, 1]);
    expect(weeks[1].spans[1].top).toBe(71);
    expect(weeks[1].spanSpace).toBe(56);
  });

  it('gives the compact month one dot per colour, spans included, at most three', () => {
    const weeks = buildWeeks(
      month({
        events: [
          ev({ start: '2026-10-12', end: '2026-10-12', colorId: '11' }),
          ev({ start: '2026-10-12', end: '2026-10-12', colorId: '11' }),
          ev({ start: '2026-10-11', end: '2026-10-13', calendarColor: '#5f7ff2' }),
          ev({ start: '2026-10-12', end: '2026-10-12', calendarColor: '#e0703c' }),
          ev({ start: '2026-10-12', end: '2026-10-12', calendarColor: '#c95ca8' }),
        ],
      }),
    );

    expect(day(weeks, '2026-10-12').dots).toEqual(['#5f7ff2', '#616161', '#e0703c']);
    expect(day(weeks, '2026-10-12').count).toBe(5);
  });
});

describe('eventColor', () => {
  it('prefers the event’s own colour, then its calendar’s, then the legend’s', () => {
    const legend = new Map([['personal', '#6c63ff']]);

    expect(eventColor(ev({ colorId: '5', calendarColor: '#000000' }), legend)).toBe('#33b679');
    expect(eventColor(ev({ colorId: '99', calendarColor: '#123456' }), legend)).toBe('#123456');
    expect(eventColor(ev({}), legend)).toBe('#6c63ff');
    expect(eventColor(ev({}))).toBeNull();
  });
});

describe('labels', () => {
  it('names days and ranges the way the agenda shows them', () => {
    expect(dayLabel('2026-10-05')).toBe('Mon 5 Oct');
    expect(rangeLabel('2026-10-23', '2026-10-25')).toBe('Fri 23 to Sun 25 Oct');
    expect(rangeLabel('2026-09-30', '2026-10-02')).toBe('Wed 30 Sep to Fri 2 Oct');
    expect(rangeLabel('2026-12-31', '2027-01-02')).toBe('Thu 31 Dec 2026 to Sat 2 Jan 2027');
  });
});

describe('newEventDate', () => {
  it('starts on today in the current month and on the 1st in any other', () => {
    const today = new Date(2026, 9, 4);

    expect(newEventDate(2026, 9, today)).toBe(today);
    expect(newEventDate(2026, 10, today)).toEqual(new Date(2026, 10, 1));
    expect(newEventDate(2025, 9, today)).toEqual(new Date(2025, 9, 1));
  });
});

describe('agendaRange and agendaTitle', () => {
  it('covers the rest of the current month, or the whole of another', () => {
    expect(agendaRange(2026, 9, '2026-10-04')).toEqual({ from: '2026-10-04', to: '2026-10-31' });
    expect(agendaRange(2026, 10, '2026-10-04')).toEqual({ from: '2026-11-01', to: '2026-11-30' });
    expect(agendaRange(2026, 8, '2026-10-04')).toEqual({ from: '2026-09-01', to: '2026-09-30' });
  });

  it('says what is coming up now or ahead, and names a month gone by', () => {
    const today = new Date(2026, 9, 4);

    expect(agendaTitle(2026, 9, today)).toBe('Coming up');
    expect(agendaTitle(2026, 10, today)).toBe('Coming up in November');
    expect(agendaTitle(2026, 8, today)).toBe('In September');
    expect(agendaTitle(2025, 11, today)).toBe('In December');
  });
});

describe('agendaGroups', () => {
  const listTitles = new Map([['finance', 'Finance']]);

  function agenda(events: CalendarEvent[], tasks: Task[] = [], hidden = new Set<string>()) {
    return agendaGroups({
      events,
      tasks,
      hidden,
      from: '2026-10-04',
      to: '2026-10-31',
      today: '2026-10-04',
      listTitles,
    });
  }

  it('groups what is left of the month by day, in date order', () => {
    const groups = agenda(
      [
        ev({
          title: 'Sprint review',
          start: '2026-10-07T10:00:00+01:00',
          end: '2026-10-07T11:00:00+01:00',
          isAllDay: false,
          calendarName: 'Work',
        }),
        ev({
          title: 'Dentist',
          start: '2026-10-06T09:30:00+01:00',
          end: '2026-10-06T10:00:00+01:00',
          isAllDay: false,
          calendarName: 'Personal',
        }),
        ev({ title: 'Rent due', start: '2026-10-01', end: '2026-10-01' }),
        ev({
          title: 'EDP bill due',
          start: '2026-10-07',
          end: '2026-10-07',
          calendarName: 'Bills',
        }),
      ],
      [
        task({ title: 'Upload statements', due: '2026-10-05' }),
        task({ title: 'Next month', due: '2026-11-02' }),
      ],
    );

    expect(groups.map((g) => g.label)).toEqual([
      'Today, Sun 4 Oct',
      'Mon 5 Oct',
      'Tue 6 Oct',
      'Wed 7 Oct',
    ]);
    expect(groups[0]).toMatchObject({ isToday: true, items: [] });
    expect(groups[1].items).toMatchObject([
      { title: 'Upload statements', meta: 'Task, Finance', color: null, done: false },
    ]);
    expect(groups[2].items[0].meta).toBe('09:30, Personal');
    expect(groups[3].items.map((i) => i.title)).toEqual(['EDP bill due', 'Sprint review']);
    expect(groups[3].items[0].meta).toBe('All day, Bills');
  });

  it('lists a day’s events before its tasks, and marks tasks already done', () => {
    const groups = agenda(
      [ev({ title: 'Car insurance renewal', start: '2026-10-31', end: '2026-10-31' })],
      [task({ title: 'Renew car insurance', due: '2026-10-31', completed: true })],
    );
    const last = groups[groups.length - 1];

    expect(last.label).toBe('Sat 31 Oct');
    expect(last.items.map((i) => [i.title, i.meta, i.done])).toEqual([
      ['Car insurance renewal', 'All day', false],
      ['Renew car insurance', 'Done, Finance', true],
    ]);
  });

  it('gives an event over several days a group of its own, after its first day shown', () => {
    const groups = agenda([
      ev({
        title: 'Porto weekend',
        start: '2026-10-23',
        end: '2026-10-25',
        calendarName: 'Personal',
      }),
      ev({ title: 'Payday', start: '2026-10-23', end: '2026-10-23' }),
      ev({ title: 'Conference', start: '2026-09-30', end: '2026-10-05' }),
    ]);

    expect(groups.map((g) => g.label)).toEqual([
      'Today, Sun 4 Oct',
      'Wed 30 Sep to Mon 5 Oct',
      'Fri 23 Oct',
      'Fri 23 to Sun 25 Oct',
    ]);
    expect(groups[1].items[0].meta).toBe('6 days');
    expect(groups[3].items[0].meta).toBe('3 days, Personal');
  });

  it('leaves out hidden calendars and anything outside the range', () => {
    const groups = agenda(
      [
        ev({ start: '2026-10-10', end: '2026-10-10', calendarId: 'work' }),
        ev({ start: '2026-11-01', end: '2026-11-01' }),
      ],
      [],
      new Set(['work']),
    );

    expect(groups.map((g) => g.key)).toEqual(['2026-10-04']);
  });

  it('has no today group for another month', () => {
    const groups = agendaGroups({
      events: [],
      tasks: [],
      hidden: new Set<string>(),
      from: '2026-11-01',
      to: '2026-11-30',
      today: '2026-10-04',
    });

    expect(groups).toEqual([]);
  });
});
