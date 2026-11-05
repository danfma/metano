import { absInt64, bigMulInt64, bigMulUInt64, clampInt64, clampUInt64, divRemInt64, divRemUInt64, maxInt64, maxUInt64, minInt64, minUInt64, signInt64 } from "metano-runtime";

export class Statics {
  constructor() { }

  abs(l: bigint): bigint {
    return BigInt.asIntN(64, absInt64(l) + absInt64(l));
  }

  max(l: bigint, m: bigint): bigint {
    return BigInt.asIntN(64, maxInt64(l, m) + maxInt64(l, m));
  }

  min(l: bigint, m: bigint): bigint {
    return BigInt.asIntN(64, minInt64(l, m) + minInt64(l, m));
  }

  widened(i: number, l: bigint): bigint {
    return maxInt64(BigInt(i), l);
  }

  unsigned(u: bigint, v: bigint): bigint {
    return BigInt.asUintN(64, maxUInt64(u, v) + minUInt64(u, v));
  }

  sign(l: bigint, u: bigint): number {
    return signInt64(l) + signInt64(l) + signInt64(u);
  }

  clamp(l: bigint): bigint {
    return BigInt.asIntN(64, clampInt64(l, -10n, 10n) + clampInt64(l, 0n, 1n));
  }

  uClamp(u: bigint): bigint {
    return clampUInt64(u, 1n, 2n);
  }

  divRem(l: bigint, m: bigint): [bigint, bigint] {
    return divRemInt64(l, m);
  }

  uDivRem(u: bigint, v: bigint): [bigint, bigint] {
    return divRemUInt64(u, v);
  }

  divRemOut(l: bigint, m: bigint): bigint {
    const [quotient, rest] = divRemInt64(l, m);

    return BigInt.asIntN(64, quotient + rest);
  }

  bigMul(l: bigint, m: bigint): bigint {
    const [high, low] = bigMulInt64(l, m);

    return high ^ low;
  }

  uBigMul(u: bigint, v: bigint): bigint {
    const [high, low] = bigMulUInt64(u, v);

    return high ^ low;
  }

  pow(l: bigint): number {
    return Math.pow(Number(l), 2);
  }

  doubles(a: number, b: number): number {
    return Math.max(a, b) + Math.abs(a);
  }
}
