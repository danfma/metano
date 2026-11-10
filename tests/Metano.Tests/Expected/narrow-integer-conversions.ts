import { checkedInt32, checkedNarrow, checkedNarrowInt32, checkedUInt32, int32FromDouble, int32FromDoubleChecked } from "metano-runtime";

export class Narrowing {
  constructor() { }

  intToUInt(x: number): number {
    return x >>> 0;
  }

  uIntToInt(x: number): number {
    return x | 0;
  }

  intToShort(x: number): number {
    return (x << 16) >> 16;
  }

  intToUShort(x: number): number {
    return x & 0xFFFF;
  }

  intToSByte(x: number): number {
    return (x << 24) >> 24;
  }

  intToByte(x: number): number {
    return x & 0xFF;
  }

  sByteToByte(x: number): number {
    return x & 0xFF;
  }

  byteToSByte(x: number): number {
    return (x << 24) >> 24;
  }

  shortToUShort(x: number): number {
    return x & 0xFFFF;
  }

  sByteToUInt(x: number): number {
    return x >>> 0;
  }

  sum(a: number, b: number): number {
    return (a + b) & 0xFF;
  }

  negated(x: number): number {
    return (-x << 16) >> 16;
  }

  widened(x: number): number {
    return x;
  }

  widenedUnsigned(x: number): number {
    return x;
  }

  same(x: number): number {
    return x;
  }

  widenedSigned(x: number): number {
    return x;
  }

  masked(x: number): number {
    return x & 255;
  }

  maskedPair(x: number, y: number): number {
    return (x & y) >>> 0 & 65535;
  }

  constant(): number {
    return 4294967295;
  }

  constantByte(): number {
    return 44;
  }

  fromDouble(d: number): number {
    return int32FromDouble(d) & 0xFF;
  }

  fromFloat(f: number): number {
    return (int32FromDouble(f) << 16) >> 16;
  }

  fromLong(l: bigint): number {
    return Number(BigInt.asIntN(8, l));
  }

  fromULong(u: bigint): number {
    return Number(BigInt.asUintN(32, u));
  }

  lifted(x: number | null): number | null {
    return x == null ? null : x & 0xFF;
  }

  unwrapped(x: number | null): number {
    return (x ?? (() => {
      throw new Error("Nullable object must have a value.");
    })()) & 0xFF;
  }

  checked(x: number): number {
    return checkedNarrowInt32(x, 8, false);
  }

  checkedSigned(x: number): number {
    return checkedNarrowInt32(x, 8, true);
  }

  checkedUShort(x: number): number {
    return checkedNarrowInt32(x, 16, false);
  }

  checkedUInt(x: number): number {
    return checkedUInt32(x);
  }

  checkedInt(x: number): number {
    return checkedInt32(x);
  }

  checkedMasked(x: number): number {
    return x & 255;
  }

  checkedLifted(x: number | null): number | null {
    return x == null ? null : checkedNarrowInt32(x, 8, false);
  }

  checkedFromDouble(d: number): number {
    return Number(checkedNarrow(BigInt(int32FromDoubleChecked(d)), 8, false));
  }

  checkedFromLong(l: bigint): number {
    return Number(checkedNarrow(l, 8, false));
  }

  checkedLiftedFromLong(l: bigint | null): number | null {
    return l == null ? null : Number(checkedNarrow(l, 32, true));
  }
}
