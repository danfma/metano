import { isInt32 } from "metano-runtime";

export class Mixed<T> {
  protected static readonly byMixedT = /* @__PURE__ */ Symbol("Mixed(T x)");
  protected static readonly byMixedInt32 = /* @__PURE__ */ Symbol("Mixed(int x)");
  readonly log: string = "";

  constructor(overload: typeof Mixed.byMixedT, x: T);
  constructor(overload: typeof Mixed.byMixedInt32, x: number);
  constructor(x: number);
  constructor(x: T);
  constructor(...args: unknown[]) {
    if (typeof args[0] === "symbol" && args[0] === Mixed.byMixedT) {
      this.log = "T";

      return;
    }

    if (typeof args[0] === "symbol" && args[0] === Mixed.byMixedInt32) {
      this.log = "int";

      return;
    }

    if (args.length === 1 && isInt32(args[0])) {
      this.log = "int";

      return;
    }

    if (args.length === 1) {
      this.log = "T";

      return;
    }

    throw new Error("No matching constructor");
  }

  static fromT<T>(x: T): Mixed<T> {
    return new Mixed(Mixed.byMixedT, x);
  }

  static fromInt32<T>(x: number): Mixed<T> {
    return new Mixed(Mixed.byMixedInt32, x);
  }
}
