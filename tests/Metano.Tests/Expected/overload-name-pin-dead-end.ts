import { isBool, isInt32, isString } from "metano-runtime";

export class Box {
  constructor() { }

  fInt32$1(a: number): number {
    return a;
  }

  fString(a: string): number {
    return 0;
  }

  fInt32$2(b: boolean): number {
    return 1;
  }

  f(a: number): number;
  f(a: string): number;
  f(b: boolean): number;
  f(...args: unknown[]): number {
    if (args.length === 1 && isInt32(args[0])) {
      return this.fInt32$1(args[0] as number);
    }

    if (args.length === 1 && isString(args[0])) {
      return this.fString(args[0] as string);
    }

    if (args.length === 1 && isBool(args[0])) {
      return this.fInt32$2(args[0] as boolean);
    }

    throw new Error("No matching overload for f");
  }
}
