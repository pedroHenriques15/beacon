import { describe, it, expect } from 'vitest';
import { SimpleChange } from '@angular/core';
import { TaskModalComponent } from './task-modal';
import { Task } from '../../core/models/task';

const task: Task = {
  id: 't1',
  taskListId: 'list1',
  title: 'Buy milk',
  completed: false,
} as Task;

describe('TaskModalComponent', () => {
  it('initialises the form when the modal opens', () => {
    const c = new TaskModalComponent();
    c.open = true;
    c.task = task;
    c.ngOnChanges({ open: new SimpleChange(false, true, true) });

    expect(c.form.title).toBe('Buy milk');
  });

  it('does NOT wipe in-progress edits when only saving/saveError change', () => {
    const c = new TaskModalComponent();
    c.open = true;
    c.task = task;
    c.ngOnChanges({ open: new SimpleChange(false, true, true) });

    c.form.title = 'Buy milk and bread';
    c.saveError = 'Failed to save. Please try again.';
    c.ngOnChanges({
      saving: new SimpleChange(true, false, false),
      saveError: new SimpleChange(null, c.saveError, false),
    });

    expect(c.form.title).toBe('Buy milk and bread');
  });

  it('re-initialises when a different task is opened', () => {
    const c = new TaskModalComponent();
    c.open = true;
    c.task = task;
    c.ngOnChanges({ open: new SimpleChange(false, true, true) });
    c.form.title = 'edited';

    const other = { ...task, id: 't2', title: 'Walk dog' } as Task;
    c.task = other;
    c.ngOnChanges({ task: new SimpleChange(task, other, false) });

    expect(c.form.title).toBe('Walk dog');
  });

  it('re-initialises after close and reopen for a new task', () => {
    const c = new TaskModalComponent();
    c.open = true;
    c.task = task;
    c.ngOnChanges({ open: new SimpleChange(false, true, true) });
    c.form.title = 'edited but abandoned';

    c.open = false;
    c.task = null;
    c.ngOnChanges({
      open: new SimpleChange(true, false, false),
      task: new SimpleChange(task, null, false),
    });

    c.open = true;
    c.selectedListId = 'list9';
    c.ngOnChanges({
      open: new SimpleChange(false, true, false),
      selectedListId: new SimpleChange('', 'list9', false),
    });

    expect(c.form.title).toBe('');
    expect(c.form.taskListId).toBe('list9');
  });
});
