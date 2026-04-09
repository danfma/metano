import { Priority } from "sample-todo/priority";
import { TodoItem } from "sample-todo/todo-item";

export class TodoSummarizer {
  constructor() { }

  describe(item: TodoItem): string {
    return `${(item.completed ? "[x]" : "[ ]")} ${item.title} (${item.priority})`;
  }

  withHighPriority(item: TodoItem): TodoItem {
    return item.setPriority(Priority.High);
  }
}
