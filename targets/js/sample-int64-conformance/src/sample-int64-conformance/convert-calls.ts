import { convertDecimalToInt64, convertDecimalToUInt64, convertDoubleToInt64, convertDoubleToUInt64, convertNarrow, convertStringToInt64, convertStringToUInt64, int64ToSingle } from "metano-runtime";
import type { Decimal } from "decimal.js";

export function convertFromDouble(value: number): bigint {
  return convertDoubleToInt64(value);
}

export function convertUnsignedFromDouble(value: number): bigint {
  return convertDoubleToUInt64(value);
}

export function convertFromSingle(value: number): bigint {
  return convertDoubleToInt64(value);
}

export function convertFromString(text: string): bigint {
  return convertStringToInt64(text);
}

export function convertUnsignedFromString(text: string): bigint {
  return convertStringToUInt64(text);
}

export function convertFromStringInBase(text: string, fromBase: number): bigint {
  return convertStringToInt64(text, fromBase);
}

export function convertUnsignedFromStringInBase(text: string, fromBase: number): bigint {
  return convertStringToUInt64(text, fromBase);
}

export function convertFromBoolean(value: boolean): bigint {
  return (value ? 1n : 0n);
}

export function convertFromChar(value: string): bigint {
  return BigInt(value.charCodeAt(0));
}

export function convertUnsignedFromInt(value: number): bigint {
  return convertNarrow(BigInt(value), "UInt64");
}

export function convertUnsignedFromLong(value: bigint): bigint {
  return convertNarrow(value, "UInt64");
}

export function convertFromULong(value: bigint): bigint {
  return convertNarrow(value, "Int64");
}

export function convertToInt32(value: bigint): number {
  return Number(convertNarrow(value, "Int32"));
}

export function convertToUInt32(value: bigint): number {
  return Number(convertNarrow(value, "UInt32"));
}

export function convertToInt16(value: bigint): number {
  return Number(convertNarrow(value, "Int16"));
}

export function convertToUInt16(value: bigint): number {
  return Number(convertNarrow(value, "UInt16"));
}

export function convertToByte(value: bigint): number {
  return Number(convertNarrow(value, "Byte"));
}

export function convertToSByte(value: bigint): number {
  return Number(convertNarrow(value, "SByte"));
}

export function convertToChar(value: bigint): string {
  return String.fromCharCode(Number(convertNarrow(value, "Char")));
}

export function convertUnsignedToInt32(value: bigint): number {
  return Number(convertNarrow(value, "Int32"));
}

export function convertToDouble(value: bigint): number {
  return Number(value);
}

export function convertToSingle(value: bigint): number {
  return int64ToSingle(value);
}

export function convertToBoolean(value: bigint): boolean {
  return (value !== 0n);
}

export class DecimalConvertCalls {
  constructor() { }

  static convertFromDecimal(value: Decimal): bigint {
    return convertDecimalToInt64(value);
  }

  static convertUnsignedFromDecimal(value: Decimal): bigint {
    return convertDecimalToUInt64(value);
  }
}
