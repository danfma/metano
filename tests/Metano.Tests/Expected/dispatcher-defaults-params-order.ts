import { isInt32, isString } from "metano-runtime";

export class Over {
  constructor() { }

  fA(a: number): string {
    return "a";
  }

  fAB(a: number, b: number = 7): string {
    return "ab" + b;
  }

  f(a: number): string;
  f(a: number, b?: number): string;
  f(...args: unknown[]): string {
    if (args.length === 1 && isInt32(args[0])) {
      return this.fA(args[0] as number);
    }

    if (args.length >= 1 && args.length <= 2 && isInt32(args[0]) && (args[1] === undefined || isInt32(args[1]))) {
      return this.fAB(args[0] as number, args[1] as number);
    }

    throw new Error("No matching overload for f");
  }

  manyAB(a: string, b: string): string {
    return "two";
  }

  manyXs(...xs: string[]): string {
    return "many" + xs.length;
  }

  many(a: string, b: string): string;
  many(...xs: string[]): string;
  many(...args: unknown[]): string {
    if (args.length === 2 && isString(args[0]) && isString(args[1])) {
      return this.manyAB(args[0] as string, args[1] as string);
    }

    if (args.slice(0).every((value) => isString(value))) {
      return this.manyXs(...args.slice(0) as string[]);
    }

    throw new Error("No matching overload for many");
  }
}
