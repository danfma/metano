import { objectEquals, valueHashCode } from "metano-runtime";
import type { Color } from "./color.js";
import type { Point } from "./point.js";

export class Money {
  declare cents: number;

  constructor() {
    this.cents = 0;
  }

  equals(other: Object | null): boolean {
    return other instanceof Money && other.cents === this.cents;
  }

  hashCode(): number {
    return valueHashCode(this.cents) ^ 0;
  }

  hashes(p: Point, s: string, i: number, c: Color, o: Object, n: number | null): number {
    return (valueHashCode(p) + valueHashCode(s) + valueHashCode(i) + valueHashCode(c) + valueHashCode(o) + valueHashCode(n) + valueHashCode(this) + valueHashCode(this)) | 0;
  }

  same(a: Money | null, b: Money | null): boolean {
    return objectEquals(a, b) && !(a === b);
  }
}
