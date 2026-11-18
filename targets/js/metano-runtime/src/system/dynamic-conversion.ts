/**
 * The explicit numeric casts of a C# `dynamic` value (`(int)d`, `(long)d`, `(char)d`,
 * `(decimal)d`; spec 011 FR-030). The C# runtime binder converts from the type the value
 * holds at run time, which the compiler does not know, so these helpers look at the value:
 *
 * - a `number` that is an integer in the 32-bit range (`int`, `uint` and the narrower
 *   integers hold such values) converts as an integer: wrapped to the target's width, or
 *   range-checked in a checked context;
 * - any other `number` converts as a `double`: truncated toward zero, saturated to the
 *   target's range (NaN gives 0), or range-checked in a checked context;
 * - a `bigint` (a `long`, `ulong` or `BigInteger`) converts as a 64-bit integer;
 * - a decimal.js value converts as a `decimal`: truncated, and out of range it throws
 *   `OverflowException` in every context, as .NET does;
 * - a one-character `string` is a `char`, which only the `char` target accepts;
 * - `null`, and any other value (a `string`, a `boolean`, an object), throw
 *   `InvalidCastException`, where .NET throws `RuntimeBinderException`.
 *
 * A cast to `T?` lets `null` through before it calls these helpers.
 */
import { InvalidCastException, OverflowException } from "./exceptions/index.ts";

/** The C# numeric types a `dynamic` value converts to as a `number`. */
export type DynamicNumberTarget =
  | "SByte"
  | "Byte"
  | "Int16"
  | "UInt16"
  | "Int32"
  | "UInt32"
  | "Single"
  | "Double";

/** The C# numeric types a `dynamic` value converts to as a `bigint`. */
export type DynamicBigIntTarget = "Int64" | "UInt64" | "BigInteger";

/** The decimal.js surface the conversions read; structural, so this module does not load decimal.js. */
type DecimalValue = {
  trunc(): { toFixed(): string };
  toNumber(): number;
  toFixed(): string;
};

type IntegerRange = { readonly bits: number; readonly signed: boolean; readonly name: string };

const INTEGER_RANGES: Readonly<
  Record<Exclude<DynamicNumberTarget, "Single" | "Double">, IntegerRange>
> = {
  SByte: { bits: 8, signed: true, name: "sbyte" },
  Byte: { bits: 8, signed: false, name: "byte" },
  Int16: { bits: 16, signed: true, name: "short" },
  UInt16: { bits: 16, signed: false, name: "ushort" },
  Int32: { bits: 32, signed: true, name: "int" },
  UInt32: { bits: 32, signed: false, name: "uint" },
};

const BIGINT_RANGES: Readonly<Record<"Int64" | "UInt64", IntegerRange>> = {
  Int64: { bits: 64, signed: true, name: "long" },
  UInt64: { bits: 64, signed: false, name: "ulong" },
};

const CHAR_RANGE: IntegerRange = { bits: 16, signed: false, name: "char" };

const INT32_MIN = -(2 ** 31);
const UINT32_MAX = 2 ** 32 - 1;

/**
 * `(T)d` for a `T` held in a `number`: `sbyte`, `byte`, `short`, `ushort`, `int`, `uint`,
 * `float` and `double`.
 *
 * @throws InvalidCastException for `null` and for a value that is not numeric.
 * @throws OverflowException for a value outside `T` in a checked context, and for a decimal outside `T`.
 */
export function numberFromDynamic(
  value: unknown,
  target: DynamicNumberTarget,
  checked = false,
): number {
  if (target === "Double" || target === "Single") {
    const double = doubleOf(value, target === "Double" ? "double" : "float");

    return target === "Single" ? Math.fround(double) : double;
  }

  return Number(integerOf(value, INTEGER_RANGES[target], checked));
}

/**
 * `(char)d`: a `char` (a one-character `string`) as is, or the UTF-16 code a numeric value
 * converts to as a `ushort`.
 *
 * @throws InvalidCastException for `null` and for a value that is neither a `char` nor numeric.
 * @throws OverflowException for a code outside `char` in a checked context.
 */
export function charFromDynamic(value: unknown, checked = false): string {
  if (typeof value === "string" && value.length === 1) {
    return value;
  }

  return String.fromCharCode(Number(integerOf(value, CHAR_RANGE, checked)));
}

/**
 * `(long)d`, `(ulong)d` and `(BigInteger)d`.
 *
 * @throws InvalidCastException for `null` and for a value that is not numeric.
 * @throws OverflowException for a value outside `T` in a checked context, a decimal outside
 * `T`, and a NaN or infinite `double` to `BigInteger`.
 */
export function bigintFromDynamic(
  value: unknown,
  target: DynamicBigIntTarget,
  checked = false,
): bigint {
  if (target !== "BigInteger") {
    return integerOf(value, BIGINT_RANGES[target], checked);
  }

  if (typeof value === "bigint") {
    return value;
  }

  if (isDecimalValue(value)) {
    return truncatedDecimal(value);
  }

  const double = doubleOf(value, "System.Numerics.BigInteger");

  if (!Number.isFinite(double)) {
    throw new OverflowException();
  }

  return BigInt(Math.trunc(double));
}

/** The largest magnitude a `decimal` holds, 2^96 − 1. */
const DECIMAL_MAX = 2n ** 96n - 1n;

const DECIMAL_RANGE_MESSAGE = "Value was either too large or too small for a Decimal.";

/**
 * The argument `new Decimal(…)` takes for `(decimal)d`: the `number`, or the exact digits
 * of a `bigint` or of a decimal (as text, so this module does not load decimal.js).
 *
 * @throws InvalidCastException for `null` and for a value that is not numeric.
 * @throws OverflowException for a NaN or infinite `number` and a `bigint` beyond ±(2^96 − 1).
 */
export function decimalSourceFromDynamic(value: unknown): number | string {
  if (isDecimalValue(value)) {
    return value.toFixed();
  }

  if (typeof value === "bigint") {
    if (value > DECIMAL_MAX || value < -DECIMAL_MAX) {
      throw new OverflowException(DECIMAL_RANGE_MESSAGE);
    }

    return value.toString();
  }

  const double = doubleOf(value, "decimal");

  if (!Number.isFinite(double)) {
    throw new OverflowException(DECIMAL_RANGE_MESSAGE);
  }

  return double;
}

/** The value as a `double`: a `number`, a `bigint` or a decimal. */
function doubleOf(value: unknown, targetName: string): number {
  if (typeof value === "number") {
    return value;
  }

  if (typeof value === "bigint") {
    return Number(value);
  }

  if (isDecimalValue(value)) {
    return value.toNumber();
  }

  throw cannotConvertDynamic(value, targetName);
}

/** The value converted to the integer `range` describes, as a `bigint`. */
function integerOf(value: unknown, range: IntegerRange, checked: boolean): bigint {
  if (typeof value === "bigint") {
    return checked ? requireRange(value, range) : wrapped(value, range);
  }

  if (isDecimalValue(value)) {
    return requireRange(truncatedDecimal(value), range);
  }

  if (typeof value !== "number") {
    throw cannotConvertDynamic(value, range.name);
  }

  if (isThirtyTwoBitInteger(value)) {
    const integer = BigInt(value);

    return checked ? requireRange(integer, range) : wrapped(integer, range);
  }

  return checked ? requireRange(checkedTruncation(value), range) : saturated(value, range);
}

/** Whether a `number` holds a value an `int`, a `uint` or a narrower integer can hold. */
function isThirtyTwoBitInteger(value: number): boolean {
  return Number.isInteger(value) && value >= INT32_MIN && value <= UINT32_MAX;
}

/** The low `bits` of `value`, read signed or unsigned: an unchecked integer narrowing. */
function wrapped(value: bigint, range: IntegerRange): bigint {
  return range.signed ? BigInt.asIntN(range.bits, value) : BigInt.asUintN(range.bits, value);
}

/** An unchecked `double` cast (.NET 9+): truncated, saturated to the range, NaN gives 0. */
function saturated(value: number, range: IntegerRange): bigint {
  if (Number.isNaN(value)) {
    return 0n;
  }

  const { min, max } = boundsOf(range);

  if (value <= Number(min)) {
    return min;
  }

  if (value >= Number(max)) {
    return max;
  }

  return BigInt(Math.trunc(value));
}

/** A checked `double` cast: truncated; NaN and ±Infinity overflow. */
function checkedTruncation(value: number): bigint {
  if (!Number.isFinite(value)) {
    throw new OverflowException();
  }

  return BigInt(Math.trunc(value));
}

function requireRange(value: bigint, range: IntegerRange): bigint {
  const { min, max } = boundsOf(range);

  if (value < min || value > max) {
    throw new OverflowException();
  }

  return value;
}

function boundsOf(range: IntegerRange): { min: bigint; max: bigint } {
  const span = 1n << BigInt(range.bits);

  return range.signed ? { min: -(span / 2n), max: span / 2n - 1n } : { min: 0n, max: span - 1n };
}

function truncatedDecimal(value: DecimalValue): bigint {
  return BigInt(value.trunc().toFixed());
}

function isDecimalValue(value: unknown): value is DecimalValue {
  return (
    typeof value === "object" &&
    value !== null &&
    typeof (value as Partial<DecimalValue>).trunc === "function" &&
    typeof (value as Partial<DecimalValue>).toNumber === "function"
  );
}

/** .NET's `RuntimeBinderException` text for a dynamic conversion, on an `InvalidCastException`. */
function cannotConvertDynamic(value: unknown, targetName: string): InvalidCastException {
  if (value == null) {
    return new InvalidCastException(
      `Cannot convert null to '${targetName}' because it is a non-nullable value type`,
    );
  }

  return new InvalidCastException(`Cannot convert type '${typeNameOf(value)}' to '${targetName}'`);
}

function typeNameOf(value: unknown): string {
  if (typeof value === "string") {
    return "string";
  }

  if (typeof value === "boolean") {
    return "bool";
  }

  return typeof value === "object" && value !== null && value.constructor.name !== ""
    ? value.constructor.name
    : typeof value;
}
