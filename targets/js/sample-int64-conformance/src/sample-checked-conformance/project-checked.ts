import { checkedInt32, checkedInt64, checkedNarrow, checkedNarrowNumber, checkedUInt32, checkedUInt64, int32FromDoubleChecked, intDiv } from "metano-runtime";

export function intAdd(a: number, b: number): number {
  return checkedInt32(a + b);
}

export function intSubtract(a: number, b: number): number {
  return checkedInt32(a - b);
}

export function intMultiply(a: number, b: number): number {
  return checkedInt32(a * b);
}

export function intNegate(a: number): number {
  return checkedInt32(-a);
}

export function uIntAdd(a: number, b: number): number {
  return checkedUInt32(a + b);
}

export function uIntSubtract(a: number, b: number): number {
  return checkedUInt32(a - b);
}

export function intIncrement(a: number): number {
  a = checkedInt32(a + 1);

  return a;
}

export function uIntDecrement(a: number): number {
  const previous = (a = checkedUInt32(a - 1)) + 1;

  return previous;
}

export function intShiftLeft(a: number, count: number): number {
  return a << count;
}

export function uIntOr(a: number, b: number): number {
  return (a | b) >>> 0;
}

export function intToByte(source: number): number {
  return checkedNarrowNumber(source, 8, false);
}

export function intToSByte(source: number): number {
  return checkedNarrowNumber(source, 8, true);
}

export function intToShort(source: number): number {
  return checkedNarrowNumber(source, 16, true);
}

export function intToUShort(source: number): number {
  return checkedNarrowNumber(source, 16, false);
}

export function intToUInt(source: number): number {
  return checkedUInt32(source);
}

export function uIntToInt(source: number): number {
  return checkedInt32(source);
}

export function sByteToByte(source: number): number {
  return checkedNarrowNumber(source, 8, false);
}

export function longToInt(source: bigint): number {
  return Number(checkedNarrow(source, 32, true));
}

export function doubleToByte(source: number): number {
  return Number(checkedNarrow(BigInt(int32FromDoubleChecked(source)), 8, false));
}

export function liftedIntToByte(source: number | null): number | null {
  return source == null ? null : checkedNarrowNumber(source, 8, false);
}

export function byteAdd(a: number, b: number): number {
  a = checkedNarrowNumber(a + b, 8, false);

  return a;
}

export function sByteIncrement(a: number): number {
  return a = checkedNarrowNumber(a + 1, 8, true);
}

export function shortPostfixDecrement(a: number): number {
  const previous = (a = checkedNarrowNumber(a - 1, 16, true)) + 1;

  return checkedNarrowNumber(previous ^ a, 16, true);
}

export function uShortShiftLeft(a: number, count: number): number {
  a = checkedNarrowNumber(a << count, 16, false);

  return a;
}

export function bytesAdd(a: number, b: number): number {
  return a + b;
}

export function longAdd(a: bigint, b: bigint): bigint {
  return checkedInt64(a + b);
}

export function uLongSubtract(a: bigint, b: bigint): bigint {
  return checkedUInt64(a - b);
}

export function intAddLambda(a: number, b: number): number {
  const add = (value: number) => checkedInt32(value + b);

  return add(a);
}

export function uncheckedHashNarrowed(a: number): number {
  const hash = ((a * 31 + 7) >>> 0) | 0;

  return checkedInt32(intDiv(hash, 2) + 1);
}

export function uncheckedIntToByte(source: number): number {
  return source & 0xFF;
}

export function uncheckedIntToUInt(source: number): number {
  return source >>> 0;
}

export function uncheckedByteAdd(a: number, b: number): number {
  {
    a = (a + b) & 0xFF;
  }

  return a;
}

export function uncheckedLongAdd(a: bigint, b: bigint): bigint {
  return BigInt.asIntN(64, a + b);
}
