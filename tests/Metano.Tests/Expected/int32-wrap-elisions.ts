import { HashSet } from "metano-runtime";
import type { Bag } from "./bag";

export class Elisions {
  constructor() { }

  lastIndex(a: number[]): number {
    return a.length - 1;
  }

  textLast(s: string): number {
    return s.length - 1;
  }

  listLast(l: number[]): number {
    return l.length - 1;
  }

  dictionaryLast(d: Map<string, number>): number {
    return d.size - 1;
  }

  setDouble(s: HashSet<number>): number {
    return Math.imul(s.size, 2);
  }

  listNext(l: number[]): number {
    return (l.length + 1) | 0;
  }

  userCount(b: Bag): number {
    return (b.count - 1) | 0;
  }

  interface(c: number[]): number {
    return (c.length - 1) | 0;
  }

  masked(x: number): number {
    return (x & 255) + 1;
  }

  bytes(a: number, b: number): number {
    return a + b;
  }

  up(a: number[]): number {
    let s = 0;
    for (let i = 0; i < a.length; i++) {
      s = (s + a[i]!) | 0;
    }

    return s;
  }

  down(n: number): number {
    let s = 0;
    for (let i = n; i > 0; i--) {
      s = (s + i) | 0;
    }

    return s;
  }

  mirrored(n: number): number {
    let s = 0;
    for (let i = 0; n > i; ++i) {
      s = (s + i) | 0;
    }

    return s;
  }

  inclusive(n: number): number {
    let s = 0;
    for (let i = 0; i <= n; i++) {
      s = (s + i) | 0;
    }

    return s;
  }

  downInclusive(): number {
    let s = 0;
    for (let i = 10; i >= 0; i--) {
      s = (s + i) | 0;
    }

    return s;
  }

  chained(n: number, go: boolean): number {
    let s = 0;
    for (let i = 0; go && i < n && s < 100; i++) {
      s = (s + i) | 0;
    }

    return s;
  }

  unsigned(n: number): number {
    let s = 0;
    for (let i = 0; i < n; i++) {
      s = (s + i) >>> 0;
    }

    return s;
  }

  nestedComparison(n: number): number {
    let s = 0;
    for (let i = 0; !(i >= n); i = (i + 1) | 0) {
      s = (s + i) | 0;
    }

    return s;
  }

  comparedToTrue(n: number): number {
    let s = 0;
    for (let i = 0; i < n === true; i = (i + 1) | 0) {
      s = (s + i) | 0;
    }

    return s;
  }

  converted(n: number): number {
    let s = 0;
    for (let i = 0; (i << 16) >> 16 < 100; i = (i + 1) | 0) {
      s = (s + i) | 0;
    }

    return s;
  }

  promoted(n: bigint): number {
    let s = 0;
    for (let i = 0; i < n; i = (i + 1) | 0) {
      s = (s + i) | 0;
    }

    return s;
  }

  writtenInBody(n: number): number {
    let s = 0;
    for (let i = 0; i < n; i = (i + 1) | 0) {
      i = (i + 2) | 0;
      s = (s + 1) | 0;
    }

    return s;
  }

  passedByRef(n: number): number {
    const s = 0;
    for (let i = 0; i < n; i = (i + 1) | 0) {
      Elisions.bump(i);
    }

    return s;
  }

  writtenByLambda(n: number): number {
    let s = 0;
    for (let i = 0; i < n; i = (i + 1) | 0) {
      const skip = () => ((i = (i + 1) | 0) - 1) | 0;
      skip();
      s = (s + 1) | 0;
    }

    return s;
  }

  unboundedInclusive(n: number): number {
    let s = 0;
    for (let i = 0; i <= n; i = (i + 1) | 0) {
      s = (s + 1) | 0;
    }

    return s;
  }

  unsignedDownToZero(n: number): number {
    let s = 0;
    for (let i = n; i >= 0; i = (i - 1) >>> 0) {
      s = (s + 1) >>> 0;

      if (s > 3) {
        break;
      }
    }

    return s;
  }

  unbounded(n: number): number {
    let s = 0;
    for (let i = 0; i !== n; i = (i + 1) | 0) {
      s = (s + 1) | 0;
    }

    return s;
  }

  private static bump(value: number): void {
    ((value = (value + 1) | 0) - 1) | 0;
  }
}
