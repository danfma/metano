import { isString } from "metano-runtime";

export class Binder {
  constructor() { }

  fString(a: string): string {
    return "string";
  }

  fObject(a: Object): string {
    return "object";
  }

  f(a: string): string;
  f(a: Object): string;
  f(...args: unknown[]): string {
    if (args.length === 1 && isString(args[0])) {
      return this.fString(args[0] as string);
    }

    if (args.length === 1) {
      return this.fObject(args[0] as Object);
    }

    throw new Error("No matching overload for f");
  }

  pick(): string {
    const o = "s";

    return this.fObject(o);
  }

  direct(): string {
    return this.fString("s");
  }
}
