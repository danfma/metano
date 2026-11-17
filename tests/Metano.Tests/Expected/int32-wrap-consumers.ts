import { checkedInt32, checkedNarrowNumber, intDiv } from "metano-runtime";

export class Consumers {
  constructor() { }

  productPlus(a: number, b: number, c: number): number {
    return (Math.imul(a, b) + c) | 0;
  }

  chain(a: number, b: number, c: number): number {
    return (a + b + c - Math.imul(a, b)) | 0;
  }

  uProductPlus(a: number, b: number): number {
    return (Math.imul(a, b) + 1) >>> 0;
  }

  masked(a: number, b: number): number {
    return a + b & 255;
  }

  shifted(a: number, b: number, n: number): number {
    return a + b << n;
  }

  complement(a: number, b: number): number {
    return ~(a + b);
  }

  narrowed(a: number, b: number): number {
    return (a + b) & 0xFF;
  }

  narrowedProduct(a: number, b: number): number {
    return (Math.imul(a, b) << 16) >> 16;
  }

  signed(a: number, b: number): number {
    return (a + b) | 0;
  }

  minusNegated(a: number, b: number): number {
    return (a - -b) | 0;
  }

  minusSum(a: number, b: number, c: number): number {
    return (a - (b + c)) | 0;
  }

  minusDifference(a: number, b: number, c: number): number {
    return (a - (b - c)) | 0;
  }

  negatedSum(a: number): number {
    return -(a + 1) | 0;
  }

  productOfSum(a: number, b: number, c: number): number {
    return Math.imul(a + b, c);
  }

  compared(a: number, b: number): boolean {
    return ((a + b) | 0) < 0;
  }

  halved(a: number, b: number): number {
    return intDiv((a + b) | 0, 2);
  }

  widened(a: number, b: number): bigint {
    return BigInt((a + b) | 0);
  }

  asDouble(a: number, b: number): number {
    return Math.imul(a, b);
  }

  text(a: number, b: number): string {
    return "v" + ((a + b) | 0);
  }

  argument(a: number, b: number): number {
    return Consumers.take((a + b) | 0);
  }

  checkedOuter(a: number, b: number, c: number): number {
    return checkedInt32(a + Math.imul(b, c));
  }

  checkedNarrowing(a: number, b: number): number {
    return checkedNarrowNumber((a + b) | 0, 8, false);
  }

  private static take(value: number): number {
    return value;
  }
}
