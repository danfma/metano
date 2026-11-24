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
