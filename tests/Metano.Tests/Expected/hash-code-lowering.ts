import { valueEquals, valueHashCode } from "metano-runtime";
import type { Color } from "./color";
import type { Point } from "./point";

export class Money {
  cents: number = 0;

  constructor() { }

  equals(other: Object | null): boolean {
    return other instanceof Money && other.cents === this.cents;
  }

  hashCode(): number {
    return valueHashCode(this.cents) ^ 0;
  }

  hashes(p: Point, s: string, i: number, c: Color, o: Object, n: number | null): number {
    return valueHashCode(p) + valueHashCode(s) + valueHashCode(i) + valueHashCode(c) + valueHashCode(o) + valueHashCode(n) + valueHashCode(this) + valueHashCode(this);
  }

  same(a: Money | null, b: Money | null): boolean {
    return valueEquals(a, b) && !(a === b);
  }
}
