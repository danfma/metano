import { HashCode } from "@meta-sharp/runtime";
import { Priority } from "#/priority";

export class TodoItem {
  constructor(readonly title: string, readonly completed: boolean = false, readonly priority: Priority = "medium") { }

  toggleCompleted(): TodoItem {
    return this.with({ completed: !this.completed });
  }

  setPriority(priority: Priority): TodoItem {
    return this.with({ priority: priority });
  }

  toString(): string {
    return `[${(this.completed ? "x" : " ")}] ${this.title} (${this.priority})`;
  }

  equals(other: any): boolean {
    return other instanceof TodoItem && this.title === other.title && this.completed === other.completed && this.priority === other.priority;
  }

  hashCode(): number {
    const hc = new HashCode();
    hc.add(this.title);
    hc.add(this.completed);
    hc.add(this.priority);
    return hc.toHashCode();
  }

  with(overrides?: Partial<TodoItem>): TodoItem {
    return new TodoItem(overrides?.title ?? this.title, overrides?.completed ?? this.completed, overrides?.priority ?? this.priority);
  }
}
