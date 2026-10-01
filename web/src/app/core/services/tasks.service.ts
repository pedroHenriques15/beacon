import { Injectable, inject, signal } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { forkJoin, Observable, Subject } from 'rxjs';
import { takeUntil } from 'rxjs/operators';
import { Task, TaskFormData, TaskList } from '../models/task';

@Injectable({ providedIn: 'root' })
export class TasksService {
  private http = inject(HttpClient);
  private loadCancel$ = new Subject<void>();

  taskLists = signal<TaskList[]>([]);
  private _tasks = signal<Task[]>([]);
  readonly tasks = this._tasks.asReadonly();
  loading = signal(false);
  error = signal<string | null>(null);

  loadTaskLists(): void {
    this.http.get<TaskList[]>('/api/tasks/lists').subscribe({
      next: (lists) => this.taskLists.set(lists),
      error: () => this.error.set('Failed to load task lists.'),
    });
  }

  loadTasks(listId: string): void {
    if (!listId) return;
    const params = new HttpParams().set('listId', listId);

    this.loadCancel$.next();
    this.loading.set(true);
    this.error.set(null);

    this.http
      .get<Task[]>('/api/tasks', { params })
      .pipe(takeUntil(this.loadCancel$))
      .subscribe({
        next: (tasks) => {
          this._tasks.set(tasks);
          this.loading.set(false);
        },
        error: () => {
          this.error.set('Failed to load tasks.');
          this.loading.set(false);
        },
      });
  }

  patchTask(id: string, patch: Partial<Task>): void {
    this._tasks.update((all) => all.map((t) => (t.id === id ? { ...t, ...patch } : t)));
  }

  loadAllTasks(silent = false): void {
    const lists = this.taskLists();
    if (lists.length === 0) return;

    if (!silent) this.loading.set(true);
    this.error.set(null);

    forkJoin(
      lists.map((list) => this.http.get<Task[]>('/api/tasks', { params: { listId: list.id } })),
    ).subscribe({
      next: (results) => {
        this._tasks.set(results.flat());
        if (!silent) this.loading.set(false);
      },
      error: () => {
        this.error.set('Failed to load tasks.');
        if (!silent) this.loading.set(false);
      },
    });
  }

  createTask(data: TaskFormData): Observable<Task> {
    return this.http.post<Task>('/api/tasks', data);
  }

  updateTask(id: string, data: TaskFormData): Observable<Task> {
    return this.http.put<Task>(`/api/tasks/${id}`, data);
  }

  deleteTask(id: string, listId: string): Observable<void> {
    return this.http.delete<void>(`/api/tasks/${id}`, { params: { listId } });
  }

  moveTask(
    id: string,
    sourceListId: string,
    targetListId: string,
    previousTaskId: string | null,
  ): Observable<Task> {
    return this.http.post<Task>(`/api/tasks/${id}/move`, {
      sourceListId,
      targetListId,
      previousTaskId,
    });
  }
}
