import { isInt32 } from "metano-runtime";

export class Bag<T> {
  readonly log: string = "";

  constructor(item: T, count: number);
  constructor(item: T);
  constructor(...args: unknown[]) {
    if (args.length === 2 && isInt32(args[1])) {
      const item = args[0] as T;
      const count = args[1] as number;
      this.log = "count";

      return;
    }

    if (args.length === 1) {
      const item = args[0] as T;
      this.log = "item";

      return;
    }

    throw new Error("No matching constructor");
  }
}
