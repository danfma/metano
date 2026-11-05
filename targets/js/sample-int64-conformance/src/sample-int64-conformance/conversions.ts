import { checkedInt64, checkedNarrow, checkedUInt64, int32FromDouble, int32FromDoubleChecked, int64FromBigInteger, int64FromDecimal, int64FromDouble, int64FromDoubleChecked, int64ToSingle, uint32FromDouble, uint64FromBigInteger, uint64FromDecimal, uint64FromDouble, uint64FromDoubleChecked } from "metano-runtime";
import { Decimal } from "decimal.js";

export function fromInt(value: number): bigint {
  return BigInt(value);
}

export function fromUInt(value: number): bigint {
  return BigInt(value);
}

export function fromChar(value: string): bigint {
  return BigInt(value.charCodeAt(0));
}

export function unsignedFromInt(value: number): bigint {
  return BigInt.asUintN(64, BigInt(value));
}

export function unsignedFromIntChecked(value: number): bigint {
  return checkedUInt64(BigInt(value));
}

export function toInt(value: bigint): number {
  return Number(BigInt.asIntN(32, value));
}

export function toIntChecked(value: bigint): number {
  return Number(checkedNarrow(value, 32, true));
}

export function toUInt(value: bigint): number {
  return Number(BigInt.asUintN(32, value));
}

export function toUIntChecked(value: bigint): number {
  return Number(checkedNarrow(value, 32, false));
}

export function toShort(value: bigint): number {
  return Number(BigInt.asIntN(16, value));
}

export function toShortChecked(value: bigint): number {
  return Number(checkedNarrow(value, 16, true));
}

export function toUShort(value: bigint): number {
  return Number(BigInt.asUintN(16, value));
}

export function toSByte(value: bigint): number {
  return Number(BigInt.asIntN(8, value));
}

export function toByte(value: bigint): number {
  return Number(BigInt.asUintN(8, value));
}

export function toByteChecked(value: bigint): number {
  return Number(checkedNarrow(value, 8, false));
}

export function toChar(value: bigint): string {
  return String.fromCharCode(Number(BigInt.asUintN(16, value)));
}

export function toULong(value: bigint): bigint {
  return BigInt.asUintN(64, value);
}

export function toULongChecked(value: bigint): bigint {
  return checkedUInt64(value);
}

export function fromULong(value: bigint): bigint {
  return BigInt.asIntN(64, value);
}

export function fromULongChecked(value: bigint): bigint {
  return checkedInt64(value);
}

export function unsignedToInt(value: bigint): number {
  return Number(BigInt.asIntN(32, value));
}

export function unsignedToIntChecked(value: bigint): number {
  return Number(checkedNarrow(value, 32, true));
}

export function toDouble(value: bigint): number {
  return Number(value);
}

export function unsignedToDouble(value: bigint): number {
  return Number(value);
}

export function toSingle(value: bigint): number {
  return int64ToSingle(value);
}

export function unsignedToSingle(value: bigint): number {
  return int64ToSingle(value);
}

export function fromDouble(value: number): bigint {
  return int64FromDouble(value);
}

export function fromDoubleChecked(value: number): bigint {
  return int64FromDoubleChecked(value);
}

export function unsignedFromDouble(value: number): bigint {
  return uint64FromDouble(value);
}

export function unsignedFromDoubleChecked(value: number): bigint {
  return uint64FromDoubleChecked(value);
}

export function fromSingle(value: number): bigint {
  return int64FromDouble(value);
}

export function intFromDouble(value: number): number {
  return int32FromDouble(value);
}

export function intFromDoubleChecked(value: number): number {
  return int32FromDoubleChecked(value);
}

export function uIntFromDouble(value: number): number {
  return uint32FromDouble(value);
}

export function byteFromDouble(value: number): number {
  return int32FromDouble(value) & 0xFF;
}

export function byteFromDoubleChecked(value: number): number {
  return Number(checkedNarrow(BigInt(int32FromDoubleChecked(value)), 8, false));
}

export function shortFromDouble(value: number): number {
  return (int32FromDouble(value) << 16) >> 16;
}

export function charFromDouble(value: number): string {
  return String.fromCharCode(int32FromDouble(value) & 0xFFFF);
}

export function toBigInteger(value: bigint): bigint {
  return value;
}

export function fromBigInteger(value: bigint): bigint {
  return int64FromBigInteger(value);
}

export function unsignedFromBigInteger(value: bigint): bigint {
  return uint64FromBigInteger(value);
}

export function liftedFromInt(value: number | null): bigint | null {
  return value == null ? null : BigInt(value);
}

export function liftedToInt(value: bigint | null): number | null {
  return value == null ? null : Number(BigInt.asIntN(32, value));
}

export function lessThanInt(left: bigint, right: number): boolean {
  return left < right;
}

export function equalsInt(left: bigint, right: number): boolean {
  return left === BigInt(right);
}

export function unsignedGreaterThanInt(left: bigint, right: number): boolean {
  return left > BigInt.asUintN(64, BigInt(right));
}

export class DecimalConversions {
  constructor() { }

  static toDecimal(value: bigint): Decimal {
    return new Decimal(value.toString());
  }

  static unsignedToDecimal(value: bigint): Decimal {
    return new Decimal(value.toString());
  }

  static fromDecimal(value: Decimal): bigint {
    return int64FromDecimal(value);
  }

  static unsignedFromDecimal(value: Decimal): bigint {
    return uint64FromDecimal(value);
  }
}
