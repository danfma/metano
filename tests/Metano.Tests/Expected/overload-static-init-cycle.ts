import { B } from "./b.js";

export class A {
  static readonly seed = /* @__PURE__ */ B.fromV(1) as B;

  read(b: B): number {
    return b.v;
  }
}

import { isInt32, isString } from "metano-runtime";
import { A } from "./a.js";

export class B {
  protected static readonly byBV = /* @__PURE__ */ Symbol("B(int v)");
  protected static readonly byBText = /* @__PURE__ */ Symbol("B(string text)");
  declare readonly v: number;

  constructor(overload: typeof B.byBV, v: number);
  constructor(overload: typeof B.byBText, text: string);
  constructor(v: number);
  constructor(text: string);
  constructor(...args: unknown[]) {
    if (typeof args[0] === "symbol" && args[0] === B.byBV) {
      const v = args[1] as number;
      this.v = 0;
      this.v = v;

      return;
    }

    if (typeof args[0] === "symbol" && args[0] === B.byBText) {
      const text = args[1] as string;
      this.v = 0;
      this.v = text.length;

      return;
    }

    if (args.length === 1 && isInt32(args[0])) {
      const v = args[0] as number;
      this.v = 0;
      this.v = v;

      return;
    }

    if (args.length === 1 && isString(args[0])) {
      const text = args[0] as string;
      this.v = 0;
      this.v = text.length;

      return;
    }

    throw new Error("No matching constructor");
  }

  static fromV(v: number): B {
    return new B(B.byBV, v);
  }

  static fromText(text: string): B {
    return new B(B.byBText, text);
  }

  twice(): number {
    return Math.imul(new A().read(this), 2);
  }
}
