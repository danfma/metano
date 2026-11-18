/**
 * The explicit numeric casts of a C# `dynamic` value (`(int)d`, `(long)d`, `(char)d`,
 * `(decimal)d`; spec 011 FR-030). The C# runtime binder converts from the type the value
 * holds at run time, which the compiler does not know, so these helpers look at the value and
 * then convert it with the helper the compiler uses for the same cast from that type:
 *
 * - a `number` that is an integer in the 32-bit range (`int`, `uint` and the narrower
 *   integers hold such values) converts as an integer: the low bits of the target's width,
 *   or range-checked in a checked context;
 * - any other `number` converts as a `double` (`int32FromDouble`, `int64FromDouble`, …);
 * - a `bigint` (a `long`, `ulong` or `BigInteger`) converts as a 64-bit integer;
 * - a decimal.js value converts as a `decimal` (`int64FromDecimal`, …), which checks the
 *   range in every context, as .NET does;
 * - a one-character `string` is a `char`, which only the `char` target accepts;
 * - `null`, and any other value (a `string`, a `boolean`, an object), throw
 *   `InvalidCastException`, where .NET throws `RuntimeBinderException`.
 *
 * A cast to `T?` lets `null` through before it calls these helpers.
 */
import {
  bigIntegerFromDecimal,
  bigIntegerFromDouble,
  decimalTextFromBigInteger,
  int32FromBigInteger,
  narrowFromBigInteger,
  uint32FromBigInteger,
} from "./big-integer.ts";
import { InvalidCastException } from "./exceptions/index.ts";
import {
  checkedInt32,
  checkedInt64,
  checkedNarrow,
  checkedNarrowNumber,
  checkedUInt32,
  checkedUInt64,
  int32FromDouble,
  int32FromDoubleChecked,
  int64FromDecimal,
  int64FromDouble,
  int64FromDoubleChecked,
  uint32FromDouble,
  uint32FromDoubleChecked,
  uint64FromDecimal,
  uint64FromDouble,
  uint64FromDoubleChecked,
} from "./int64.ts";

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

type IntegerTarget = Exclude<DynamicNumberTarget, "Single" | "Double"> | "Char";

/** An integer target's width, signedness and C# name (for the cast failure message). */
type IntegerShape = { readonly bits: 8 | 16 | 32; readonly signed: boolean; readonly name: string };

const INTEGER_SHAPES: Readonly<Record<IntegerTarget, IntegerShape>> = {
  SByte: { bits: 8, signed: true, name: "sbyte" },
  Byte: { bits: 8, signed: false, name: "byte" },
  Int16: { bits: 16, signed: true, name: "short" },
  UInt16: { bits: 16, signed: false, name: "ushort" },
  Char: { bits: 16, signed: false, name: "char" },
  Int32: { bits: 32, signed: true, name: "int" },
  UInt32: { bits: 32, signed: false, name: "uint" },
};

const BIGINT_NAMES: Readonly<Record<DynamicBigIntTarget, string>> = {
  Int64: "long",
  UInt64: "ulong",
  BigInteger: "System.Numerics.BigInteger",
};

const INT32_MIN = -2147483648;
const UINT32_MAX = 4294967295;

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
  if (target === "Double") {
    return doubleOf(value, "double");
  }

  if (target === "Single") {
    return Math.fround(doubleOf(value, "float"));
  }

  return integerOf(value, target, checked);
}

/**
 * `(char)d`: a `char` (a one-character `string`) as is, or the UTF-16 code a numeric value
 * converts to.
 *
 * @throws InvalidCastException for `null` and for a value that is neither a `char` nor numeric.
 * @throws OverflowException for a code outside `char` in a checked context.
 */
export function charFromDynamic(value: unknown, checked = false): string {
  if (typeof value === "string" && value.length === 1) {
    return value;
  }

  return String.fromCharCode(integerOf(value, "Char", checked));
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
  if (typeof value === "bigint") {
    return bigintOfInteger(value, target, checked);
  }

  if (isDecimalValue(value)) {
    return bigintOfDecimal(value, target);
  }

  if (typeof value !== "number") {
    throw cannotConvert(value, BIGINT_NAMES[target]);
  }

  return isThirtyTwoBitInteger(value)
    ? bigintOfInteger(BigInt(value), target, checked)
    : bigintOfDouble(value, target, checked);
}

/**
 * The argument `new Decimal(…)` takes for `(decimal)d`: the `number` (converted as the cast
 * from `double` is), or the exact digits of a `bigint` or of a decimal, as text, so this
 * module does not load decimal.js.
 *
 * @throws InvalidCastException for `null` and for a value that is not numeric.
 * @throws OverflowException for a `bigint` beyond ±(2^96 − 1).
 */
export function decimalSourceFromDynamic(value: unknown): number | string {
  if (isDecimalValue(value)) {
    return value.toFixed();
  }

  if (typeof value === "bigint") {
    return decimalTextFromBigInteger(value);
  }

  return doubleOf(value, "decimal");
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

  throw cannotConvert(value, targetName);
}

/** The value converted to a 32-bit or narrower integer target (`char` as its code). */
function integerOf(value: unknown, target: IntegerTarget, checked: boolean): number {
  const shape = INTEGER_SHAPES[target];

  if (typeof value === "bigint") {
    return checked
      ? Number(checkedNarrow(value, shape.bits, shape.signed))
      : Number(lowBits(value, shape));
  }

  if (isDecimalValue(value)) {
    return integerOfDecimal(value, shape);
  }

  if (typeof value !== "number") {
    throw cannotConvert(value, shape.name);
  }

  return isThirtyTwoBitInteger(value)
    ? integerOfInteger(value, shape, checked)
    : integerOfDouble(value, shape, checked);
}

/** An integer held in a `number` narrowed to `shape`: its low bits, or range-checked. */
function integerOfInteger(value: number, shape: IntegerShape, checked: boolean): number {
  if (!checked) {
    return Number(lowBits(BigInt(value), shape));
  }

  if (shape.bits === 32) {
    return shape.signed ? checkedInt32(value) : checkedUInt32(value);
  }

  return checkedNarrowNumber(value, shape.bits, shape.signed);
}

/**
 * A `double` cast to a 32-bit or narrower integer as the compiler casts one (FR-070):
 * truncated and saturated to `int` (or `uint`), then the low bits of a narrower target; a
 * checked cast throws outside the target's range.
 */
function integerOfDouble(value: number, shape: IntegerShape, checked: boolean): number {
  if (shape.bits === 32 && !shape.signed) {
    return checked ? uint32FromDoubleChecked(value) : uint32FromDouble(value);
  }

  const asInt32 = checked ? int32FromDoubleChecked(value) : int32FromDouble(value);

  return integerOfInteger(asInt32, shape, checked);
}

/** A decimal cast to a 32-bit or narrower integer: truncated, range-checked in every context. */
function integerOfDecimal(value: DecimalValue, shape: IntegerShape): number {
  const integer = bigIntegerFromDecimal(value);

  if (shape.bits === 32) {
    return shape.signed ? int32FromBigInteger(integer) : uint32FromBigInteger(integer);
  }

  return narrowFromBigInteger(integer, shape.bits, shape.signed);
}

function bigintOfInteger(value: bigint, target: DynamicBigIntTarget, checked: boolean): bigint {
  switch (target) {
    case "Int64":
      return checked ? checkedInt64(value) : BigInt.asIntN(64, value);
    case "UInt64":
      return checked ? checkedUInt64(value) : BigInt.asUintN(64, value);
    case "BigInteger":
      return value;
  }
}

function bigintOfDouble(value: number, target: DynamicBigIntTarget, checked: boolean): bigint {
  switch (target) {
    case "Int64":
      return checked ? int64FromDoubleChecked(value) : int64FromDouble(value);
    case "UInt64":
      return checked ? uint64FromDoubleChecked(value) : uint64FromDouble(value);
    case "BigInteger":
      return bigIntegerFromDouble(value);
  }
}

function bigintOfDecimal(value: DecimalValue, target: DynamicBigIntTarget): bigint {
  switch (target) {
    case "Int64":
      return int64FromDecimal(value);
    case "UInt64":
      return uint64FromDecimal(value);
    case "BigInteger":
      return bigIntegerFromDecimal(value);
  }
}

/** The low `bits` of `value`, read signed or unsigned: an unchecked integer narrowing. */
function lowBits(value: bigint, shape: IntegerShape): bigint {
  return shape.signed ? BigInt.asIntN(shape.bits, value) : BigInt.asUintN(shape.bits, value);
}

/** Whether a `number` holds a value an `int`, a `uint` or a narrower integer can hold. */
function isThirtyTwoBitInteger(value: number): boolean {
  return Number.isInteger(value) && value >= INT32_MIN && value <= UINT32_MAX;
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
function cannotConvert(value: unknown, targetName: string): InvalidCastException {
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

  const constructorName =
    typeof value === "object" && value !== null
      ? (value as { constructor?: { name?: string } }).constructor?.name
      : undefined;

  return constructorName ? constructorName : typeof value;
}
