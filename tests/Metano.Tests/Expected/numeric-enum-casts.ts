import { checkedInt32, checkedNarrow, checkedNarrowNumber, checkedUInt32, int32FromDouble, int32FromDoubleChecked, mapNullable } from "metano-runtime";

export const ByteEnum = {
  One: 1,
} as const;

export type ByteEnum = typeof ByteEnum[keyof typeof ByteEnum];

export const SByteEnum = {
  One: 1,
} as const;

export type SByteEnum = typeof SByteEnum[keyof typeof SByteEnum];

export const ShortEnum = {
  One: 1,
} as const;

export type ShortEnum = typeof ShortEnum[keyof typeof ShortEnum];

export const UShortEnum = {
  One: 1,
} as const;

export type UShortEnum = typeof UShortEnum[keyof typeof UShortEnum];

export const IntEnum = {
  One: 1,
} as const;

export type IntEnum = typeof IntEnum[keyof typeof IntEnum];

export const UIntEnum = {
  One: 1,
} as const;

export type UIntEnum = typeof UIntEnum[keyof typeof UIntEnum];

export const FlagsEnum = {
  One: 1,
} as const;

export type FlagsEnum = number & { readonly __enum?: "FlagsEnum" };

export class EnumCasts {
  constructor() { }

  static byte(x: number): ByteEnum {
    return (x & 0xFF) as ByteEnum;
  }

  static sByte(x: number): SByteEnum {
    return (x << 24) >> 24 as SByteEnum;
  }

  static short(x: number): ShortEnum {
    return (x << 16) >> 16 as ShortEnum;
  }

  static uShort(x: number): UShortEnum {
    return (x & 0xFFFF) as UShortEnum;
  }

  static int(x: number): IntEnum {
    return (x | 0) as IntEnum;
  }

  static uInt(x: number): UIntEnum {
    return x >>> 0 as UIntEnum;
  }

  static checkedByte(x: number): ByteEnum {
    return checkedNarrowNumber(x, 8, false) as ByteEnum;
  }

  static checkedSByte(x: number): SByteEnum {
    return checkedNarrowNumber(x, 8, true) as SByteEnum;
  }

  static checkedShort(x: number): ShortEnum {
    return checkedNarrowNumber(x, 16, true) as ShortEnum;
  }

  static checkedUShort(x: number): UShortEnum {
    return checkedNarrowNumber(x, 16, false) as UShortEnum;
  }

  static checkedInt(x: number): IntEnum {
    return checkedInt32(x) as IntEnum;
  }

  static checkedUInt(x: number): UIntEnum {
    return checkedUInt32(x) as UIntEnum;
  }

  static block(x: number): ByteEnum {
    {
      return checkedNarrowNumber(x, 8, false) as ByteEnum;
    }
  }

  static inner(x: number): ByteEnum {
    return (x & 0xFF) as ByteEnum;
  }

  static constant(): ByteEnum {
    return 255 as ByteEnum;
  }

  static constantUInt(): UIntEnum {
    return 4294967295 as UIntEnum;
  }

  static lifted(x: number | null): ByteEnum | null {
    return (x == null ? null : x & 0xFF) as ByteEnum | null;
  }

  static checkedLifted(x: number | null): ByteEnum | null {
    return (x == null ? null : checkedNarrowNumber(x, 8, false)) as ByteEnum | null;
  }

  static once(x: number | null): ByteEnum | null {
    return mapNullable(EnumCasts.next(x), (value) => value & 0xFF) as ByteEnum | null;
  }

  static next(x: number | null): number | null {
    return x;
  }

  static fromEnum(x: ShortEnum): ByteEnum {
    return (x & 0xFF) as ByteEnum;
  }

  static checkedFromEnum(x: ShortEnum): ByteEnum {
    return checkedNarrowNumber(x, 8, false) as ByteEnum;
  }

  static flags(x: number): FlagsEnum {
    return x & 0xFF;
  }

  static checkedFlags(x: number): FlagsEnum {
    return checkedNarrowNumber(x, 8, false);
  }

  static double(x: number): ByteEnum {
    return (int32FromDouble(x) & 0xFF) as ByteEnum;
  }

  static checkedDouble(x: number): ByteEnum {
    return Number(checkedNarrow(BigInt(int32FromDoubleChecked(x)), 8, false)) as ByteEnum;
  }

  static long(x: bigint): ByteEnum {
    return Number(BigInt.asUintN(8, x)) as ByteEnum;
  }

  static checkedLong(x: bigint): ByteEnum {
    return Number(checkedNarrow(x, 8, false)) as ByteEnum;
  }

  static masked(x: number): ByteEnum {
    return (x & 255) as ByteEnum;
  }

  static widened(x: number): IntEnum {
    return x as IntEnum;
  }

  static outOfEnum(x: ShortEnum): number {
    return x & 0xFF;
  }

  static checkedOutOfEnum(x: ShortEnum): number {
    return checkedNarrowNumber(x, 8, false);
  }
}
