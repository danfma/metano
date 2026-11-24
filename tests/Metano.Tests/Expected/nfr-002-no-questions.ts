// fixtures/no-runtime-questions/counter.ts
export class Counter {
  private _count: number = 0;

  constructor() { }

  get count(): number {
    return this._count;
  }

  increment(by: number = 1): void {
    this._count = (this._count + by) | 0;
  }

  static startingAt(value: number): Counter {
    const counter = new Counter();
    counter.increment(value);

    return counter;
  }
}

// fixtures/no-runtime-questions/geometry.ts
import { absInt32 } from "metano-runtime";
import { Point } from "./point";
import { Priority } from "./priority";
import { TodoItem } from "./todo-item";

export class Geometry {
  constructor() { }

  static distance(a: Point, b: Point): number {
    return (absInt32((a.x - b.x) | 0) + absInt32((a.y - b.y) | 0)) | 0;
  }

  static plan(title: string, x: number, y: number): TodoItem {
    return new TodoItem(title, Priority.Low, new Point(x, y));
  }
}

// fixtures/no-runtime-questions/index.ts
export { Counter } from "./counter";
export { Geometry } from "./geometry";
export { Point } from "./point";
export { Priority } from "./priority";
export { TodoItem } from "./todo-item";

// fixtures/no-runtime-questions/point.ts
import { HashCode, absInt32, copyRecordWith } from "metano-runtime";

export class Point {
  constructor(readonly x: number, readonly y: number) { }

  translate(dx: number, dy: number): Point {
    return this.with({ x: (this.x + dx) | 0, y: (this.y + dy) | 0 });
  }

  manhattanLength(): number {
    return (absInt32(this.x) + absInt32(this.y)) | 0;
  }

  equals(other: any): boolean {
    return other != null && Object.getPrototypeOf(other) === Object.getPrototypeOf(this) && this.x === other.x && this.y === other.y;
  }

  hashCode(): number {
    const hc = new HashCode();
    hc.add(this.x);
    hc.add(this.y);

    return hc.toHashCode();
  }

  with(overrides?: Partial<Point>): Point {
    return copyRecordWith<Point>(this, overrides);
  }
}

// fixtures/no-runtime-questions/priority.ts
export const Priority = {
  Low: "Low",
  Medium: "Medium",
  High: "High",
} as const;

export type Priority = typeof Priority[keyof typeof Priority];

// fixtures/no-runtime-questions/todo-item.ts
import { HashCode, copyRecordWith, valueEquals } from "metano-runtime";
import type { Point } from "./point";
import { Priority } from "./priority";

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
