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
