/**
 * Conversions between `System.Numerics.BigInteger`, which the compiler maps
 * to `bigint`, and the other numeric types (spec 012 FR-003), with .NET's
 * results and messages. Every explicit `BigInteger` conversion checks its
 * range in any context. .NET keeps a value above `int.MinValue` and up to
 * `int.MaxValue` inline and converts it with a checked cast, so some
 * out-of-range values report the generic overflow message instead of the
 * per-type one; the helpers reproduce that split.
 */
import { OverflowException } from "./exceptions/index.ts";
import { checkedNarrow } from "./int64.ts";

const INT32_MIN = -2147483648n;
const INT32_MAX = 2147483647n;
const UINT32_MAX = 4294967295n;

/** The decimal.js-free bounds of `decimal`: ±(2^96 − 1). */
const DECIMAL_MAX = 79228162514264337593543950335n;

/** A double holds 53 significant bits. */
const DOUBLE_SIGNIFICAND_BITS = 53;

/** .NET gives a `BigInteger` of more than 1024 bits an infinite `double`. */
const MAX_FINITE_DOUBLE_BITS = 1024;

const INT32_RANGE_MESSAGE = "Value was either too large or too small for an Int32.";
const UINT32_RANGE_MESSAGE = "Value was either too large or too small for a UInt32.";
const DECIMAL_RANGE_MESSAGE = "Value was either too large or too small for a Decimal.";
const NOT_A_NUMBER_MESSAGE = "The value is not a number.";
const INFINITY_MESSAGE = "BigInteger cannot represent infinity.";

/** The decimal.js surface {@link bigIntegerFromDecimal} reads. */
type TruncatableDecimal = {
  trunc(): { toFixed(): string };
};

/**
 * `(int)b`.
 *
 * @throws OverflowException outside the `int` range: the generic message for
 *   a value in [2^31, 2^32), as .NET's checked cast of one 32-bit word does,
 *   and "Value was either too large or too small for an Int32." otherwise.
 */
export function int32FromBigInteger(value: bigint): number {
  if (value >= INT32_MIN && value <= INT32_MAX) {
    return Number(value);
  }

  const oneUnsignedWord = value > INT32_MAX && value <= UINT32_MAX;

  throw oneUnsignedWord ? new OverflowException() : new OverflowException(INT32_RANGE_MESSAGE);
}

/**
 * `(uint)b`.
 *
 * @throws OverflowException outside the `uint` range: the generic message for
 *   a negative value .NET holds inline, "Value was either too large or too
 *   small for a UInt32." otherwise.
 */
export function uint32FromBigInteger(value: bigint): number {
  if (value >= 0n && value <= UINT32_MAX) {
    return Number(value);
  }

  const negativeHeldInline = value < 0n && value > INT32_MIN;

  throw negativeHeldInline ? new OverflowException() : new OverflowException(UINT32_RANGE_MESSAGE);
}

/**
 * `(short)b`, `(ushort)b`, `(sbyte)b`, `(byte)b` and `(char)b` (as its code):
 * .NET converts to `int` first, then checks the narrower range.
 *
 * @throws OverflowException as {@link int32FromBigInteger} does, then with the generic message.
 */
export function narrowFromBigInteger(value: bigint, bits: 8 | 16, signed: boolean): number {
  return Number(checkedNarrow(BigInt(int32FromBigInteger(value)), bits, signed));
}

/**
 * `(double)b` as .NET computes it: the top 53 significant bits, the rest
 * truncated (not rounded), and an infinity beyond 1024 bits. `(float)b` is
 * `Math.fround` of it, as .NET rounds through `double`.
 */
export function doubleFromBigInteger(value: bigint): number {
  const negative = value < 0n;
  const magnitude = negative ? -value : value;
  const bitLength = magnitude.toString(2).length;

  if (bitLength > MAX_FINITE_DOUBLE_BITS) {
    return negative ? Number.NEGATIVE_INFINITY : Number.POSITIVE_INFINITY;
  }

  const dropped = BigInt(Math.max(bitLength - DOUBLE_SIGNIFICAND_BITS, 0));
  const truncated = Number((magnitude >> dropped) << dropped);

  return negative ? -truncated : truncated;
}

/**
 * The digits of `(decimal)b`, for `new Decimal(…)`.
 *
 * @throws OverflowException("Value was either too large or too small for a Decimal.") beyond ±(2^96 − 1).
 */
export function decimalTextFromBigInteger(value: bigint): string {
  if (value > DECIMAL_MAX || value < -DECIMAL_MAX) {
    throw new OverflowException(DECIMAL_RANGE_MESSAGE);
  }

  return value.toString();
}

/**
 * `(BigInteger)d` and `(BigInteger)f`: truncates toward zero, exactly.
 *
 * @throws OverflowException for NaN ("The value is not a number.") and ±Infinity ("BigInteger cannot represent infinity.").
 */
export function bigIntegerFromDouble(value: number): bigint {
  if (Number.isNaN(value)) {
    throw new OverflowException(NOT_A_NUMBER_MESSAGE);
  }

  if (!Number.isFinite(value)) {
    throw new OverflowException(INFINITY_MESSAGE);
  }

  return BigInt(Math.trunc(value));
}

/** `(BigInteger)m`: truncates toward zero; every `decimal` fits. */
export function bigIntegerFromDecimal(value: TruncatableDecimal): bigint {
  return BigInt(value.trunc().toFixed());
}
