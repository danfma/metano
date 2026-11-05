import { isInt32, isString } from "metano-runtime";

export abstract class Shape {
  protected static readonly byShapeScale: unique symbol = Symbol("Shape(int scale)");

  protected static readonly byShapeName: unique symbol = Symbol("Shape(string name)");

  readonly scale: number = 0;

  protected constructor(overload: typeof Shape.byShapeScale, scale: number);
  protected constructor(overload: typeof Shape.byShapeName, name: string);
  protected constructor(scale: number);
  protected constructor(name: string);
  protected constructor(...args: unknown[]) {
    if (typeof args[0] === "symbol" && args[0] === Shape.byShapeScale) {
      const scale = args[1] as number;
      this.scale = scale;

      return;
    }

    if (typeof args[0] === "symbol" && args[0] === Shape.byShapeName) {
      const name = args[1] as string;
      this.scale = name.length;

      return;
    }

    if (args.length === 1 && isInt32(args[0])) {
      const scale = args[0] as number;
      this.scale = scale;

      return;
    }

    if (args.length === 1 && isString(args[0])) {
      const name = args[0] as string;
      this.scale = name.length;

      return;
    }

    throw new Error("No matching constructor");
  }
}

import { Shape } from "./shape";

export class Circle extends Shape {
  constructor(radius: number) {
    super(radius);
  }
}
