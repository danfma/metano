import { isInt32, isInt64, isString } from "metano-runtime";
import type { UId } from "./u-id";

export type Key = string & { readonly __brand: "Key" };

export namespace Key {
  export function create(value: string): Key {
    return value as Key;
  }

  export function xL$(l: bigint): string {
    return "x-long";
  }

  export function xU(u: UId): string {
    return "x-uid";
  }

  export function x(l: bigint): string;
  export function x(u: UId): string;
  export function x(...args: unknown[]): string {
    if (args.length === 1 && isInt64(args[0])) {
      return xL$(args[0] as bigint);
    }

    if (args.length === 1 && typeof args[0] === "bigint") {
      return xU(args[0] as UId);
    }

    throw new Error("No matching overload for x");
  }

  export function xL(s: string): string {
    return "xl";
  }

  export function pinViaLong(l: bigint): string {
    return "long";
  }

  export function pinU(u: UId): string {
    return "uid";
  }

  export function pin(l: bigint): string;
  export function pin(u: UId): string;
  export function pin(...args: unknown[]): string {
    if (args.length === 1 && isInt64(args[0])) {
      return pinViaLong(args[0] as bigint);
    }

    if (args.length === 1 && typeof args[0] === "bigint") {
      return pinU(args[0] as UId);
    }

    throw new Error("No matching overload for pin");
  }

  export function vL(l: bigint): string {
    return "long";
  }

  /** @internal */
  export function vU(u: UId): string {
    return "internal";
  }

  /** @internal */
  export function vS(s: string): string {
    return "private";
  }

  export function v(l: bigint): string;
  /** @internal */
  export function v(u: UId): string;
  /** @internal */
  export function v(s: string): string;
  export function v(...args: unknown[]): string {
    if (args.length === 1 && isInt64(args[0])) {
      return vL(args[0] as bigint);
    }

    if (args.length === 1 && typeof args[0] === "bigint") {
      return vU(args[0] as UId);
    }

    if (args.length === 1 && isString(args[0])) {
      return vS(args[0] as string);
    }

    throw new Error("No matching overload for v");
  }

  export function hidden(): string {
    return Key.vS("s");
  }

  export function dLN(l: bigint, n: number = 3): string {
    return "long" + n;
  }

  export function dUN(u: UId, n: number = 5): string {
    return "uid" + n;
  }

  export function d(l: bigint, n?: number): string;
  export function d(u: UId, n?: number): string;
  export function d(...args: unknown[]): string {
    if (args.length >= 1 && args.length <= 2 && isInt64(args[0]) && (args[1] === undefined || isInt32(args[1]))) {
      return dLN(args[0] as bigint, args[1] as number);
    }

    if (args.length >= 1 && args.length <= 2 && typeof args[0] === "bigint" && (args[1] === undefined || isInt32(args[1]))) {
      return dUN(args[0] as UId, args[1] as number);
    }

    throw new Error("No matching overload for d");
  }
}
