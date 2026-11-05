import { describe, expect, test } from "bun:test";
import { Decimal } from "decimal.js";
import { DivideByZeroException, OverflowException } from "#/system/exceptions/index.ts";
import {
  INT64_MAX,
  INT64_MIN,
  UINT64_MAX,
  checkedInt64,
  checkedNarrow,
  checkedUInt64,
  divInt64,
  divUInt64,
  int32FromDouble,
  int32FromDoubleChecked,
  int64FromBigInteger,
  int64FromDecimal,
  int64FromDouble,
  int64FromDoubleChecked,
  int64ToSingle,
  mapNullable,
  remInt64,
  remUInt64,
  uint32FromDouble,
  uint32FromDoubleChecked,
  uint64FromBigInteger,
  uint64FromDecimal,
  uint64FromDouble,
  uint64FromDoubleChecked,
} from "#/system/int64.ts";

/**
 * Every expected value and message below was printed by the same C# conversion on
 * .NET 10.0.12 (`unchecked` / `checked` casts, `(float)`, `(long)m`, `(long)b`).
 */
const OVERFLOW = "Arithmetic operation resulted in an overflow.";
const INT64_RANGE = "Value was either too large or too small for an Int64.";
const UINT64_RANGE = "Value was either too large or too small for a UInt64.";

/** Marks a row whose C# conversion throws OverflowException. */
const THROWS = "throws";

type Outcome<T> = T | typeof THROWS;

function expectOverflow(convert: () => unknown, message: string): void {
  try {
    convert();
  } catch (error) {
    expect(error).toBeInstanceOf(OverflowException);
    expect((error as OverflowException).message).toBe(message);

    return;
  }

  throw new Error("expected an OverflowException");
}

function expectOutcome<T>(convert: () => T, outcome: Outcome<T>, message: string): void {
  if (outcome === THROWS) {
    expectOverflow(convert, message);

    return;
  }

  expect(convert()).toBe(outcome);
}

describe("ranges", () => {
  test("are the .NET limits", () => {
    expect(INT64_MIN).toBe(-(2n ** 63n));
    expect(INT64_MAX).toBe(2n ** 63n - 1n);
    expect(UINT64_MAX).toBe(2n ** 64n - 1n);
  });
});

describe("checkedInt64 / checkedUInt64", () => {
  test("return the value inside the range", () => {
    expect(checkedInt64(INT64_MIN)).toBe(INT64_MIN);
    expect(checkedInt64(INT64_MAX)).toBe(INT64_MAX);
    expect(checkedUInt64(0n)).toBe(0n);
    expect(checkedUInt64(UINT64_MAX)).toBe(UINT64_MAX);
  });

  test("throw OverflowException one past each end, like checked(long.MaxValue + 1)", () => {
    expectOverflow(() => checkedInt64(INT64_MAX + 1n), OVERFLOW);
    expectOverflow(() => checkedInt64(INT64_MIN - 1n), OVERFLOW);
    expectOverflow(() => checkedUInt64(-1n), OVERFLOW);
    expectOverflow(() => checkedUInt64(UINT64_MAX + 1n), OVERFLOW);
  });
});

describe("checkedNarrow", () => {
  type NarrowRow = [bigint, Outcome<bigint>[]];

  /** Columns: sbyte, byte, short, ushort, int, uint (checked casts from long). */
  const rows: NarrowRow[] = [
    [127n, [127n, 127n, 127n, 127n, 127n, 127n]],
    [128n, [THROWS, 128n, 128n, 128n, 128n, 128n]],
    [-128n, [-128n, THROWS, -128n, THROWS, -128n, THROWS]],
    [-129n, [THROWS, THROWS, -129n, THROWS, -129n, THROWS]],
    [255n, [THROWS, 255n, 255n, 255n, 255n, 255n]],
    [256n, [THROWS, THROWS, 256n, 256n, 256n, 256n]],
    [32767n, [THROWS, THROWS, 32767n, 32767n, 32767n, 32767n]],
    [32768n, [THROWS, THROWS, THROWS, 32768n, 32768n, 32768n]],
    [-32769n, [THROWS, THROWS, THROWS, THROWS, -32769n, THROWS]],
    [65535n, [THROWS, THROWS, THROWS, 65535n, 65535n, 65535n]],
    [65536n, [THROWS, THROWS, THROWS, THROWS, 65536n, 65536n]],
    [2147483647n, [THROWS, THROWS, THROWS, THROWS, 2147483647n, 2147483647n]],
    [2147483648n, [THROWS, THROWS, THROWS, THROWS, THROWS, 2147483648n]],
    [-2147483649n, [THROWS, THROWS, THROWS, THROWS, THROWS, THROWS]],
    [4294967295n, [THROWS, THROWS, THROWS, THROWS, THROWS, 4294967295n]],
    [4294967296n, [THROWS, THROWS, THROWS, THROWS, THROWS, THROWS]],
    [-1n, [-1n, THROWS, -1n, THROWS, -1n, THROWS]],
  ];

  const targets: [8 | 16 | 32, boolean][] = [
    [8, true],
    [8, false],
    [16, true],
    [16, false],
    [32, true],
    [32, false],
  ];

  test.each(rows)("%p narrows as C# checked casts do", (value, outcomes) => {
    targets.forEach(([bits, signed], index) => {
      expectOutcome(() => checkedNarrow(value, bits, signed), outcomes[index]!, OVERFLOW);
    });
  });
});

describe("mapNullable", () => {
  test("gives null for null and undefined without calling convert", () => {
    const convert = (value: number) => {
      throw new Error(`called with ${value}`);
    };

    expect(mapNullable(null, convert)).toBeNull();
    expect(mapNullable(undefined, convert)).toBeNull();
  });

  test("converts any other value once, zero included", () => {
    const calls: number[] = [];

    const convert = (value: number) => {
      calls.push(value);

      return BigInt(value);
    };

    expect(mapNullable(0, convert)).toBe(0n);
    expect(mapNullable(5, convert)).toBe(5n);
    expect(calls).toEqual([0, 5]);
  });
});

describe("double to 64 and 32 bits", () => {
  /** Columns: long, checked long, ulong, checked ulong, int, checked int, uint, checked uint. */
  type DoubleRow = [
    number,
    Outcome<bigint>,
    Outcome<bigint>,
    Outcome<bigint>,
    Outcome<bigint>,
    Outcome<number>,
    Outcome<number>,
    Outcome<number>,
    Outcome<number>,
  ];

  const T = THROWS;
  const LONG_MAX = 9223372036854775807n;
  const LONG_MIN = -9223372036854775808n;
  const ULONG_MAX = 18446744073709551615n;
  const INT_MAX = 2147483647;
  const INT_MIN = -2147483648;
  const UINT_MAX = 4294967295;

  // biome-ignore format: one C# probe row per line
  const rows: DoubleRow[] = [
    [0, 0n, 0n, 0n, 0n, 0, 0, 0, 0],
    [-0, 0n, 0n, 0n, 0n, 0, 0, 0, 0],
    [1, 1n, 1n, 1n, 1n, 1, 1, 1, 1],
    [-1, -1n, -1n, 0n, T, -1, -1, 0, T],
    [0.5, 0n, 0n, 0n, 0n, 0, 0, 0, 0],
    [-0.5, 0n, 0n, 0n, 0n, 0, 0, 0, 0],
    [-0.9, 0n, 0n, 0n, 0n, 0, 0, 0, 0],
    [3.7, 3n, 3n, 3n, 3n, 3, 3, 3, 3],
    [-3.7, -3n, -3n, 0n, T, -3, -3, 0, T],
    [300.5, 300n, 300n, 300n, 300n, 300, 300, 300, 300],
    [2147483647, 2147483647n, 2147483647n, 2147483647n, 2147483647n, INT_MAX, INT_MAX, 2147483647, 2147483647],
    [2147483647.5, 2147483647n, 2147483647n, 2147483647n, 2147483647n, INT_MAX, INT_MAX, 2147483647, 2147483647],
    [2147483648, 2147483648n, 2147483648n, 2147483648n, 2147483648n, INT_MAX, T, 2147483648, 2147483648],
    [-2147483648, -2147483648n, -2147483648n, 0n, T, INT_MIN, INT_MIN, 0, T],
    [-2147483648.5, -2147483648n, -2147483648n, 0n, T, INT_MIN, INT_MIN, 0, T],
    [-2147483649, -2147483649n, -2147483649n, 0n, T, INT_MIN, T, 0, T],
    [4294967295, 4294967295n, 4294967295n, 4294967295n, 4294967295n, INT_MAX, T, UINT_MAX, UINT_MAX],
    [4294967295.9, 4294967295n, 4294967295n, 4294967295n, 4294967295n, INT_MAX, T, UINT_MAX, UINT_MAX],
    [4294967296, 4294967296n, 4294967296n, 4294967296n, 4294967296n, INT_MAX, T, UINT_MAX, T],
    [1e10, 10000000000n, 10000000000n, 10000000000n, 10000000000n, INT_MAX, T, UINT_MAX, T],
    [2 ** 53 - 1, 9007199254740991n, 9007199254740991n, 9007199254740991n, 9007199254740991n, INT_MAX, T, UINT_MAX, T],
    [2 ** 53, 9007199254740992n, 9007199254740992n, 9007199254740992n, 9007199254740992n, INT_MAX, T, UINT_MAX, T],
    [-(2 ** 53), -9007199254740992n, -9007199254740992n, 0n, T, INT_MIN, T, 0, T],
    [2 ** 63, LONG_MAX, T, 9223372036854775808n, 9223372036854775808n, INT_MAX, T, UINT_MAX, T],
    [-(2 ** 63), LONG_MIN, LONG_MIN, 0n, T, INT_MIN, T, 0, T],
    [-9223372036854777856, LONG_MIN, T, 0n, T, INT_MIN, T, 0, T],
    [18446744073709549568, LONG_MAX, T, 18446744073709549568n, 18446744073709549568n, INT_MAX, T, UINT_MAX, T],
    [2 ** 64, LONG_MAX, T, ULONG_MAX, T, INT_MAX, T, UINT_MAX, T],
    [1e19, LONG_MAX, T, 10000000000000000000n, 10000000000000000000n, INT_MAX, T, UINT_MAX, T],
    [-1e19, LONG_MIN, T, 0n, T, INT_MIN, T, 0, T],
    [1e20, LONG_MAX, T, ULONG_MAX, T, INT_MAX, T, UINT_MAX, T],
    [Infinity, LONG_MAX, T, ULONG_MAX, T, INT_MAX, T, UINT_MAX, T],
    [-Infinity, LONG_MIN, T, 0n, T, INT_MIN, T, 0, T],
    [NaN, 0n, T, 0n, T, 0, T, 0, T],
  ];

  test.each(rows)(
    "%p converts as C# casts do",
    (value, int64, int64Checked, uint64, uint64Checked, int32, int32Checked, uint32, uint32Checked) => {
      expectOutcome(() => int64FromDouble(value), int64, OVERFLOW);
      expectOutcome(() => int64FromDoubleChecked(value), int64Checked, OVERFLOW);
      expectOutcome(() => uint64FromDouble(value), uint64, OVERFLOW);
      expectOutcome(() => uint64FromDoubleChecked(value), uint64Checked, OVERFLOW);
      expectOutcome(() => int32FromDouble(value), int32, OVERFLOW);
      expectOutcome(() => int32FromDoubleChecked(value), int32Checked, OVERFLOW);
      expectOutcome(() => uint32FromDouble(value), uint32, OVERFLOW);
      expectOutcome(() => uint32FromDoubleChecked(value), uint32Checked, OVERFLOW);
    },
  );

  test("never yields negative zero, which C# integers do not have", () => {
    const negativeZeroSources = [-0, -0.5, -0.9];

    for (const value of negativeZeroSources) {
      expect(Object.is(int32FromDouble(value), 0)).toBe(true);
      expect(Object.is(int32FromDoubleChecked(value), 0)).toBe(true);
      expect(Object.is(uint32FromDouble(value), 0)).toBe(true);
      expect(Object.is(uint32FromDoubleChecked(value), 0)).toBe(true);
    }
  });

  test("keeps the low bits of the saturated int for a narrower target, as (byte)300.5 is 44", () => {
    expect(int32FromDouble(300.5) & 0xff).toBe(44);
    expect(int32FromDouble(-3.7) & 0xff).toBe(253);
    expect(int32FromDouble(1e10) & 0xff).toBe(255);
    expect(int32FromDouble(-1e19) & 0xff).toBe(0);
    expect(int32FromDouble(NaN) & 0xff).toBe(0);
  });
});

describe("int64ToSingle", () => {
  // biome-ignore format: one C# probe row per line
  const rows: [bigint, number][] = [
    [0n, 0],
    [1n, 1],
    [-1n, -1],
    [16777217n, 16777216],
    [9007199254740993n, 9007199254740992],
    [(1n << 60n) + (1n << 36n), 1.152921504606847e18],
    [(1n << 60n) + (1n << 36n) + 1n, 1.1529216420458004e18],
    [-((1n << 60n) + (1n << 36n) + 1n), -1.1529216420458004e18],
    [(1n << 62n) + (1n << 38n), 4.611686018427388e18],
    [(1n << 62n) + (1n << 38n) + 1n, 4.611686568183202e18],
    [INT64_MAX, 9.223372036854776e18],
    [INT64_MIN, -9.223372036854776e18],
    [1n << 63n, 9.223372036854776e18],
    [(1n << 63n) + (1n << 39n), 9.223372036854776e18],
    [(1n << 63n) + (1n << 39n) + 1n, 9.223373136366404e18],
    [UINT64_MAX, 1.8446744073709552e19],
  ];

  test.each(rows)("(float)%p rounds once to %p", (value, single) => {
    expect(int64ToSingle(value)).toBe(single);
  });

  test("differs from the double-rounded Math.fround(Number(x)) on a halfway value (R5)", () => {
    const value = (1n << 60n) + (1n << 36n) + 1n;

    expect(Math.fround(Number(value))).toBe(2 ** 60);
    expect(int64ToSingle(value)).toBe(1152921642045800448);
  });
});

describe("decimal to 64 bits", () => {
  /** Columns: (long)m, (ulong)m; both throw in every context. */
  // biome-ignore format: one C# probe row per line
  const rows: [string, Outcome<bigint>, Outcome<bigint>][] = [
    ["0", 0n, 0n],
    ["-0.9", 0n, 0n],
    ["0.9", 0n, 0n],
    ["3.7", 3n, 3n],
    ["-3.7", -3n, THROWS],
    ["-1", -1n, THROWS],
    ["9223372036854775807.9", INT64_MAX, 9223372036854775807n],
    ["9223372036854775808", THROWS, 9223372036854775808n],
    ["-9223372036854775808.9", INT64_MIN, THROWS],
    ["-9223372036854775809", THROWS, THROWS],
    ["18446744073709551615.5", THROWS, UINT64_MAX],
    ["18446744073709551616", THROWS, THROWS],
    ["79228162514264337593543950335", THROWS, THROWS],
    ["-79228162514264337593543950335", THROWS, THROWS],
  ];

  test.each(rows)("%pm converts as C# does", (text, int64, uint64) => {
    expectOutcome(() => int64FromDecimal(new Decimal(text)), int64, INT64_RANGE);
    expectOutcome(() => uint64FromDecimal(new Decimal(text)), uint64, UINT64_RANGE);
  });

  test("treats a NaN or infinite decimal.js value as out of range", () => {
    expectOverflow(() => int64FromDecimal(new Decimal(NaN)), INT64_RANGE);
    expectOverflow(() => uint64FromDecimal(new Decimal(Infinity)), UINT64_RANGE);
  });
});

describe("BigInteger to 64 bits", () => {
  /** Columns: (long)b, (ulong)b and the message .NET reports; both throw in every context. */
  // biome-ignore format: one C# probe row per line
  const rows: [bigint, Outcome<bigint>, Outcome<bigint>, string][] = [
    [0n, 0n, 0n, UINT64_RANGE],
    [-1n, -1n, THROWS, OVERFLOW],
    [-2147483647n, -2147483647n, THROWS, OVERFLOW],
    [-2147483648n, -2147483648n, THROWS, UINT64_RANGE],
    [-2147483649n, -2147483649n, THROWS, UINT64_RANGE],
    [INT64_MAX, INT64_MAX, INT64_MAX, UINT64_RANGE],
    [INT64_MAX + 1n, THROWS, INT64_MAX + 1n, UINT64_RANGE],
    [INT64_MIN, INT64_MIN, THROWS, UINT64_RANGE],
    [INT64_MIN - 1n, THROWS, THROWS, UINT64_RANGE],
    [UINT64_MAX, THROWS, UINT64_MAX, UINT64_RANGE],
    [UINT64_MAX + 1n, THROWS, THROWS, UINT64_RANGE],
  ];

  test.each(rows)("%p converts as C# does", (value, int64, uint64, uint64Message) => {
    expectOutcome(() => int64FromBigInteger(value), int64, INT64_RANGE);
    expectOutcome(() => uint64FromBigInteger(value), uint64, uint64Message);
  });
});

describe("division and remainder", () => {
  const DIVIDE_BY_ZERO = "Attempted to divide by zero.";

  function expectDivideByZero(divide: () => unknown): void {
    expect(divide).toThrow(DivideByZeroException);
    expect(divide).toThrow(DIVIDE_BY_ZERO);
  }

  test("truncate toward zero, and the remainder takes the dividend's sign, like -7L / 2L", () => {
    expect(divInt64(-7n, 2n)).toBe(-3n);
    expect(remInt64(-7n, 2n)).toBe(-1n);
    expect(divInt64(7n, -2n)).toBe(-3n);
    expect(remInt64(7n, -2n)).toBe(1n);
    expect(divInt64(INT64_MIN, 1n)).toBe(INT64_MIN);
    expect(remInt64(INT64_MAX, -1n)).toBe(0n);
    expect(divUInt64(UINT64_MAX, 2n)).toBe(INT64_MAX);
    expect(remUInt64(UINT64_MAX, 10n)).toBe(5n);
  });

  test("throw DivideByZeroException on a zero divisor", () => {
    expectDivideByZero(() => divInt64(1n, 0n));
    expectDivideByZero(() => remInt64(INT64_MIN, 0n));
    expectDivideByZero(() => divUInt64(UINT64_MAX, 0n));
    expectDivideByZero(() => remUInt64(0n, 0n));
  });

  test("throw OverflowException for long.MinValue by -1, in every context", () => {
    expectOverflow(() => divInt64(INT64_MIN, -1n), OVERFLOW);
    expectOverflow(() => remInt64(INT64_MIN, -1n), OVERFLOW);
  });

  test("give null for a null or undefined operand, like a lifted long? division", () => {
    expect(divInt64(null, 0n)).toBeNull();
    expect(remInt64(5n, undefined)).toBeNull();
    expect(divUInt64(undefined, 2n)).toBeNull();
    expect(remUInt64(5n, null)).toBeNull();
  });
});
