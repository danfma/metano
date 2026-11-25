// fixtures/no-runtime-questions/counter.ts
export class Counter {
  declare private _count: number;

  constructor() {
    this._count = 0;
  }

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
import { Point } from "./point.js";
import { Priority } from "./priority.js";
import { TodoItem } from "./todo-item.js";

export class Geometry {
  private constructor() { }

  static distance(a: Point, b: Point): number {
    return (absInt32((a.x - b.x) | 0) + absInt32((a.y - b.y) | 0)) | 0;
  }

  static plan(title: string, x: number, y: number): TodoItem {
    return new TodoItem(title, Priority.Low, new Point(x, y));
  }
}

// fixtures/no-runtime-questions/index.ts
export { Counter } from "./counter.js";
export { Geometry } from "./geometry.js";
export { Point } from "./point.js";
export { Priority } from "./priority.js";
export { TodoItem } from "./todo-item.js";

// fixtures/no-runtime-questions/point.ts
import { HashCode, absInt32, copyRecordWith } from "metano-runtime";

export class Point {
  declare readonly x: number;
  declare readonly y: number;

  constructor(x: number, y: number) {
    this.x = x;
    this.y = y;
  }

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

  protected $printMembers(): string {
    return `X = ${this.x}, Y = ${this.y}`;
  }

  toString(): string {
    return `Point { ${this.$printMembers()} }`;
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
import { HashCode, copyRecordWith, formatBoolean, valueEquals } from "metano-runtime";
import type { Point } from "./point.js";
import { Priority } from "./priority.js";

export class TodoItem {
  declare readonly title: string;
  declare readonly priority: Priority;
  declare readonly location: Point;

  constructor(title: string, priority: Priority, location: Point) {
    this.title = title;
    this.priority = priority;
    this.location = location;
  }

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

  protected $printMembers(): string {
    return `Title = ${this.title ?? ""}, Priority = ${this.priority}, Location = ${this.location ?? ""}, IsUrgent = ${formatBoolean(this.isUrgent)}`;
  }

  toString(): string {
    return `TodoItem { ${this.$printMembers()} }`;
  }
}
