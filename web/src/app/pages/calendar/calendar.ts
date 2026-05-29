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
import { SlicePipe } from '@angular/common';
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
  imports: [EventModalComponent, TaskModalComponent, RouterLink, SlicePipe],
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

  selectedTaskListId = signal<string>('');
  showCompleted = signal(false);

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
  taskListTitleMap = computed(() =>
    new Map(this.tasksService.taskLists().map((l) => [l.id, l.title]))
  );

  calendarDays = computed<CalendarDay[]>(() => {
    const y = this.year();
    const m = this.month();
    const events = this.calendarService.events();
    const hidden = this.hiddenCalendarIds();
    const tasks = this.tasksWithDue();
    const todayStr = toDateStr(new Date());

    const firstDay = new Date(y, m, 1);
    let startDow = firstDay.getDay();
    startDow = startDow === 0 ? 6 : startDow - 1;

    return Array.from({ length: 42 }, (_, i) => {
      const date = new Date(y, m, 1 - startDow + i);
      const dateStr = toDateStr(date);
      return {
        date,
        dateStr,
        isCurrentMonth: date.getMonth() === m,
        isToday: dateStr === todayStr,
        events: events.filter((e) => !hidden.has(e.calendarId) && eventFallsOnDate(e, dateStr)),
        tasks: tasks.filter((t) => t.due === dateStr),
      };
    });
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

    this.saving.set(true);
    this.saveError.set(null);
    save$.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => {
        this.saving.set(false);
        this.closeModal();
        this.calendarService.loadEvents(this.year(), this.month());
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

    this.taskSaving.set(true);
    this.taskSaveError.set(null);
    save$.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => {
        this.taskSaving.set(false);
        this.closeTaskModal();
        this.refreshTasks();
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
        error: () => {
          this.tasksService.patchTask(task.id, { completed: task.completed });
          this.taskToggleError.set('Failed to update task. Please try again.');
        },
      });
  }

  private refreshTasks(): void {
    this.tasksService.loadAllTasks();
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
