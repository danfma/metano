/**
 * The `System.Math` and `INumber` statics over C#'s 64-bit integers, which the
 * compiler maps to `bigint`: `Abs`, `Max`, `Min`, `Sign`, `Clamp`, `DivRem`,
 * `BigMul` and `CompareTo`, with .NET's exceptions and messages. `Math.max`
 * and its siblings throw a `TypeError` for a `bigint`, so the compiler routes
 * every 64-bit overload here.
 */
import { ArgumentException, OverflowException } from "./exceptions/index.ts";
import { divInt64, divUInt64, INT64_MIN, remInt64, remUInt64 } from "./int64.ts";

/** .NET's message for `Math.Abs(long.MinValue)`. */
const NEGATE_MIN_VALUE_MESSAGE =
  "Negating the minimum value of a twos complement number is invalid.";

/** The low 64 bits of a 128-bit product. */
const LOW_64_BITS = 0xffff_ffff_ffff_ffffn;

/**
 * `Math.Abs(long)` / `long.Abs`.
 *
 * @throws OverflowException for `long.MinValue`, whose magnitude does not fit `long`.
 */
export function absInt64(value: bigint): bigint {
  if (value === INT64_MIN) {
    throw new OverflowException(NEGATE_MIN_VALUE_MESSAGE);
  }

  return value < 0n ? -value : value;
}

/** `Math.Max(long, long)` / `long.Max`. */
export function maxInt64(left: bigint, right: bigint): bigint {
  return left >= right ? left : right;
}

/** `Math.Min(long, long)` / `long.Min`. */
export function minInt64(left: bigint, right: bigint): bigint {
  return left <= right ? left : right;
}

/**
 * `Math.Max(ulong, ulong)` / `ulong.Max`. The unsigned helpers share the
 * signed bodies (a `bigint` orders the same either way) but stay distinct
 * exports, one per .NET member the compiler maps.
 */
export function maxUInt64(left: bigint, right: bigint): bigint {
  return left >= right ? left : right;
}

/** `Math.Min(ulong, ulong)` / `ulong.Min`. */
export function minUInt64(left: bigint, right: bigint): bigint {
  return left <= right ? left : right;
}

/** `Math.Sign(long)` / `long.Sign` / `ulong.Sign`: `-1`, `0` or `1`. */
export function signInt64(value: bigint): number {
  return compareInt64(value, 0n);
}

/**
 * `Math.Clamp(long, long, long)` / `long.Clamp`.
 *
 * @throws ArgumentException when `min` is greater than `max`.
 */
export function clampInt64(value: bigint, min: bigint, max: bigint): bigint {
  return clamp(value, min, max);
}

/**
 * `Math.Clamp(ulong, ulong, ulong)` / `ulong.Clamp`.
 *
 * @throws ArgumentException when `min` is greater than `max`.
 */
export function clampUInt64(value: bigint, min: bigint, max: bigint): bigint {
  return clamp(value, min, max);
}

/**
 * `Math.DivRem(long, long)` / `long.DivRem` (and the `out` form,
 * `Math.DivRem(long, long, out long)`): the quotient and the remainder.
 * A mutable tuple, because C#'s `(long, long)` maps to `[bigint, bigint]`.
 *
 * @throws DivideByZeroException when `divisor` is zero.
 * @throws OverflowException for `long.MinValue` by `-1`.
 */
export function divRemInt64(
  dividend: bigint,
  divisor: bigint,
): [quotient: bigint, remainder: bigint] {
  return [divInt64(dividend, divisor), remInt64(dividend, divisor)];
}

/**
 * `Math.DivRem(ulong, ulong)` / `ulong.DivRem`: the quotient and the remainder.
 *
 * @throws DivideByZeroException when `divisor` is zero.
 */
export function divRemUInt64(
  dividend: bigint,
  divisor: bigint,
): [quotient: bigint, remainder: bigint] {
  return [divUInt64(dividend, divisor), remUInt64(dividend, divisor)];
}

/**
 * `Math.BigMul(long, long, out long low)`: the high 64 bits of the exact
 * 128-bit product, then its low 64 bits reinterpreted as `long`.
 */
export function bigMulInt64(left: bigint, right: bigint): [high: bigint, low: bigint] {
  const product = left * right;

  return [product >> 64n, BigInt.asIntN(64, product)];
}

/** `Math.BigMul(ulong, ulong, out ulong low)`: the high and low 64 bits of the 128-bit product. */
export function bigMulUInt64(left: bigint, right: bigint): [high: bigint, low: bigint] {
  const product = left * right;

  return [product >> 64n, product & LOW_64_BITS];
}

/** `long.CompareTo(long)` and `ulong.CompareTo(ulong)`: `-1`, `0` or `1`. */
export function compareInt64(left: bigint, right: bigint): number {
  if (left < right) {
    return -1;
  }

  return left > right ? 1 : 0;
}

function clamp(value: bigint, min: bigint, max: bigint): bigint {
  if (min > max) {
    throw new ArgumentException(`'${min}' cannot be greater than ${max}.`);
  }

  return maxInt64(min, minInt64(value, max));
}
