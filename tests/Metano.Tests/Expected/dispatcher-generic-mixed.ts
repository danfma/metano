import { isInt32 } from "metano-runtime";

export class Mixed<T> {
  readonly log: string = "";

  constructor(x: number);
  constructor(x: T);
  constructor(...args: unknown[]) {
    if (args.length === 1 && isInt32(args[0])) {
      const x = args[0] as number;
      this.log = "int";

      return;
    }

    if (args.length === 1) {
      const x = args[0] as T;
      this.log = "T";

      return;
    }

    throw new Error("No matching constructor");
  }
}
