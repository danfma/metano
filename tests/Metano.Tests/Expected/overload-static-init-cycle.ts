import { B } from "./b";

export class A {
  static readonly seed: B = B.fromV(1);

  constructor() { }

  read(b: B): number {
    return b.v;
  }
}

import { isInt32, isString } from "metano-runtime";
import { A } from "./a";

export class B {
  protected static readonly byBV: unique symbol = Symbol("B(int v)");

  protected static readonly byBText: unique symbol = Symbol("B(string text)");

  readonly v: number = 0;

  constructor(overload: typeof B.byBV, v: number);
  constructor(overload: typeof B.byBText, text: string);
  constructor(v: number);
  constructor(text: string);
  constructor(...args: unknown[]) {
    if (typeof args[0] === "symbol" && args[0] === B.byBV) {
      const v = args[1] as number;
      this.v = v;

      return;
    }

    if (typeof args[0] === "symbol" && args[0] === B.byBText) {
      const text = args[1] as string;
      this.v = text.length;

      return;
    }

    if (args.length === 1 && isInt32(args[0])) {
      const v = args[0] as number;
      this.v = v;

      return;
    }

    if (args.length === 1 && isString(args[0])) {
      const text = args[0] as string;
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
    return new A().read(this) * 2;
  }
}
