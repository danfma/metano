import { checkedInt32, checkedNarrow, checkedNarrowNumber, checkedUInt32, int32FromDouble, int32FromDoubleChecked, uint32FromDouble, uint32FromDoubleChecked } from "metano-runtime";

export function sByteToSByte(source: number): number {
  return source;
}

export function sByteToSByteChecked(source: number): number {
  return source;
}

export function sByteToByte(source: number): number {
  return source & 0xFF;
}

export function sByteToByteChecked(source: number): number {
  return checkedNarrowNumber(source, 8, false);
}

export function sByteToShort(source: number): number {
  return source;
}

export function sByteToShortChecked(source: number): number {
  return source;
}

export function sByteToUShort(source: number): number {
  return source & 0xFFFF;
}

export function sByteToUShortChecked(source: number): number {
  return checkedNarrowNumber(source, 16, false);
}

export function sByteToInt(source: number): number {
  return source;
}

export function sByteToIntChecked(source: number): number {
  return source;
}

export function sByteToUInt(source: number): number {
  return source >>> 0;
}

export function sByteToUIntChecked(source: number): number {
  return checkedUInt32(source);
}

export function byteToSByte(source: number): number {
  return (source << 24) >> 24;
}

export function byteToSByteChecked(source: number): number {
  return checkedNarrowNumber(source, 8, true);
}

export function byteToByte(source: number): number {
  return source;
}

export function byteToByteChecked(source: number): number {
  return source;
}

export function byteToShort(source: number): number {
  return source;
}

export function byteToShortChecked(source: number): number {
  return source;
}

export function byteToUShort(source: number): number {
  return source;
}

export function byteToUShortChecked(source: number): number {
  return source;
}

export function byteToInt(source: number): number {
  return source;
}

export function byteToIntChecked(source: number): number {
  return source;
}

export function byteToUInt(source: number): number {
  return source;
}

export function byteToUIntChecked(source: number): number {
  return source;
}

export function shortToSByte(source: number): number {
  return (source << 24) >> 24;
}

export function shortToSByteChecked(source: number): number {
  return checkedNarrowNumber(source, 8, true);
}

export function shortToByte(source: number): number {
  return source & 0xFF;
}

export function shortToByteChecked(source: number): number {
  return checkedNarrowNumber(source, 8, false);
}

export function shortToShort(source: number): number {
  return source;
}

export function shortToShortChecked(source: number): number {
  return source;
}

export function shortToUShort(source: number): number {
  return source & 0xFFFF;
}

export function shortToUShortChecked(source: number): number {
  return checkedNarrowNumber(source, 16, false);
}

export function shortToInt(source: number): number {
  return source;
}

export function shortToIntChecked(source: number): number {
  return source;
}

export function shortToUInt(source: number): number {
  return source >>> 0;
}

export function shortToUIntChecked(source: number): number {
  return checkedUInt32(source);
}

export function uShortToSByte(source: number): number {
  return (source << 24) >> 24;
}

export function uShortToSByteChecked(source: number): number {
  return checkedNarrowNumber(source, 8, true);
}

export function uShortToByte(source: number): number {
  return source & 0xFF;
}

export function uShortToByteChecked(source: number): number {
  return checkedNarrowNumber(source, 8, false);
}

export function uShortToShort(source: number): number {
  return (source << 16) >> 16;
}

export function uShortToShortChecked(source: number): number {
  return checkedNarrowNumber(source, 16, true);
}

export function uShortToUShort(source: number): number {
  return source;
}

export function uShortToUShortChecked(source: number): number {
  return source;
}

export function uShortToInt(source: number): number {
  return source;
}

export function uShortToIntChecked(source: number): number {
  return source;
}

export function uShortToUInt(source: number): number {
  return source;
}

export function uShortToUIntChecked(source: number): number {
  return source;
}

export function intToSByte(source: number): number {
  return (source << 24) >> 24;
}

export function intToSByteChecked(source: number): number {
  return checkedNarrowNumber(source, 8, true);
}

export function intToByte(source: number): number {
  return source & 0xFF;
}

export function intToByteChecked(source: number): number {
  return checkedNarrowNumber(source, 8, false);
}

export function intToShort(source: number): number {
  return (source << 16) >> 16;
}

export function intToShortChecked(source: number): number {
  return checkedNarrowNumber(source, 16, true);
}

export function intToUShort(source: number): number {
  return source & 0xFFFF;
}

export function intToUShortChecked(source: number): number {
  return checkedNarrowNumber(source, 16, false);
}

export function intToInt(source: number): number {
  return source;
}

export function intToIntChecked(source: number): number {
  return source;
}

export function intToUInt(source: number): number {
  return source >>> 0;
}

export function intToUIntChecked(source: number): number {
  return checkedUInt32(source);
}

export function uIntToSByte(source: number): number {
  return (source << 24) >> 24;
}

export function uIntToSByteChecked(source: number): number {
  return checkedNarrowNumber(source, 8, true);
}

export function uIntToByte(source: number): number {
  return source & 0xFF;
}

export function uIntToByteChecked(source: number): number {
  return checkedNarrowNumber(source, 8, false);
}

export function uIntToShort(source: number): number {
  return (source << 16) >> 16;
}

export function uIntToShortChecked(source: number): number {
  return checkedNarrowNumber(source, 16, true);
}

export function uIntToUShort(source: number): number {
  return source & 0xFFFF;
}

export function uIntToUShortChecked(source: number): number {
  return checkedNarrowNumber(source, 16, false);
}

export function uIntToInt(source: number): number {
  return source | 0;
}

export function uIntToIntChecked(source: number): number {
  return checkedInt32(source);
}

export function uIntToUInt(source: number): number {
  return source;
}

export function uIntToUIntChecked(source: number): number {
  return source;
}

export function longToSByte(source: bigint): number {
  return Number(BigInt.asIntN(8, source));
}

export function longToSByteChecked(source: bigint): number {
  return Number(checkedNarrow(source, 8, true));
}

export function longToByte(source: bigint): number {
  return Number(BigInt.asUintN(8, source));
}

export function longToByteChecked(source: bigint): number {
  return Number(checkedNarrow(source, 8, false));
}

export function longToShort(source: bigint): number {
  return Number(BigInt.asIntN(16, source));
}

export function longToShortChecked(source: bigint): number {
  return Number(checkedNarrow(source, 16, true));
}

export function longToUShort(source: bigint): number {
  return Number(BigInt.asUintN(16, source));
}

export function longToUShortChecked(source: bigint): number {
  return Number(checkedNarrow(source, 16, false));
}

export function longToInt(source: bigint): number {
  return Number(BigInt.asIntN(32, source));
}

export function longToIntChecked(source: bigint): number {
  return Number(checkedNarrow(source, 32, true));
}

export function longToUInt(source: bigint): number {
  return Number(BigInt.asUintN(32, source));
}

export function longToUIntChecked(source: bigint): number {
  return Number(checkedNarrow(source, 32, false));
}

export function uLongToSByte(source: bigint): number {
  return Number(BigInt.asIntN(8, source));
}

export function uLongToSByteChecked(source: bigint): number {
  return Number(checkedNarrow(source, 8, true));
}

export function uLongToByte(source: bigint): number {
  return Number(BigInt.asUintN(8, source));
}

export function uLongToByteChecked(source: bigint): number {
  return Number(checkedNarrow(source, 8, false));
}

export function uLongToShort(source: bigint): number {
  return Number(BigInt.asIntN(16, source));
}

export function uLongToShortChecked(source: bigint): number {
  return Number(checkedNarrow(source, 16, true));
}

export function uLongToUShort(source: bigint): number {
  return Number(BigInt.asUintN(16, source));
}

export function uLongToUShortChecked(source: bigint): number {
  return Number(checkedNarrow(source, 16, false));
}

export function uLongToInt(source: bigint): number {
  return Number(BigInt.asIntN(32, source));
}

export function uLongToIntChecked(source: bigint): number {
  return Number(checkedNarrow(source, 32, true));
}

export function uLongToUInt(source: bigint): number {
  return Number(BigInt.asUintN(32, source));
}

export function uLongToUIntChecked(source: bigint): number {
  return Number(checkedNarrow(source, 32, false));
}

export function doubleToSByte(source: number): number {
  return (int32FromDouble(source) << 24) >> 24;
}

export function doubleToSByteChecked(source: number): number {
  return Number(checkedNarrow(BigInt(int32FromDoubleChecked(source)), 8, true));
}

export function doubleToByte(source: number): number {
  return int32FromDouble(source) & 0xFF;
}

export function doubleToByteChecked(source: number): number {
  return Number(checkedNarrow(BigInt(int32FromDoubleChecked(source)), 8, false));
}

export function doubleToShort(source: number): number {
  return (int32FromDouble(source) << 16) >> 16;
}

export function doubleToShortChecked(source: number): number {
  return Number(checkedNarrow(BigInt(int32FromDoubleChecked(source)), 16, true));
}

export function doubleToUShort(source: number): number {
  return int32FromDouble(source) & 0xFFFF;
}

export function doubleToUShortChecked(source: number): number {
  return Number(checkedNarrow(BigInt(int32FromDoubleChecked(source)), 16, false));
}

export function doubleToInt(source: number): number {
  return int32FromDouble(source);
}

export function doubleToIntChecked(source: number): number {
  return int32FromDoubleChecked(source);
}

export function doubleToUInt(source: number): number {
  return uint32FromDouble(source);
}

export function doubleToUIntChecked(source: number): number {
  return uint32FromDoubleChecked(source);
}

export function floatToSByte(source: number): number {
  return (int32FromDouble(source) << 24) >> 24;
}

export function floatToSByteChecked(source: number): number {
  return Number(checkedNarrow(BigInt(int32FromDoubleChecked(source)), 8, true));
}

export function floatToByte(source: number): number {
  return int32FromDouble(source) & 0xFF;
}

export function floatToByteChecked(source: number): number {
  return Number(checkedNarrow(BigInt(int32FromDoubleChecked(source)), 8, false));
}

export function floatToShort(source: number): number {
  return (int32FromDouble(source) << 16) >> 16;
}

export function floatToShortChecked(source: number): number {
  return Number(checkedNarrow(BigInt(int32FromDoubleChecked(source)), 16, true));
}

export function floatToUShort(source: number): number {
  return int32FromDouble(source) & 0xFFFF;
}

export function floatToUShortChecked(source: number): number {
  return Number(checkedNarrow(BigInt(int32FromDoubleChecked(source)), 16, false));
}

export function floatToInt(source: number): number {
  return int32FromDouble(source);
}

export function floatToIntChecked(source: number): number {
  return int32FromDoubleChecked(source);
}

export function floatToUInt(source: number): number {
  return uint32FromDouble(source);
}

export function floatToUIntChecked(source: number): number {
  return uint32FromDoubleChecked(source);
}

export function liftedIntToByte(source: number | null): number | null {
  return source == null ? null : source & 0xFF;
}

export function liftedIntToByteChecked(source: number | null): number | null {
  return source == null ? null : checkedNarrowNumber(source, 8, false);
}

export function liftedIntToUInt(source: number | null): number | null {
  return source == null ? null : source >>> 0;
}

export function liftedUIntToShortChecked(source: number | null): number | null {
  return source == null ? null : checkedNarrowNumber(source, 16, true);
}

export function liftedLongToIntChecked(source: bigint | null): number | null {
  return source == null ? null : Number(checkedNarrow(source, 32, true));
}

export function sumToByte(a: number, b: number): number {
  return (a + b) & 0xFF;
}

export function productToSByte(a: number, b: number): number {
  return ((a * b) << 24) >> 24;
}

export function negateToShort(a: number): number {
  return (-a << 16) >> 16;
}

export function complementToUShort(a: number): number {
  return ~a & 0xFFFF;
}

export function maskToByte(source: number): number {
  return source & 255;
}

export function highByte(source: number): number {
  return (source >> 24) & 0xFF;
}

export function constantUInt(): number {
  return 4294967295;
}

export function constantByte(): number {
  return 44;
}

export function constantSByte(): number {
  return -128;
}
