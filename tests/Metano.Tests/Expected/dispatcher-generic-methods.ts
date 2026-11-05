import { isInt32, isString } from "metano-runtime";

export class Holder<T> {
  constructor() { }

  private putString(x: string): string {
    return "string";
  }

  private putT(x: T): string {
    return "T";
  }

  put(x: string): string;
  put(x: T): string;
  put(...args: unknown[]): string {
    if (args.length === 1 && isString(args[0])) {
      return this.putString(args[0] as string);
    }

    if (args.length === 1) {
      return this.putT(args[0] as T);
    }

    throw new Error("No matching overload for put");
  }

  private refN(n: number): string {
    return "int";
  }

  private refU<U>(u: U): string {
    return "U";
  }

  ref(n: number): string;
  ref<U>(u: U): string;
  ref(...args: unknown[]): any {
    if (args.length === 1 && isInt32(args[0])) {
      return this.refN(args[0] as number);
    }

    if (args.length === 1 && (typeof args[0] === "object" || typeof args[0] === "function" || typeof args[0] === "string")) {
      return this.refU(args[0] as any);
    }

    throw new Error("No matching overload for ref");
  }

  private optAB(a: number, b: number): string {
    return "ab";
  }

  private optX(x: T | null = null): string {
    return "opt";
  }

  opt(a: number, b: number): string;
  opt(x?: T | null): string;
  opt(...args: unknown[]): string {
    if (args.length === 2 && isInt32(args[0]) && isInt32(args[1])) {
      return this.optAB(args[0] as number, args[1] as number);
    }

    if (args.length <= 1) {
      return this.optX(args[0] as T | null);
    }

    throw new Error("No matching overload for opt");
  }
}
