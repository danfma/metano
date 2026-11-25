import { isInt32, isType } from "metano-runtime";
import { IRank$runtimeType, type IRank } from "./i-rank.js";

export class Box<T extends IRank<T>> {
  protected static readonly byBoxValue = /* @__PURE__ */ Symbol("Box(T value)");
  protected static readonly byBoxN = /* @__PURE__ */ Symbol("Box(int n)");
  declare readonly value: T | null;
  declare readonly n: number;

  constructor(overload: typeof Box.byBoxValue, value: T);
  constructor(overload: typeof Box.byBoxN, n: number);
  constructor(n: number);
  constructor(value: T);
  constructor(...args: unknown[]) {
    if (typeof args[0] === "symbol" && args[0] === Box.byBoxValue) {
      const value = args[1] as T;
      this.value = null;
      this.n = 0;
      this.value = value;

      return;
    }

    if (typeof args[0] === "symbol" && args[0] === Box.byBoxN) {
      const n = args[1] as number;
      this.value = null;
      this.n = 0;
      this.n = n;

      return;
    }

    if (args.length === 1 && isInt32(args[0])) {
      const n = args[0] as number;
      this.value = null;
      this.n = 0;
      this.n = n;

      return;
    }

    if (args.length === 1 && isType(args[0], IRank$runtimeType)) {
      const value = args[0] as T;
      this.value = null;
      this.n = 0;
      this.value = value;

      return;
    }

    throw new Error("No matching constructor");
  }

  static fromValue<T extends IRank<T>>(value: T): Box<T> {
    return new Box(Box.byBoxValue, value);
  }

  static fromN<T extends IRank<T>>(n: number): Box<T> {
    return new Box(Box.byBoxN, n);
  }
}

import { isInt32, isType } from "metano-runtime";
import { Box } from "./box.js";
import { IRank$runtimeType, type IRank } from "./i-rank.js";

export class RankedBox<T extends IRank<T>> extends Box<T> {
  protected static readonly byRankedBoxValue = /* @__PURE__ */ Symbol("RankedBox(T value)");
  protected static readonly byRankedBoxN = /* @__PURE__ */ Symbol("RankedBox(int n)");

  constructor(overload: typeof RankedBox.byRankedBoxValue, value: T);
  constructor(overload: typeof RankedBox.byRankedBoxN, n: number);
  constructor(n: number);
  constructor(value: T);
  constructor(...args: unknown[]) {
    if (typeof args[0] === "symbol" && args[0] === RankedBox.byRankedBoxValue) {
      const value = args[1] as T;
      super(Box.byBoxValue, value);

      return;
    }

    if (typeof args[0] === "symbol" && args[0] === RankedBox.byRankedBoxN) {
      const n = args[1] as number;
      super(Box.byBoxN, n);

      return;
    }

    if (args.length === 1 && isInt32(args[0])) {
      const n = args[0] as number;
      super(Box.byBoxN, n);

      return;
    }

    if (args.length === 1 && isType(args[0], IRank$runtimeType)) {
      const value = args[0] as T;
      super(Box.byBoxValue, value);

      return;
    }

    throw new Error("No matching constructor");
  }

  static override fromValue<T extends IRank<T>>(value: T): RankedBox<T> {
    return new RankedBox(RankedBox.byRankedBoxValue, value);
  }

  static override fromN<T extends IRank<T>>(n: number): RankedBox<T> {
    return new RankedBox(RankedBox.byRankedBoxN, n);
  }
}
