import { isInt32, isString } from "metano-runtime";

export class Opt {
  protected static readonly byOptA: unique symbol = Symbol("Opt(int a)");

  protected static readonly byOptSRest: unique symbol = Symbol("Opt(string s, params int[] rest)");

  readonly log: string = "";

  constructor(overload: typeof Opt.byOptA, a?: number);
  constructor(overload: typeof Opt.byOptSRest, s: string, ...rest: number[]);
  constructor(a?: number);
  constructor(s: string, ...rest: number[]);
  constructor(...args: unknown[]) {
    if (typeof args[0] === "symbol" && args[0] === Opt.byOptA) {
      const a = (args[1] === undefined ? 5 : args[1]) as number;
      this.log = "int:" + a;

      return;
    }

    if (typeof args[0] === "symbol" && args[0] === Opt.byOptSRest) {
      const s = args[1] as string;
      const rest = args.slice(2) as number[];
      this.log = "params:" + s + ":" + rest.length;

      return;
    }

    if (args.length <= 1 && (args[0] === undefined || isInt32(args[0]))) {
      const a = (args[0] === undefined ? 5 : args[0]) as number;
      this.log = "int:" + a;

      return;
    }

    if (args.length >= 1 && isString(args[0]) && args.slice(1).every((value) => isInt32(value))) {
      const s = args[0] as string;
      const rest = args.slice(1) as number[];
      this.log = "params:" + s + ":" + rest.length;

      return;
    }

    throw new Error("No matching constructor");
  }

  static fromA(a: number = 5): Opt {
    return new Opt(Opt.byOptA, a);
  }

  static fromSRest(s: string, ...rest: number[]): Opt {
    return new Opt(Opt.byOptSRest, s, ...rest);
  }
}
