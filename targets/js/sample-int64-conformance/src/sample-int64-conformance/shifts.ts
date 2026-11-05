export function shiftLeft(a: bigint, n: number): bigint {
  return BigInt.asIntN(64, a << BigInt(n & 63));
}

export function shiftRight(a: bigint, n: number): bigint {
  return a >> BigInt(n & 63);
}

export function shiftRightLogical(a: bigint, n: number): bigint {
  return BigInt.asIntN(64, BigInt.asUintN(64, a) >> BigInt(n & 63));
}

export function shiftLeftUnsigned(a: bigint, n: number): bigint {
  return BigInt.asUintN(64, a << BigInt(n & 63));
}

export function shiftRightUnsigned(a: bigint, n: number): bigint {
  return a >> BigInt(n & 63);
}

export function shiftRightLogicalUnsigned(a: bigint, n: number): bigint {
  return a >> BigInt(n & 63);
}

export function shiftLeftChecked(a: bigint, n: number): bigint {
  return BigInt.asIntN(64, a << BigInt(n & 63));
}

export function shiftLeftBySeventy(a: bigint): bigint {
  return BigInt.asIntN(64, a << 6n);
}

export function shiftRightByMinusOne(a: bigint): bigint {
  return a >> 63n;
}

export function shiftRightLogicalByTwo(a: bigint): bigint {
  return BigInt.asIntN(64, BigInt.asUintN(64, a) >> 2n);
}

export function liftedShiftLeft(a: bigint | null, n: number | null): bigint | null {
  return a == null || n == null ? null : BigInt.asIntN(64, a << BigInt(n & 63));
}

export function compoundShiftLeft(a: bigint, n: number): bigint {
  let x = a;
  x = BigInt.asIntN(64, x << BigInt(n & 63));

  return x;
}

export function compoundShiftLeftChecked(a: bigint, n: number): bigint {
  let x = a;

  {
    x = BigInt.asIntN(64, x << BigInt(n & 63));
  }

  return x;
}

export function compoundShiftRightLogical(a: bigint, n: number): bigint {
  let x = a;
  x = BigInt.asIntN(64, BigInt.asUintN(64, x) >> BigInt(n & 63));

  return x;
}

export function compoundShiftRightUnsigned(a: bigint, n: number): bigint {
  let x = a;
  x = x >> BigInt(n & 63);

  return x;
}

export function bitwise(a: bigint, b: bigint): bigint {
  return a & b | a ^ ~b;
}

export function bitwiseUnsigned(a: bigint, b: bigint): bigint {
  return a & b | a ^ BigInt.asUintN(64, ~b);
}
