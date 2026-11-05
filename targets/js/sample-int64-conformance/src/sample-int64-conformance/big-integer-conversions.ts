import { bigIntegerFromDecimal, bigIntegerFromDouble, decimalTextFromBigInteger, doubleFromBigInteger, int32FromBigInteger, narrowFromBigInteger, uint32FromBigInteger } from "metano-runtime";
import { Decimal } from "decimal.js";

export function bigToInt(value: bigint): number {
  return int32FromBigInteger(value);
}

export function bigToUInt(value: bigint): number {
  return uint32FromBigInteger(value);
}

export function bigToShort(value: bigint): number {
  return narrowFromBigInteger(value, 16, true);
}

export function bigToUShort(value: bigint): number {
  return narrowFromBigInteger(value, 16, false);
}

export function bigToSByte(value: bigint): number {
  return narrowFromBigInteger(value, 8, true);
}

export function bigToByte(value: bigint): number {
  return narrowFromBigInteger(value, 8, false);
}

export function bigToChar(value: bigint): string {
  return String.fromCharCode(narrowFromBigInteger(value, 16, false));
}

export function bigToDouble(value: bigint): number {
  return doubleFromBigInteger(value);
}

export function bigToSingle(value: bigint): number {
  return Math.fround(doubleFromBigInteger(value));
}

export function bigFromDouble(value: number): bigint {
  return bigIntegerFromDouble(value);
}

export function bigFromSingle(value: number): bigint {
  return bigIntegerFromDouble(value);
}

export class DecimalBigIntegerConversions {
  constructor() { }

  static bigToDecimal(value: bigint): Decimal {
    return new Decimal(decimalTextFromBigInteger(value));
  }

  static bigFromDecimal(value: Decimal): bigint {
    return bigIntegerFromDecimal(value);
  }
}
