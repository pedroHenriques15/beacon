import { Component, EventEmitter, Input, OnChanges, Output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Task, TaskFormData, TaskList } from '../../core/models/task';

@Component({
  selector: 'app-task-modal',
  standalone: true,
  imports: [FormsModule],
  templateUrl: './task-modal.html',
  styleUrl: './_task-modal.scss',
})
export class TaskModalComponent implements OnChanges {
  @Input() open = false;
  @Input() task: Task | null = null;
  @Input() taskLists: TaskList[] = [];
  @Input() selectedListId = '';
  @Input() saving = false;
  @Input() saveError: string | null = null;

  @Output() save = new EventEmitter<TaskFormData>();
  @Output() delete = new EventEmitter<string>();
  @Output() cancel = new EventEmitter<void>();

  form: TaskFormData = {
    title: '',
    notes: '',
    due: '',
    completed: false,
    taskListId: '',
  };

  ngOnChanges(): void {
    if (!this.open) return;

    if (this.task) {
      this.form = {
        title: this.task.title,
        notes: this.task.notes ?? '',
        due: this.task.due ?? '',
        completed: this.task.completed,
        taskListId: this.task.taskListId,
      };
    } else {
      this.form = {
        title: '',
        notes: '',
        due: '',
        completed: false,
        taskListId: this.selectedListId,
      };
    }
  }

  onSave(): void {
    if (!this.form.title.trim()) return;
    this.save.emit({ ...this.form, title: this.form.title.trim() });
  }

  onDelete(): void {
    if (this.task) this.delete.emit(this.task.id);
  }

  onCancel(): void {
    this.cancel.emit();
  }
}
