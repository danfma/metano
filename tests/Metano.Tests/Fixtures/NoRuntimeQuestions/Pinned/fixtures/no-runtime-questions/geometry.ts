import { Point } from "./point.js";
import { Priority } from "./priority.js";
import { TodoItem } from "./todo-item.js";

export class Geometry {
  constructor() { }

  static distance(a: Point, b: Point): number {
    return (Math.abs((a.x - b.x) | 0) + Math.abs((a.y - b.y) | 0)) | 0;
  }

  static plan(title: string, x: number, y: number): TodoItem {
    return new TodoItem(title, Priority.Low, new Point(x, y));
  }
}
