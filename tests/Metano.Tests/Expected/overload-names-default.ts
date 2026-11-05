import { isInt32 } from "metano-runtime";

export class Clock {
  constructor() { }

  private nowTick(tick: number): number {
    return tick;
  }

  private nowDefault(): number {
    return 0;
  }

  now(tick: number): number;
  now(): number;
  now(...args: unknown[]): number {
    if (args.length === 1 && isInt32(args[0])) {
      return this.nowTick(args[0] as number);
    }

    if (args.length === 0) {
      return this.nowDefault();
    }

    throw new Error("No matching overload for now");
  }
}
