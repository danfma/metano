import { divInt64, divUInt64, remInt64, remUInt64 } from "metano-runtime";

export function divide(a: bigint, b: bigint): bigint {
  return divInt64(a, b);
}

export function remainder(a: bigint, b: bigint): bigint {
  return remInt64(a, b);
}

export function divideUnchecked(a: bigint, b: bigint): bigint {
  return divInt64(a, b);
}

export function remainderChecked(a: bigint, b: bigint): bigint {
  return remInt64(a, b);
}

export function divideUnsigned(a: bigint, b: bigint): bigint {
  return divUInt64(a, b);
}

export function remainderUnsigned(a: bigint, b: bigint): bigint {
  return remUInt64(a, b);
}

export function divideInt(a: bigint, b: number): bigint {
  return divInt64(a, BigInt(b));
}

export function divideBySeven(a: bigint): bigint {
  return a / 7n;
}

export function remainderByMinusSeven(a: bigint): bigint {
  return a % -7n;
}

export function divideByMinusOne(a: bigint): bigint {
  return divInt64(a, -1n);
}

export function remainderByMinusOne(a: bigint): bigint {
  return remInt64(a, -1n);
}

export function divideUnsignedByThree(a: bigint): bigint {
  return a / 3n;
}

export function liftedDivide(a: bigint | null, b: bigint | null): bigint | null {
  return a == null || b == null ? null : divInt64(a, b);
}

export function liftedRemainderUnsigned(a: bigint | null, b: bigint): bigint | null {
  return a == null ? null : remUInt64(a, b);
}

export function compoundDivide(a: bigint, b: bigint): bigint {
  let x = a;
  x = divInt64(x, b);

  return x;
}

export function compoundRemainder(a: bigint, b: bigint): bigint {
  let x = a;
  x = remInt64(x, b);

  return x;
}

export function compoundDivideUnsigned(a: bigint, b: bigint): bigint {
  let x = a;
  x = divUInt64(x, b);

  return x;
}

export function liftedCompoundRemainder(a: bigint | null, b: bigint): bigint | null {
  let x = a;
  x = x == null ? null : remInt64(x, b);

  return x;
}
