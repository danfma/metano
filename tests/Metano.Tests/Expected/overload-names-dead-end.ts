import { isInt32 } from "metano-runtime";

export class Generic {
  constructor() { }

  private mInt32$1<T>(x: number): number {
    return 1;
  }

  private mInt32$2(x: number): number {
    return 2;
  }

  m<T>(x: number): number;
  m(x: number): number;
  m(...args: unknown[]): any {
    if (args.length === 1 && isInt32(args[0])) {
      return this.mInt32$1(args[0] as any);
    }

    if (args.length === 1 && isInt32(args[0])) {
      return this.mInt32$2(args[0] as number);
    }

    throw new Error("No matching overload for m");
  }
}
