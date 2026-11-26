import { isInt32 } from "metano-runtime";

export class Dimension {
  protected static readonly byDimensionSize = /* @__PURE__ */ Symbol("Dimension(int size)");
  protected static readonly byDimensionWidthHeight = /* @__PURE__ */ Symbol("Dimension(int width, int height)");
  declare readonly size: number;
  declare readonly width: number;
  declare readonly height: number;

  constructor(overload: typeof Dimension.byDimensionSize, size: number);
  constructor(overload: typeof Dimension.byDimensionWidthHeight, width: number, height: number);
  constructor(width: number, height: number);
  constructor(size: number);
  constructor(...args: unknown[]) {
    if (typeof args[0] === "symbol" && args[0] === Dimension.byDimensionSize) {
      const size = args[1] as number;
      this.size = 0;
      this.width = 0;
      this.height = 0;
      this.size = size;

      return;
    }

    if (typeof args[0] === "symbol" && args[0] === Dimension.byDimensionWidthHeight) {
      const width = args[1] as number;
      const height = args[2] as number;
      this.size = 0;
      this.width = 0;
      this.height = 0;
      this.width = width;
      this.height = height;

      return;
    }

    if (args.length === 2 && isInt32(args[0]) && isInt32(args[1])) {
      const width = args[0] as number;
      const height = args[1] as number;
      this.size = 0;
      this.width = 0;
      this.height = 0;
      this.width = width;
      this.height = height;

      return;
    }

    if (args.length === 1 && isInt32(args[0])) {
      const size = args[0] as number;
      this.size = 0;
      this.width = 0;
      this.height = 0;
      this.size = size;

      return;
    }

    throw new Error("No matching constructor");
  }

  static fromSize(size: number): Dimension {
    return new Dimension(Dimension.byDimensionSize, size);
  }

  static fromWidthHeight(width: number, height: number): Dimension {
    return new Dimension(Dimension.byDimensionWidthHeight, width, height);
  }
}
