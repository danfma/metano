import { intDiv } from "metano-runtime";

export class Wraps {
  field: number = 0;

  unsigned: number = 0;

  prop: number = 0;

  cells: number[] = new Array<number>(4).fill(0);

  constructor() { }

  add(a: number, b: number): number {
    return (a + b) | 0;
  }

  sub(a: number, b: number): number {
    return (a - b) | 0;
  }

  mul(a: number, b: number): number {
    return Math.imul(a, b);
  }

  neg(a: number): number {
    return -a | 0;
  }

  uAdd(a: number, b: number): number {
    return (a + b) >>> 0;
  }

  uSub(a: number, b: number): number {
    return (a - b) >>> 0;
  }

  uMul(a: number, b: number): number {
    return Math.imul(a, b) >>> 0;
  }

  uNeg(a: number): bigint {
    return BigInt.asIntN(64, -BigInt(a));
  }

  bytes(a: number, b: number): number {
    return a * b * a;
  }

  charTimes(c: string): number {
    return Math.imul(c.charCodeAt(0), 70000);
  }

  charPlus(c: string): number {
    return c.charCodeAt(0) + 1;
  }

  constant(): number {
    return -2147483648;
  }

  divide(a: number, b: number): number {
    return intDiv(a, b);
  }

  lifted(a: number | null, b: number | null): number | null {
    return a == null || b == null ? null : (a + b) | 0;
  }

  liftedProduct(a: number | null, b: number): number | null {
    return a == null ? null : Math.imul(a, b);
  }

  steps(a: number): number {
    a = (a + 1) | 0;
    a = (a + 1) | 0;
    a = (a - 1) | 0;
    a = (a + 5) | 0;
    a = (a - 1) | 0;
    a = Math.imul(a, 3);

    return a;
  }

  unsignedSteps(u: number): number {
    u = (u - 1) >>> 0;
    u = Math.imul(u, 3) >>> 0;

    return u;
  }

  postfix(a: number): number {
    const old = ((a = (a + 1) | 0) - 1) | 0;

    return (old + a) | 0;
  }

  prefix(a: number): number {
    return a = (a + 1) | 0;
  }

  stores(k: number): void {
    this.field = (this.field + k) | 0;
    this.unsigned = (this.unsigned - 1) >>> 0;
    this.prop = Math.imul(this.prop, k);
    this.cells[k] = (this.cells[k]! + 2) | 0;
    this.cells[k] = (this.cells[k]! + 1) | 0;
  }
}
