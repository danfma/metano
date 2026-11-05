export class Wrap {
  constructor() { }

  add(l: bigint, m: bigint): bigint {
    return BigInt.asIntN(64, l + m);
  }

  sub(l: bigint, m: bigint): bigint {
    return BigInt.asIntN(64, l - m);
  }

  mul(l: bigint, m: bigint): bigint {
    return BigInt.asIntN(64, l * m);
  }

  uAdd(u: bigint, v: bigint): bigint {
    return BigInt.asUintN(64, u + v);
  }

  uSub(u: bigint, v: bigint): bigint {
    return BigInt.asUintN(64, u - v);
  }

  uMul(u: bigint, v: bigint): bigint {
    return BigInt.asUintN(64, u * v);
  }

  addInt(l: bigint, i: number): bigint {
    return BigInt.asIntN(64, l + BigInt(i));
  }

  uAddConst(u: bigint): bigint {
    return BigInt.asUintN(64, u + 1n);
  }

  neg(l: bigint): bigint {
    return BigInt.asIntN(64, -l);
  }

  not(l: bigint): bigint {
    return ~l;
  }

  uNot(u: bigint): bigint {
    return BigInt.asUintN(64, ~u);
  }

  bitwise(l: bigint, m: bigint): bigint {
    return l & m | l ^ m;
  }

  compare(l: bigint, m: bigint): boolean {
    return BigInt.asIntN(64, l + 1n) < m;
  }

  promote(ui: number, i: number): bigint {
    return BigInt.asIntN(64, BigInt(ui) + BigInt(i));
  }

  promoteMul(ui: number, i: number): bigint {
    return BigInt.asIntN(64, BigInt(ui) * BigInt(i));
  }

  promoteNeg(ui: number): bigint {
    return BigInt.asIntN(64, -BigInt(ui));
  }

  promoteNarrow(ui: number, i: number): number {
    return Number(BigInt.asIntN(32, BigInt.asIntN(64, BigInt(ui) + BigInt(i))));
  }

  unchecked(l: bigint, m: bigint): bigint {
    return BigInt.asIntN(64, l * m);
  }

  chain(a: bigint, b: bigint, c: bigint): bigint {
    return BigInt.asIntN(64, BigInt.asIntN(64, a * b) + c);
  }

  native(a: bigint, b: bigint): bigint {
    return BigInt.asIntN(64, a + b);
  }

  uNative(a: bigint, b: bigint): bigint {
    return BigInt.asUintN(64, a * b);
  }

  lifted(nl: bigint | null): bigint | null {
    return nl == null ? null : BigInt.asIntN(64, nl + 1n);
  }

  liftedPair(nl: bigint | null, nm: bigint | null): bigint | null {
    return nl == null || nm == null ? null : BigInt.asIntN(64, nl * nm);
  }

  liftedNeg(nl: bigint | null): bigint | null {
    return nl == null ? null : BigInt.asIntN(64, -nl);
  }

  liftedNot(nu: bigint | null): bigint | null {
    return nu == null ? null : BigInt.asUintN(64, ~nu);
  }

  narrow(a: number, b: number): number {
    return a + b;
  }

  floating(a: number, b: number): number {
    return a * b;
  }
}
