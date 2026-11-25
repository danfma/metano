/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { Circle } from "./circle.js";
import type { Shape } from "./shape.js";
import { Square } from "./square.js";

export class ShapeFactory {
  private constructor() {}

  static create(kind: string): Shape {
    return kind === "circle" ? new Circle() : new Square();
  }
}
