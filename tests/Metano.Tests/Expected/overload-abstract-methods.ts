import { isBool, isInt32, isString } from "metano-runtime";

export abstract class Op {
  constructor() { }

  abstract runX(x: number): number;

  abstract runS(s: string): number;

  runFlag(flag: boolean): number {
    return flag ? this.runX(1) : this.runS("ab");
  }

  run(x: number): number;
  run(s: string): number;
  run(flag: boolean): number;
  run(...args: unknown[]): number {
    if (args.length === 1 && isInt32(args[0])) {
      return this.runX(args[0] as number);
    }

    if (args.length === 1 && isString(args[0])) {
      return this.runS(args[0] as string);
    }

    if (args.length === 1 && isBool(args[0])) {
      return this.runFlag(args[0] as boolean);
    }

    throw new Error("No matching overload for run");
  }
}

import { Op } from "./op";

export class Neg extends Op {
  constructor() {
    super();
  }

  override runX(x: number): number {
    return -x;
  }

  override runS(s: string): number {
    return -s.length;
  }
}
