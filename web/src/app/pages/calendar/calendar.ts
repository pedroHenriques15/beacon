import {
  Component,
  DestroyRef,
  OnInit,
  computed,
  effect,
  inject,
  signal,
  untracked,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { DatePipe, SlicePipe } from '@angular/common';
import { CalendarService, CalendarInfo } from '../../core/services/calendar.service';
import { GoogleAuthService } from '../../core/services/google-auth.service';
import { TasksService } from '../../core/services/tasks.service';
import { CalendarEvent, CalendarEventFormData } from '../../core/models/calendar-event';
import { Task, TaskFormData } from '../../core/models/task';
import { GOOGLE_CALENDAR_COLORS } from '../../core/constants/calendar-colors';
import { EventModalComponent } from './event-modal';
import { TaskModalComponent } from '../tasks/task-modal';

interface CalendarDay {
  date: Date;
  dateStr: string;
  isCurrentMonth: boolean;
  isToday: boolean;
  events: CalendarEvent[];
  tasks: Task[];
}

interface SpanLayout {
  event: CalendarEvent;
  startCol: number;
  endCol: number;
  row: number;
  isStart: boolean;
  isEnd: boolean;
}

interface WeekRow {
  days: CalendarDay[];
  spans: SpanLayout[];
  maxSpanRow: number;
}

const MONTH_NAMES = [
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

@Component({
  selector: 'app-calendar',
  standalone: true,
  imports: [EventModalComponent, TaskModalComponent, RouterLink, SlicePipe, DatePipe],
  templateUrl: './calendar.html',
  styleUrl: './calendar.scss',
})
export class CalendarPage implements OnInit {
  calendarService = inject(CalendarService);
  googleAuth = inject(GoogleAuthService);
  tasksService = inject(TasksService);
  private destroyRef = inject(DestroyRef);

  readonly DAY_NAMES = ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'];

  year = signal(new Date().getFullYear());
  month = signal(new Date().getMonth());

  modalOpen = signal(false);
  editingEvent = signal<CalendarEvent | null>(null);
  prefilledDate = signal<string>('');
  saving = signal(false);
  saveError = signal<string | null>(null);

  hiddenCalendarIds = signal<Set<string>>(new Set());

  taskModalOpen = signal(false);
  editingTask = signal<Task | null>(null);
  taskSaving = signal(false);
  taskSaveError = signal<string | null>(null);
  taskToggleError = signal<string | null>(null);

  successToast = signal<string | null>(null);
  private _toastTimer: ReturnType<typeof setTimeout> | null = null;

  selectedTaskListId = signal<string>('');
  showCompleted = signal(false);

  draggedTask = signal<Task | null>(null);
  dragOverTaskId = signal<string | null>(null);
  dragOverListId = signal<string | null>(null);

  private readonly _taskListAutoLoad = effect(() => {
    const lists = this.tasksService.taskLists();
    if (lists.length > 0 && !this.selectedTaskListId()) {
      untracked(() => {
        this.selectedTaskListId.set(lists[0].id);
        this.tasksService.loadAllTasks();
      });
    }
  });

  monthLabel = computed(() => `${MONTH_NAMES[this.month()]} ${this.year()}`);

  availableCalendars = this.calendarService.calendarList;

  tasksWithDue = computed(() => this.tasksService.tasks().filter((t) => !!t.due));
  pendingTaskGroups = computed(() => {
    const lists = this.tasksService.taskLists();
    const pending = this.tasksService.tasks().filter((t) => !t.completed);
    return lists
      .map((list) => ({
        listId: list.id,
        listTitle: list.title,
        tasks: pending
          .filter((t) => t.taskListId === list.id)
          .sort((a, b) => {
            if (a.due && b.due) return a.due.localeCompare(b.due);
            if (a.due) return -1;
            if (b.due) return 1;
            return 0;
          }),
      }))
      .filter((group) => group.tasks.length > 0);
  });
  completedTasks = computed(() => this.tasksService.tasks().filter((t) => t.completed));
  taskListTitleMap = computed(
    () => new Map(this.tasksService.taskLists().map((l) => [l.id, l.title])),
  );

  calendarWeeks = computed<WeekRow[]>(() => {
    const y = this.year();
    const m = this.month();
    const events = this.calendarService.events();
    const hidden = this.hiddenCalendarIds();
    const tasks = this.tasksWithDue();
    const todayStr = toDateStr(new Date());

    const firstDay = new Date(y, m, 1);
    let startDow = firstDay.getDay();
    startDow = startDow === 0 ? 6 : startDow - 1;

    const allDays: CalendarDay[] = Array.from({ length: 42 }, (_, i) => {
      const date = new Date(y, m, 1 - startDow + i);
      const dateStr = toDateStr(date);
      return {
        date,
        dateStr,
        isCurrentMonth: date.getMonth() === m,
        isToday: dateStr === todayStr,
        events: events.filter(
          (e) => !hidden.has(e.calendarId) && !isMultiDay(e) && eventFallsOnDate(e, dateStr),
        ),
        tasks: tasks.filter((t) => t.due === dateStr),
      };
    });

    const spanningEvents = events.filter((e) => !hidden.has(e.calendarId) && isMultiDay(e));

    const weeks: WeekRow[] = [];
    for (let w = 0; w < 6; w++) {
      const days = allDays.slice(w * 7, w * 7 + 7);
      const weekStart = days[0].dateStr;
      const weekEnd = days[6].dateStr;

      const weekSpans: SpanLayout[] = [];
      for (const event of spanningEvents) {
        const eventStartDate = event.start.substring(0, 10);
        const eventEndDate = event.end.substring(0, 10);
        if (eventStartDate > weekEnd || eventEndDate < weekStart) continue;
        const clampedStart = eventStartDate < weekStart ? weekStart : eventStartDate;
        const clampedEnd = eventEndDate > weekEnd ? weekEnd : eventEndDate;
        const startIdx = days.findIndex((d) => d.dateStr === clampedStart);
        const endIdx = days.findIndex((d) => d.dateStr === clampedEnd);
        weekSpans.push({
          event,
          startCol: startIdx + 1,
          endCol: endIdx + 1,
          row: 0,
          isStart: event.start >= weekStart,
          isEnd: event.end <= weekEnd,
        });
      }
      assignSpanRows(weekSpans);
      const maxSpanRow = weekSpans.length > 0 ? Math.max(...weekSpans.map((s) => s.row)) : 0;
      weeks.push({ days, spans: weekSpans, maxSpanRow });
    }
    return weeks;
  });

  ngOnInit(): void {
    this.calendarService.loadEvents(this.year(), this.month());
    this.tasksService.loadTaskLists();
  }

  prevMonth(): void {
    if (this.month() === 0) {
      this.year.update((y) => y - 1);
      this.month.set(11);
    } else {
      this.month.update((m) => m - 1);
    }
    this.calendarService.loadEvents(this.year(), this.month());
  }

  nextMonth(): void {
    if (this.month() === 11) {
      this.year.update((y) => y + 1);
      this.month.set(0);
    } else {
      this.month.update((m) => m + 1);
    }
    this.calendarService.loadEvents(this.year(), this.month());
  }

  goToToday(): void {
    const now = new Date();
    this.year.set(now.getFullYear());
    this.month.set(now.getMonth());
    this.calendarService.loadEvents(this.year(), this.month());
  }

  toggleCalendar(id: string): void {
    this.hiddenCalendarIds.update((s) => {
      const next = new Set(s);
      if (next.has(id)) {
        next.delete(id);
      } else {
        next.add(id);
      }
      return next;
    });
  }

  openCreateModal(date: Date): void {
    this.editingEvent.set(null);
    this.prefilledDate.set(toDateStr(date));
    this.modalOpen.set(true);
  }

  openEditModal(event: CalendarEvent, domEvent: MouseEvent): void {
    domEvent.stopPropagation();
    this.editingEvent.set(event);
    this.prefilledDate.set('');
    this.modalOpen.set(true);
  }

  closeModal(): void {
    this.modalOpen.set(false);
    this.editingEvent.set(null);
    this.prefilledDate.set('');
    this.saveError.set(null);
  }

  onSave(data: CalendarEventFormData): void {
    const editing = this.editingEvent();
    const save$ = editing
      ? this.calendarService.updateEvent(editing.id, editing.calendarId, data)
      : this.calendarService.createEvent(data);
    const successMsg = editing ? 'Event updated' : 'Event created';

    this.saving.set(true);
    this.saveError.set(null);
    save$.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => {
        this.saving.set(false);
        this.closeModal();
        this.calendarService.loadEvents(this.year(), this.month());
        this.showToast(successMsg);
      },
      error: () => {
        this.saving.set(false);
        this.saveError.set('Failed to save event. Please try again.');
      },
    });
  }

  chipStyle(event: CalendarEvent): Record<string, string> {
    const color = event.colorId ? GOOGLE_CALENDAR_COLORS[event.colorId]?.hex : event.calendarColor;
    if (!color) return {};
    return {
      background: `color-mix(in srgb, ${color} 20%, transparent)`,
      'border-left-color': color,
    };
  }

  spanStyle(span: SpanLayout): Record<string, string> {
    const leftPct = ((span.startCol - 1) / 7) * 100;
    const widthPct = ((span.endCol - span.startCol + 1) / 7) * 100;
    const topRem = 2.2 + (span.row - 1) * 1.6;
    const leftInset = span.isStart ? 2 : 0;
    const rightInset = span.isEnd ? 2 : 0;
    const styles: Record<string, string> = {
      left: `calc(${leftPct}% + ${leftInset}px)`,
      top: `${topRem}rem`,
      width: `calc(${widthPct}% - ${leftInset + rightInset}px)`,
    };
    const color = span.event.colorId
      ? GOOGLE_CALENDAR_COLORS[span.event.colorId]?.hex
      : span.event.calendarColor;
    if (color) {
      styles['background'] = `color-mix(in srgb, ${color} 20%, transparent)`;
      if (span.isStart) styles['border-left-color'] = color;
    }
    return styles;
  }

  onDelete(id: string): void {
    this.saving.set(true);
    this.saveError.set(null);
    const calendarId = this.editingEvent()?.calendarId ?? 'primary';
    this.calendarService
      .deleteEvent(id, calendarId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.saving.set(false);
          this.closeModal();
          this.calendarService.loadEvents(this.year(), this.month());
          this.showToast('Event deleted');
        },
        error: () => {
          this.saving.set(false);
          this.saveError.set('Failed to delete event. Please try again.');
        },
      });
  }

  openTaskModal(task?: Task, domEvent?: MouseEvent): void {
    domEvent?.stopPropagation();
    this.editingTask.set(task ?? null);
    this.taskSaveError.set(null);
    this.taskModalOpen.set(true);
  }

  closeTaskModal(): void {
    this.taskModalOpen.set(false);
    this.editingTask.set(null);
    this.taskSaveError.set(null);
  }

  onTaskSave(data: TaskFormData): void {
    const editing = this.editingTask();
    const save$ = editing
      ? this.tasksService.updateTask(editing.id, data)
      : this.tasksService.createTask(data);
    const successMsg = editing ? 'Task updated' : 'Task created';

    this.taskSaving.set(true);
    this.taskSaveError.set(null);
    save$.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => {
        this.taskSaving.set(false);
        this.closeTaskModal();
        this.refreshTasks();
        this.showToast(successMsg);
      },
      error: () => {
        this.taskSaving.set(false);
        this.taskSaveError.set('Failed to save task. Please try again.');
      },
    });
  }

  onTaskDelete(id: string): void {
    const listId = this.editingTask()?.taskListId ?? this.selectedTaskListId();
    this.taskSaving.set(true);
    this.taskSaveError.set(null);
    this.tasksService
      .deleteTask(id, listId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.taskSaving.set(false);
          this.closeTaskModal();
          this.refreshTasks();
          this.showToast('Task deleted');
        },
        error: () => {
          this.taskSaving.set(false);
          this.taskSaveError.set('Failed to delete task. Please try again.');
        },
      });
  }

  onTaskComplete(task: Task, domEvent: Event): void {
    domEvent.stopPropagation();
    const newCompleted = !task.completed;

    this.tasksService.patchTask(task.id, { completed: newCompleted });
    this.taskToggleError.set(null);

    const data: TaskFormData = {
      title: task.title,
      notes: task.notes ?? '',
      due: task.due ?? '',
      completed: newCompleted,
      taskListId: task.taskListId,
    };
    this.tasksService
      .updateTask(task.id, data)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.showToast('Task updated');
        },
        error: () => {
          this.tasksService.patchTask(task.id, { completed: task.completed });
          this.taskToggleError.set('Failed to update task. Please try again.');
        },
      });
  }

  onTaskDragStart(task: Task, event: DragEvent): void {
    this.draggedTask.set(task);
    event.dataTransfer?.setData('text/plain', task.id);
  }

  onTaskDragOver(task: Task, event: DragEvent): void {
    event.preventDefault();
    event.stopPropagation();
    if (this.isNoopReorder(this.draggedTask(), task.taskListId)) {
      this.dragOverTaskId.set(null);
      return;
    }
    this.dragOverTaskId.set(task.id);
    this.dragOverListId.set(null);
  }

  private isNoopReorder(dragged: Task | null, targetListId: string): boolean {
    return !!dragged?.due && dragged.taskListId === targetListId;
  }

  onListTitleDragOver(listId: string, event: DragEvent): void {
    event.preventDefault();
    this.dragOverListId.set(listId);
    this.dragOverTaskId.set(null);
  }

  onTaskDropOnTask(targetTask: Task, groupTasks: Task[], event: DragEvent): void {
    event.preventDefault();
    event.stopPropagation();
    const dragged = this.draggedTask();
    this.clearDragState();
    if (!dragged || dragged.id === targetTask.id) return;
    if (this.isNoopReorder(dragged, targetTask.taskListId)) return;

    let previousTaskId: string | null = null;
    if (!dragged.due) {
      const tasksWithoutDragged = groupTasks.filter((t) => t.id !== dragged.id);
      const targetIdx = tasksWithoutDragged.findIndex((t) => t.id === targetTask.id);
      previousTaskId = targetIdx > 0 ? tasksWithoutDragged[targetIdx - 1].id : null;
    }

    this.executeTaskMove(dragged, targetTask.taskListId, previousTaskId);
  }

  onTaskDropOnList(group: { listId: string; tasks: Task[] }, event: DragEvent): void {
    event.preventDefault();
    const dragged = this.draggedTask();
    this.clearDragState();
    if (!dragged) return;
    if (this.isNoopReorder(dragged, group.listId)) return;

    let previousTaskId: string | null = null;
    if (!dragged.due) {
      const tasksWithoutDragged = group.tasks.filter((t) => t.id !== dragged.id);
      previousTaskId =
        tasksWithoutDragged.length > 0
          ? tasksWithoutDragged[tasksWithoutDragged.length - 1].id
          : null;
    }

    this.executeTaskMove(dragged, group.listId, previousTaskId);
  }

  onTaskDragEnd(): void {
    this.clearDragState();
  }

  private clearDragState(): void {
    this.draggedTask.set(null);
    this.dragOverTaskId.set(null);
    this.dragOverListId.set(null);
  }

  private executeTaskMove(
    dragged: Task,
    targetListId: string,
    previousTaskId: string | null,
  ): void {
    if (dragged.taskListId !== targetListId) {
      this.tasksService.patchTask(dragged.id, { taskListId: targetListId });
    }

    this.tasksService
      .moveTask(dragged.id, dragged.taskListId, targetListId, previousTaskId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.refreshTasks();
          this.showToast('Task moved');
        },
        error: () => {
          this.refreshTasks();
          this.taskToggleError.set('Failed to move task. Please try again.');
        },
      });
  }

  private refreshTasks(): void {
    this.tasksService.loadAllTasks(true);
  }

  private showToast(msg: string): void {
    if (this._toastTimer) clearTimeout(this._toastTimer);
    this.successToast.set(msg);
    this._toastTimer = setTimeout(() => this.successToast.set(null), 2500);
  }
}

function toDateStr(date: Date): string {
  const y = date.getFullYear();
  const m = String(date.getMonth() + 1).padStart(2, '0');
  const d = String(date.getDate()).padStart(2, '0');
  return `${y}-${m}-${d}`;
}

function eventFallsOnDate(event: CalendarEvent, dateStr: string): boolean {
  if (event.isAllDay) {
    return dateStr >= event.start && dateStr <= event.end;
  }
  return event.start.substring(0, 10) === dateStr;
}

function isMultiDay(event: CalendarEvent): boolean {
  return event.start.substring(0, 10) !== event.end.substring(0, 10);
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
