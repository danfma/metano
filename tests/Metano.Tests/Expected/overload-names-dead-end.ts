import { isInt32 } from "metano-runtime";

export class Generic {
  constructor() { }

  mInt32$1<T>(x: number): number {
    return 1;
  }

  mInt32$2(x: number): number {
    return 2;
  }

  m<T>(x: number): number;
  m(...args: unknown[]): any {
    if (args.length === 1 && isInt32(args[0])) {
      return this.mInt32$1(args[0] as any);
    }

    throw new Error("No matching overload for m");
  }
}
