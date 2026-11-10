/**
 * Runtime helpers for C#'s 64-bit integers, which the compiler maps to `bigint`:
 * range checks, checked narrowing, lifted conversions, the conversions from
 * `double`, `decimal` and `BigInteger` with .NET 9+ semantics, and division and
 * remainder with .NET's exceptions. Every export is a function or a constant,
 * so generated code imports only what it calls.
 */
import { DivideByZeroException, OverflowException } from "./exceptions/index.ts";

/** `long.MinValue`. */
export const INT64_MIN = -9223372036854775808n;

/** `long.MaxValue`. */
export const INT64_MAX = 9223372036854775807n;

/** `ulong.MaxValue`. */
export const UINT64_MAX = 18446744073709551615n;

/** .NET's message for a `decimal` or `BigInteger` that does not fit `long`. */
const INT64_RANGE_MESSAGE = "Value was either too large or too small for an Int64.";

/** .NET's message for a `decimal` or `BigInteger` that does not fit `ulong`. */
const UINT64_RANGE_MESSAGE = "Value was either too large or too small for a UInt64.";

/** 2^31, 2^32, 2^63 and 2^64: the exclusive bounds of the integer ranges, as doubles. */
const TWO_TO_THE_31 = 2 ** 31;
const TWO_TO_THE_32 = 2 ** 32;
const TWO_TO_THE_63 = 2 ** 63;
const TWO_TO_THE_64 = 2 ** 64;

const INT32_MIN = -2147483648;
const INT32_MAX = 2147483647;
const UINT32_MAX = 4294967295;

/** `BigInteger` keeps values above `int.MinValue` inline (see {@link uint64FromBigInteger}). */
const INT32_MIN_BIGINT = -2147483648n;

/** The significand width of a double; {@link int64ToSingle} rounds to it. */
const DOUBLE_SIGNIFICAND_BITS = 53;

/** decimal.js prints an integral value as an optional minus and digits. */
const INTEGER_TEXT = /^-?\d+$/;

/**
 * The decimal.js surface the `decimal` conversions read. Structural, so the
 * runtime never imports decimal.js itself.
 */
type DecimalLike = {
  trunc(): { toFixed(): string };
};

/** Returns `value` when it fits `long`; throws OverflowException otherwise. */
export function checkedInt64(value: bigint): bigint {
  return requireRange(value, INT64_MIN, INT64_MAX);
}

/** Returns `value` when it fits `ulong` (0 … 2^64 − 1); throws OverflowException otherwise. */
export function checkedUInt64(value: bigint): bigint {
  return requireRange(value, 0n, UINT64_MAX);
}

/**
 * Checked narrowing of a 64-bit value to a `bits`-wide integer (`sbyte` / `byte`,
 * `short` / `ushort`, `int` / `uint`): returns `value` when it fits, throws
 * OverflowException otherwise.
 */
export function checkedNarrow(value: bigint, bits: 8 | 16 | 32, signed: boolean): bigint {
  const width = BigInt(bits);
  const min = signed ? -(1n << (width - 1n)) : 0n;
  const max = signed ? (1n << (width - 1n)) - 1n : (1n << width) - 1n;

  return requireRange(value, min, max);
}

/**
 * Returns `value` when it fits `int`; throws OverflowException otherwise. `int` arithmetic
 * in a checked context computes in a `number` and checks the result here: a sum or a
 * difference of two 32-bit values is exact in a double, and a product too large to be exact
 * is far outside the range, so the test decides as .NET does.
 */
export function checkedInt32(value: number): number {
  return requireNumberRange(value, INT32_MIN, INT32_MAX);
}

/** Returns `value` when it fits `uint` (0 … 2^32 − 1); throws OverflowException otherwise. */
export function checkedUInt32(value: number): number {
  return requireNumberRange(value, 0, UINT32_MAX);
}

/**
 * Checked narrowing of an integer held in a `number` to a `bits`-wide integer (`sbyte` /
 * `byte`, `short` / `ushort`): returns `value` when it fits, throws OverflowException
 * otherwise. The `number` counterpart of {@link checkedNarrow}.
 */
export function checkedNarrowInt32(value: number, bits: 8 | 16, signed: boolean): number {
  const min = signed ? -(2 ** (bits - 1)) : 0;
  const max = signed ? 2 ** (bits - 1) - 1 : 2 ** bits - 1;

  return requireNumberRange(value, min, max);
}

/** Lifted conversion: `null` and `undefined` give `null`; any other value goes through `convert` once. */
export function mapNullable<T, R>(value: T | null | undefined, convert: (value: T) => R): R | null {
  return value == null ? null : convert(value);
}

/** Unchecked `(long)d`: truncates; NaN gives `0n`; out-of-range values saturate (.NET 9+). */
export function int64FromDouble(value: number): bigint {
  if (Number.isNaN(value)) {
    return 0n;
  }

  if (value >= TWO_TO_THE_63) {
    return INT64_MAX;
  }

  if (value <= -TWO_TO_THE_63) {
    return INT64_MIN;
  }

  return BigInt(Math.trunc(value));
}

/** Checked `(long)d`: truncates, then throws OverflowException outside the range; NaN and ±Infinity throw. */
export function int64FromDoubleChecked(value: number): bigint {
  return BigInt(checkedTruncate(value, -TWO_TO_THE_63, TWO_TO_THE_63));
}

/** Unchecked `(ulong)d`: truncates; NaN and negatives give `0n`; `≥ 2^64` gives `UINT64_MAX` (.NET 9+). */
export function uint64FromDouble(value: number): bigint {
  if (Number.isNaN(value) || value < 1) {
    return 0n;
  }

  if (value >= TWO_TO_THE_64) {
    return UINT64_MAX;
  }

  return BigInt(Math.trunc(value));
}

/** Checked `(ulong)d`: truncates first, so `(ulong)-0.9` is `0n`, then throws OverflowException outside the range. */
export function uint64FromDoubleChecked(value: number): bigint {
  return BigInt(checkedTruncate(value, 0, TWO_TO_THE_64));
}

/** Unchecked `(int)d`: truncates, saturates, NaN gives `0` (.NET 9+). */
export function int32FromDouble(value: number): number {
  return saturatedTruncate(value, INT32_MIN, INT32_MAX);
}

/** Checked `(int)d`: truncates, then throws OverflowException outside the range; NaN and ±Infinity throw. */
export function int32FromDoubleChecked(value: number): number {
  return checkedTruncate(value, INT32_MIN, TWO_TO_THE_31);
}

/** Unchecked `(uint)d`: truncates, saturates, NaN gives `0` (.NET 9+). */
export function uint32FromDouble(value: number): number {
  return saturatedTruncate(value, 0, UINT32_MAX);
}

/** Checked `(uint)d`: truncates, then throws OverflowException outside the range; NaN and ±Infinity throw. */
export function uint32FromDoubleChecked(value: number): number {
  return checkedTruncate(value, 0, TWO_TO_THE_32);
}

/**
 * `(float)l` and `(float)u` over the whole `long` and `ulong` range, rounded
 * once to the nearest single (ties to even). `Math.fround(Number(x))` rounds
 * twice and differs from C# on halfway values; rounding to odd at 53 bits
 * first makes the second rounding exact.
 */
export function int64ToSingle(value: bigint): number {
  const negative = value < 0n;
  const single = Math.fround(roundToOddDouble(negative ? -value : value));

  return negative ? -single : single;
}

/** `(long)m`: truncates toward zero; throws OverflowException outside the range, in every context. */
export function int64FromDecimal(value: DecimalLike): bigint {
  const integer = truncatedDecimal(value, INT64_RANGE_MESSAGE);

  return requireRange(integer, INT64_MIN, INT64_MAX, INT64_RANGE_MESSAGE);
}

/** `(ulong)m`: truncates toward zero, so `-0.9m` gives `0n`; throws OverflowException outside the range. */
export function uint64FromDecimal(value: DecimalLike): bigint {
  const integer = truncatedDecimal(value, UINT64_RANGE_MESSAGE);

  return requireRange(integer, 0n, UINT64_MAX, UINT64_RANGE_MESSAGE);
}

/** `(long)b`: throws OverflowException outside the range, in every context. */
export function int64FromBigInteger(value: bigint): bigint {
  return requireRange(value, INT64_MIN, INT64_MAX, INT64_RANGE_MESSAGE);
}

/**
 * `(ulong)b`: throws OverflowException outside the range, in every context.
 * .NET stores a `BigInteger` above `int.MinValue` inline and converts it with
 * a checked cast, so a negative value in that window reports the generic
 * overflow message instead of the `UInt64` one.
 */
export function uint64FromBigInteger(value: bigint): bigint {
  const heldInline = value < 0n && value > INT32_MIN_BIGINT;

  if (heldInline) {
    throw new OverflowException();
  }

  return requireRange(value, 0n, UINT64_MAX, UINT64_RANGE_MESSAGE);
}

/**
 * `long.GetHashCode()` / `ulong.GetHashCode()`: the low and high 32-bit words,
 * each read as a signed int, folded with xor. Spreading the high word keeps ids
 * and ticks that differ only there from colliding, as .NET does (`(int)v ^
 * (int)(v >> 32)`); the old low-word-only formula sent `1L << 32` and `0L` to
 * the same bucket. `system/hash-code.ts` routes every `bigint` through here.
 */
export function hashInt64(value: bigint): number {
  return Number(BigInt.asIntN(32, value)) ^ Number(BigInt.asIntN(32, value >> 32n));
}

/**
 * `l / m` on `long`: the quotient truncated toward zero; a `null` (or `undefined`)
 * operand gives `null`, as C#'s lifted `long?` division.
 *
 * @throws DivideByZeroException when `divisor` is zero.
 * @throws OverflowException for `long.MinValue / -1`, in every context, as .NET does.
 */
export function divInt64(dividend: bigint, divisor: bigint): bigint;
export function divInt64(
  dividend: bigint | null | undefined,
  divisor: bigint | null | undefined,
): bigint | null;
export function divInt64(
  dividend: bigint | null | undefined,
  divisor: bigint | null | undefined,
): bigint | null {
  if (dividend == null || divisor == null) {
    return null;
  }

  requireSignedDivision(dividend, divisor);

  return dividend / divisor;
}

/**
 * `l % m` on `long`: the remainder with the dividend's sign; a `null` (or `undefined`)
 * operand gives `null`.
 *
 * @throws DivideByZeroException when `divisor` is zero.
 * @throws OverflowException for `long.MinValue % -1`, in every context, as .NET does.
 */
export function remInt64(dividend: bigint, divisor: bigint): bigint;
export function remInt64(
  dividend: bigint | null | undefined,
  divisor: bigint | null | undefined,
): bigint | null;
export function remInt64(
  dividend: bigint | null | undefined,
  divisor: bigint | null | undefined,
): bigint | null {
  if (dividend == null || divisor == null) {
    return null;
  }

  requireSignedDivision(dividend, divisor);

  return dividend % divisor;
}

/**
 * `u / v` on `ulong`: the truncated quotient; a `null` (or `undefined`) operand gives `null`.
 *
 * @throws DivideByZeroException when `divisor` is zero.
 */
export function divUInt64(dividend: bigint, divisor: bigint): bigint;
export function divUInt64(
  dividend: bigint | null | undefined,
  divisor: bigint | null | undefined,
): bigint | null;
export function divUInt64(
  dividend: bigint | null | undefined,
  divisor: bigint | null | undefined,
): bigint | null {
  if (dividend == null || divisor == null) {
    return null;
  }

  requireNonZeroDivisor(divisor);

  return dividend / divisor;
}

/**
 * `u % v` on `ulong`: the remainder; a `null` (or `undefined`) operand gives `null`.
 *
 * @throws DivideByZeroException when `divisor` is zero.
 */
export function remUInt64(dividend: bigint, divisor: bigint): bigint;
export function remUInt64(
  dividend: bigint | null | undefined,
  divisor: bigint | null | undefined,
): bigint | null;
export function remUInt64(
  dividend: bigint | null | undefined,
  divisor: bigint | null | undefined,
): bigint | null {
  if (dividend == null || divisor == null) {
    return null;
  }

  requireNonZeroDivisor(divisor);

  return dividend % divisor;
}

/** Throws what .NET throws for a signed 64-bit `/` or `%`: a zero divisor, or `long.MinValue` by `-1`. */
function requireSignedDivision(dividend: bigint, divisor: bigint): void {
  requireNonZeroDivisor(divisor);

  if (dividend === INT64_MIN && divisor === -1n) {
    throw new OverflowException();
  }
}

/** Throws DivideByZeroException, as every .NET integer `/` and `%` does, when `divisor` is zero. */
function requireNonZeroDivisor(divisor: bigint): void {
  if (divisor === 0n) {
    throw new DivideByZeroException();
  }
}

function requireRange(value: bigint, min: bigint, max: bigint, message?: string): bigint {
  if (value < min || value > max) {
    throw new OverflowException(message);
  }

  return value;
}

function requireNumberRange(value: number, min: number, max: number): number {
  const fits = value >= min && value <= max;

  // NaN fails both comparisons, so it throws.
  if (!fits) {
    throw new OverflowException();
  }

  // C# integers have no negative zero: `+ 0` turns the `-0` of `checked(-x)` into `0`.
  return value + 0;
}

/** Truncates `value` and saturates it to `[min, max]`; NaN gives `0`, never `-0`. */
function saturatedTruncate(value: number, min: number, max: number): number {
  if (Number.isNaN(value)) {
    return 0;
  }

  // C# integers have no negative zero: `+ 0` turns the `-0` of `(int)-0.5` into `0`.
  return Math.trunc(Math.min(Math.max(value, min), max)) + 0;
}

/** Truncates `value` and throws OverflowException unless it lies in `[min, maxExclusive)`. */
function checkedTruncate(value: number, min: number, maxExclusive: number): number {
  const truncated = Math.trunc(value);
  const fits = truncated >= min && truncated < maxExclusive;

  // NaN fails both comparisons, so it throws like ±Infinity.
  if (!fits) {
    throw new OverflowException();
  }

  return truncated + 0;
}

/** `magnitude` rounded to odd at 53 bits, as a double: the dropped bits fold into the lowest kept bit. */
function roundToOddDouble(magnitude: bigint): number {
  const droppedBits = magnitude.toString(2).length - DOUBLE_SIGNIFICAND_BITS;

  if (droppedBits <= 0) {
    return Number(magnitude);
  }

  const shift = BigInt(droppedBits);
  const kept = magnitude >> shift;
  const sticky = (magnitude & ((1n << shift) - 1n)) === 0n ? 0n : 1n;

  return Number(kept | sticky) * 2 ** droppedBits;
}

/** `value` truncated toward zero as a `bigint`; a NaN or infinite decimal.js value throws `message`. */
function truncatedDecimal(value: DecimalLike, message: string): bigint {
  const text = value.trunc().toFixed();

  if (!INTEGER_TEXT.test(text)) {
    throw new OverflowException(message);
  }

  return BigInt(text);
}
