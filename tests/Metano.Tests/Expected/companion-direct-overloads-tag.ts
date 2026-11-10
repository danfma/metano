import { isInt32, isInt64, isString } from "metano-runtime";
import { UId } from "./u-id";

export type Tag = string & { readonly __brand: "Tag" };

export namespace Tag {
  export function create(value: string): Tag {
    return value as Tag;
  }

  export function sL(l: bigint): string {
    return "long";
  }

  export function sU(u: UId): string {
    return "uid";
  }

  export function s(l: bigint): string;
  export function s(u: UId): string;
  export function s(...args: unknown[]): string {
    if (args.length === 1 && isInt64(args[0])) {
      return sL(args[0] as bigint);
    }

    if (args.length === 1 && typeof args[0] === "bigint") {
      return sU(args[0] as UId);
    }

    throw new Error("No matching overload for s");
  }

  export function anyValue<T>(value: T): string {
    return "any";
  }

  export function anyAB(a: number, b: number = 0): string {
    return "int";
  }

  export function any<T>(value: T): string;
  export function any(a: number, b?: number): string;
  export function any(...args: unknown[]): any {
    if (args.length === 1) {
      return anyValue(args[0] as any);
    }

    if (args.length >= 1 && args.length <= 2 && isInt32(args[0]) && (args[1] === undefined || isInt32(args[1]))) {
      return anyAB(args[0] as number, args[1] as number);
    }

    throw new Error("No matching overload for any");
  }

  function nS(s: string): string {
    return "string";
  }

  function nI(i: number): string {
    return "int";
  }

  export function n(s: string): string;
  export function n(i: number): string;
  export function n(...args: unknown[]): string {
    if (args.length === 1 && isString(args[0])) {
      return nS(args[0] as string);
    }

    if (args.length === 1 && isInt32(args[0])) {
      return nI(args[0] as number);
    }

    throw new Error("No matching overload for n");
  }

  export function both(): string {
    return Tag.sU(UId.create(2n)) + Tag.sL(2n);
  }
}
