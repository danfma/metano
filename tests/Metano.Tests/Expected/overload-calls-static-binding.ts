import { isString } from "metano-runtime";

export class Binder {
  constructor() { }

  fObject(a: Object): string {
    return "object";
  }

  fString(a: string): string {
    return "string";
  }

  f(a: Object): string;
  f(a: string): string;
  f(...args: unknown[]): string {
    if (args.length === 1 && typeof args[0] === "object") {
      return this.fObject(args[0] as Object);
    }

    if (args.length === 1 && isString(args[0])) {
      return this.fString(args[0] as string);
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
