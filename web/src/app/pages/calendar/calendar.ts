import {
  Component,
  DestroyRef,
  OnInit,
  computed,
  effect,
  inject,
  signal,
  untracked,
  ChangeDetectionStrategy,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { CalendarService } from '../../core/services/calendar.service';
import { GoogleAuthService } from '../../core/services/google-auth.service';
import { TasksService } from '../../core/services/tasks.service';
import { CalendarEvent, CalendarEventFormData } from '../../core/models/calendar-event';
import { Task, TaskFormData } from '../../core/models/task';
import { EventModalComponent } from './event-modal';
import { TaskModalComponent } from './task-modal';
import {
  AgendaItem,
  MONTH_NAMES,
  WeekRow,
  agendaGroups,
  agendaRange,
  agendaTitle as agendaTitleFor,
  buildWeeks,
  dayLabel,
  newEventDate,
  toDateStr,
} from './calendar-layout';

@Component({
  selector: 'app-calendar',
  standalone: true,
  imports: [EventModalComponent, TaskModalComponent, RouterLink],
  templateUrl: './calendar.html',
  changeDetection: ChangeDetectionStrategy.Eager,
  styleUrl: './calendar.scss',
})
export class CalendarPage implements OnInit {
  calendarService = inject(CalendarService);
  googleAuth = inject(GoogleAuthService);
  tasksService = inject(TasksService);
  private destroyRef = inject(DestroyRef);

  readonly DAY_NAMES = ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'];
  readonly dayLabel = dayLabel;

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
  connected = computed(() => this.googleAuth.status()?.connected === true);

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

  /** Calendar id to the colour the legend shows, so chips and legend agree. */
  private calendarColors = computed(
    () => new Map(this.calendarService.calendarList().map((c) => [c.id, c.color])),
  );

  calendarWeeks = computed<WeekRow[]>(() =>
    buildWeeks({
      year: this.year(),
      month: this.month(),
      events: this.calendarService.events(),
      tasks: this.tasksWithDue(),
      hidden: this.hiddenCalendarIds(),
      today: toDateStr(new Date()),
      colors: this.calendarColors(),
    }),
  );

  /** Phone: what is left of the shown month, day by day. */
  agenda = computed(() => {
    const today = toDateStr(new Date());
    return agendaGroups({
      ...agendaRange(this.year(), this.month(), today),
      events: this.calendarService.events(),
      tasks: this.tasksWithDue(),
      hidden: this.hiddenCalendarIds(),
      today,
      listTitles: this.taskListTitleMap(),
      colors: this.calendarColors(),
    });
  });
  agendaTitle = computed(() => agendaTitleFor(this.year(), this.month(), new Date()));

  ngOnInit(): void {
    this.googleAuth.loadStatus();
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

  /** The header's New event: today in the current month, else the shown month's 1st. */
  openNewEvent(): void {
    this.openCreateModal(newEventDate(this.year(), this.month(), new Date()));
  }

  openEditModal(event: CalendarEvent, domEvent?: MouseEvent): void {
    domEvent?.stopPropagation();
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

  openAgendaItem(item: AgendaItem): void {
    if (item.event) {
      this.openEditModal(item.event);
    } else if (item.task) {
      this.openTaskModal(item.task);
    }
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
