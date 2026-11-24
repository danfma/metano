import { HashCode, copyRecordWith, valueEquals } from "metano-runtime";
import type { Point } from "./point.js";
import { Priority } from "./priority.js";

export class TodoItem {
  constructor(readonly title: string, readonly priority: Priority, readonly location: Point) { }

  get isUrgent(): boolean {
    return this.priority === Priority.High;
  }

  escalate(): TodoItem {
    return this.with({ priority: this.priority === Priority.Low ? Priority.Medium : Priority.High });
  }

  describe(): string {
    return `${this.title ?? ""} (${this.priority}) at ${this.location.x},${this.location.y}`;
  }

  equals(other: any): boolean {
    return other != null && Object.getPrototypeOf(other) === Object.getPrototypeOf(this) && this.title === other.title && this.priority === other.priority && valueEquals(this.location, other.location);
  }

  hashCode(): number {
    const hc = new HashCode();
    hc.add(this.title);
    hc.add(this.priority);
    hc.add(this.location);

    return hc.toHashCode();
  }

  with(overrides?: Partial<TodoItem>): TodoItem {
    return copyRecordWith<TodoItem>(this, overrides);
  }
}
