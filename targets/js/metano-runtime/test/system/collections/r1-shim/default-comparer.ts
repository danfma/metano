// TEMPORARY test-only stand-in for R1's `default-comparer.ts` (see preload.ts).
import { defaultEquals } from "../../../../src/system/equality.ts";

export type Comparison<T> = (a: T, b: T) => number;
export interface IEqualityComparer<T> {
  equals(a: T, b: T): boolean;
  getHashCode(value: T): number;
}
export interface IComparer<T> {
  compare(a: T, b: T): number;
}

export function elementEquals(a: unknown, b: unknown): boolean {
  return Array.isArray(a) || Array.isArray(b) ? a === b : defaultEquals(a, b);
}

export function elementHashCode(value: unknown): number {
  return typeof value === "number" ? value | 0 : 0;
}

export function defaultCompare(a: unknown, b: unknown): number {
  if (a === b) {
    return 0;
  }

  if (a === null || a === undefined) {
    return -1;
  }

  if (b === null || b === undefined) {
    return 1;
  }

  if (typeof a === "number" && typeof b === "number") {
    if (Number.isNaN(a)) {
      return Number.isNaN(b) ? 0 : -1;
    }

    return Number.isNaN(b) ? 1 : a < b ? -1 : a > b ? 1 : 0;
  }

  if (typeof a === typeof b && (typeof a === "string" || typeof a === "bigint" || typeof a === "boolean")) {
    return (a as string) < (b as string) ? -1 : 1;
  }

  if (typeof (a as { compareTo?: unknown }).compareTo === "function") {
    return (a as { compareTo(other: unknown): number }).compareTo(b);
  }

  throw Object.assign(new Error("At least one object must implement IComparable."), { name: "ArgumentException" });
}

export const defaultEqualityComparer: IEqualityComparer<unknown> = { equals: elementEquals, getHashCode: elementHashCode };
export const defaultComparer: IComparer<unknown> = { compare: defaultCompare };

export function createComparer<T>(comparison: Comparison<T>): IComparer<T> {
  return { compare: comparison };
}
