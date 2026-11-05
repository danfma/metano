import { absInt64, bigMulInt64, bigMulUInt64, clampInt64, clampUInt64, divRemInt64, divRemUInt64, maxInt64, maxUInt64, minInt64, minUInt64, signInt64 } from "metano-runtime";

export function abs(value: bigint): bigint {
  return absInt64(value);
}

export function absStatic(value: bigint): bigint {
  return absInt64(value);
}

export function max(a: bigint, b: bigint): bigint {
  return maxInt64(a, b);
}

export function min(a: bigint, b: bigint): bigint {
  return minInt64(a, b);
}

export function maxWidened(a: number, b: bigint): bigint {
  return maxInt64(BigInt(a), b);
}

export function maxUnsigned(a: bigint, b: bigint): bigint {
  return maxUInt64(a, b);
}

export function minUnsigned(a: bigint, b: bigint): bigint {
  return minUInt64(a, b);
}

export function sign(value: bigint): number {
  return signInt64(value);
}

export function signUnsigned(value: bigint): number {
  return signInt64(value);
}

export function clamp(value: bigint, min: bigint, max: bigint): bigint {
  return clampInt64(value, min, max);
}

export function clampUnsigned(value: bigint, min: bigint, max: bigint): bigint {
  return clampUInt64(value, min, max);
}

export function divRem(a: bigint, b: bigint): string {
  const pair = divRemInt64(a, b);

  return pair[0] + "," + pair[1];
}

export function divRemOut(a: bigint, b: bigint): string {
  const [quotient, modulus] = divRemInt64(a, b);

  return quotient + "," + modulus;
}

export function divRemUnsigned(a: bigint, b: bigint): string {
  const pair = divRemUInt64(a, b);

  return pair[0] + "," + pair[1];
}

export function bigMul(a: bigint, b: bigint): string {
  const [high, low] = bigMulInt64(a, b);

  return high + "," + low;
}

export function bigMulUnsigned(a: bigint, b: bigint): string {
  const [high, low] = bigMulUInt64(a, b);

  return high + "," + low;
}

export function pow(value: bigint): number {
  return Math.pow(Number(value), 2);
}
