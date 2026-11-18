/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { Circle } from "./circle";
import type { Shape } from "./shape";
import { Square } from "./square";

export class ShapeFactory {
  constructor() {}

  static create(kind: string): Shape {
    return kind === "circle" ? new Circle() : new Square();
  }
}
