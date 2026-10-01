export interface TaskList {
  id: string;
  title: string;
}

export interface Task {
  id: string;
  taskListId: string;
  title: string;
  notes?: string;
  due?: string;
  completed: boolean;
  completedAt?: string;
}

export interface TaskFormData {
  title: string;
  notes: string;
  due: string;
  completed: boolean;
  taskListId: string;
}
