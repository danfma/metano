import { isInt32, isString } from "metano-runtime";

export class Opt {
  readonly log: string = "";

  constructor(a?: number);
  constructor(s: string, ...rest: number[]);
  constructor(...args: unknown[]) {
    if (args.length <= 1 && (args[0] === undefined || isInt32(args[0]))) {
      const a = (args[0] === undefined ? 5 : args[0]) as number;
      this.log = "int:" + a;

      return;
    }

    if (args.length >= 1 && isString(args[0]) && args.slice(1).every((value) => isInt32(value))) {
      const s = args[0] as string;
      const rest = args.slice(1) as number[];
      this.log = "params:" + s + ":" + rest.length;

      return;
    }

    throw new Error("No matching constructor");
  }
}
