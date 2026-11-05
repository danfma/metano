import { isInt32 } from "metano-runtime";

export class Temp {
  protected static readonly byTempA: unique symbol = Symbol("Temp(int a)");

  protected static readonly byTempDefault: unique symbol = Symbol("Temp()");

  readonly a: number = 0;

  constructor(overload: typeof Temp.byTempA, a: number);
  constructor(overload: typeof Temp.byTempDefault);
  constructor(a: number);
  constructor();
  constructor(...args: unknown[]) {
    if (typeof args[0] === "symbol" && args[0] === Temp.byTempA) {
      const a = args[1] as number;
      this.a = a;

      return;
    }

    if (typeof args[0] === "symbol" && args[0] === Temp.byTempDefault) {
      const __chain0Arg0 = 42;

      {
        const a = __chain0Arg0;
        this.a = a;
      }

      return;
    }

    if (args.length === 1 && isInt32(args[0])) {
      const a = args[0] as number;
      this.a = a;

      return;
    }

    if (args.length === 0) {
      const __chain0Arg0 = 42;

      {
        const a = __chain0Arg0;
        this.a = a;
      }

      return;
    }

    throw new Error("No matching constructor");
  }

  static fromA(a: number): Temp {
    return new Temp(Temp.byTempA, a);
  }

  static fromDefault(): Temp {
    return new Temp(Temp.byTempDefault);
  }
}
