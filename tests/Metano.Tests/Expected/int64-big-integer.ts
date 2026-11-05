import { bigIntegerFromDecimal, bigIntegerFromDouble, decimalTextFromBigInteger, doubleFromBigInteger, int32FromBigInteger, int64FromBigInteger, narrowFromBigInteger, uint32FromBigInteger } from "metano-runtime";
import { Decimal } from "decimal.js";

export class Big {
  constructor() { }

  toDecimal(b: bigint): Decimal {
    return new Decimal(decimalTextFromBigInteger(b));
  }

  fromDecimal(d: Decimal): bigint {
    return bigIntegerFromDecimal(d);
  }

  toInt(b: bigint): number {
    return int32FromBigInteger(b);
  }

  toUInt(b: bigint): number {
    return uint32FromBigInteger(b);
  }

  toShort(b: bigint): number {
    return narrowFromBigInteger(b, 16, true);
  }

  toByte(b: bigint): number {
    return narrowFromBigInteger(b, 8, false);
  }

  toChar(b: bigint): string {
    return String.fromCharCode(narrowFromBigInteger(b, 16, false));
  }

  toDouble(b: bigint): number {
    return doubleFromBigInteger(b);
  }

  toSingle(b: bigint): number {
    return Math.fround(doubleFromBigInteger(b));
  }

  fromDouble(d: number): bigint {
    return bigIntegerFromDouble(d);
  }

  fromInt(i: number): bigint {
    return BigInt(i);
  }

  toLong(b: bigint): bigint {
    return int64FromBigInteger(b);
  }

  lifted(b: bigint | null): number | null {
    return b == null ? null : int32FromBigInteger(b);
  }
}
