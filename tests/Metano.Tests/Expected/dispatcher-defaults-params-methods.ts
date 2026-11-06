import { isBool, isInt32, isString } from "metano-runtime";

export class Ops {
  constructor() { }

  mA(a: number = 5): string {
    return "int:" + a;
  }

  mSRest(s: string, ...rest: number[]): string {
    return "params:" + s + ":" + rest.length;
  }

  m(a?: number): string;
  m(s: string, ...rest: number[]): string;
  m(...args: unknown[]): string {
    if (args.length <= 1 && (args[0] === undefined || isInt32(args[0]))) {
      return this.mA(args[0] as number);
    }

    if (args.length >= 1 && isString(args[0]) && args.slice(1).every((value) => isInt32(value))) {
      return this.mSRest(args[0] as string, ...args.slice(1) as number[]);
    }

    throw new Error("No matching overload for m");
  }

  static sFlag(flag: boolean): string {
    return "bool";
  }

  static sAB(a: number = 5, b: string = "x"): string {
    return "S:" + a + b;
  }

  static s(flag: boolean): string;
  static s(a?: number, b?: string): string;
  static s(...args: unknown[]): string {
    if (args.length === 1 && isBool(args[0])) {
      return Ops.sFlag(args[0] as boolean);
    }

    if (args.length <= 2 && (args[0] === undefined || isInt32(args[0])) && (args[1] === undefined || isString(args[1]))) {
      return Ops.sAB(args[0] as number, args[1] as string);
    }

    throw new Error("No matching overload for s");
  }
}
