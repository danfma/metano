import { isBool, isChar, isFloat64, isInt32, isString } from "metano-runtime";

export class Box {
  constructor() { }

  putX(x: number): number {
    return x;
  }

  putS(s: string): number {
    return 0;
  }

  putB(b: boolean): number {
    return 1;
  }

  putD(d: number): number {
    return 2;
  }

  putC(c: string): number {
    return 3;
  }

  put(x: number): number;
  put(s: string): number;
  put(b: boolean): number;
  put(d: number): number;
  put(c: string): number;
  put(...args: unknown[]): number {
    if (args.length === 1 && isInt32(args[0])) {
      return this.putX(args[0] as number);
    }

    if (args.length === 1 && isString(args[0])) {
      return this.putS(args[0] as string);
    }

    if (args.length === 1 && isBool(args[0])) {
      return this.putB(args[0] as boolean);
    }

    if (args.length === 1 && isFloat64(args[0])) {
      return this.putD(args[0] as number);
    }

    if (args.length === 1 && isChar(args[0])) {
      return this.putC(args[0] as string);
    }

    throw new Error("No matching overload for put");
  }
}
