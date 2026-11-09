import { isInt32, isString } from "metano-runtime";

export class Box {
  constructor() { }

  putValue$1(a: number): number {
    return a;
  }

  putValue$2(b: string): number {
    return 0;
  }

  put(a: number): number;
  put(b: string): number;
  put(...args: unknown[]): number {
    if (args.length === 1 && isInt32(args[0])) {
      return this.putValue$1(args[0] as number);
    }

    if (args.length === 1 && isString(args[0])) {
      return this.putValue$2(args[0] as string);
    }

    throw new Error("No matching overload for put");
  }
}
