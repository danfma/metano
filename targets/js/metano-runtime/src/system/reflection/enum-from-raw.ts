import { ArgumentException } from "../exceptions/argument-exception.ts";
import { OverflowException } from "../exceptions/overflow-exception.ts";
import type { EnumType, PrimitiveName } from "./runtime-type.ts";

const INTEGER_TEXT = /^[+-]?\d+$/;

/** The width and signedness of an enum's underlying type, with .NET's overflow wording. */
interface Underlying {
  readonly bits: number;
  readonly signed: boolean;
  readonly overflowTarget: string;
}

const INT32: Underlying = { bits: 32, signed: true, overflowTarget: "an Int32" };

const UNDERLYING: Readonly<Partial<Record<PrimitiveName, Underlying>>> = {
  Byte: { bits: 8, signed: false, overflowTarget: "an unsigned byte" },
  SByte: { bits: 8, signed: true, overflowTarget: "a signed byte" },
  Int16: { bits: 16, signed: true, overflowTarget: "an Int16" },
  UInt16: { bits: 16, signed: false, overflowTarget: "a UInt16" },
  Int32: INT32,
  UInt32: { bits: 32, signed: false, overflowTarget: "a UInt32" },
  Int64: { bits: 64, signed: true, overflowTarget: "an Int64" },
  UInt64: { bits: 64, signed: false, overflowTarget: "a UInt64" },
};

/**
 * `Enum.ToObject` / `Enum.Parse`, faithful to .NET (spec 011 FR-024).
 *
 * - An integral `number` or a `bigint` (`ToObject`) wraps to the underlying type's width, an
 *   undeclared value included, and never throws.
 * - A string (`Parse`) is either, as a whole, an integer in the underlying type's range
 *   (`OverflowException` outside it), or a comma-separated list of member names, OR-ed in the
 *   underlying type's width (case-sensitive unless `ignoreCase`). An unknown name throws
 *   `ArgumentException` naming the whole input.
 * - A `[StringEnum]` maps a number through its members' C# values (`ordinals`, by default their
 *   positions) and a name to its value.
 */
export function enumFromRaw<E>(type: EnumType<E>, raw: unknown, ignoreCase = false): E {
  if (typeof raw === "string") {
    return fromText(type, raw, ignoreCase);
  }

  if (typeof raw === "bigint" || (typeof raw === "number" && Number.isInteger(raw))) {
    return fromInteger(type, wrap(type, BigInt(raw)));
  }

  throw new ArgumentException(
    "The value passed in must be an enum base or an underlying type for an enum, such as an Int32.",
    "value",
  );
}

function fromText<E>(type: EnumType<E>, text: string, ignoreCase: boolean): E {
  const trimmed = text.trim();

  if (trimmed === "") {
    throw new ArgumentException(
      "Must specify valid information for parsing in the string.",
      "value",
    );
  }

  if (INTEGER_TEXT.test(trimmed)) {
    return fromInteger(type, inRange(type, BigInt(trimmed)));
  }

  const combined = trimmed
    .split(",")
    .reduce((value, token) => value | memberRaw(type, token.trim(), ignoreCase, text), 0n);

  return fromInteger(type, wrap(type, combined));
}

/** The C# value of the member named `name`; an unknown name throws, naming the whole `input`. */
function memberRaw<E>(type: EnumType<E>, name: string, ignoreCase: boolean, input: string): bigint {
  const index = indexOfName(type, name, ignoreCase);

  if (index < 0) {
    throw new ArgumentException(`Requested value '${input}' was not found.`);
  }

  return type.kind === "stringEnum"
    ? BigInt(ordinalAt(type, index))
    : BigInt(type.values[index] as number | bigint);
}

/** The enum value of the C# integer `raw`. */
function fromInteger<E>(type: EnumType<E>, raw: bigint): E {
  if (type.kind === "stringEnum") {
    const index = type.values.findIndex((_, at) => BigInt(ordinalAt(type, at)) === raw);

    return (index < 0 ? String(raw) : type.values[index]) as E;
  }

  return (typeof type.values[0] === "bigint" ? raw : Number(raw)) as E;
}

function ordinalAt<E>(type: EnumType<E>, index: number): number {
  return type.ordinals?.[index] ?? index;
}

function underlyingOf<E>(type: EnumType<E>): Underlying {
  return UNDERLYING[type.underlying ?? "Int32"] ?? INT32;
}

function wrap<E>(type: EnumType<E>, raw: bigint): bigint {
  const { bits, signed } = underlyingOf(type);

  return signed ? BigInt.asIntN(bits, raw) : BigInt.asUintN(bits, raw);
}

function inRange<E>(type: EnumType<E>, raw: bigint): bigint {
  if (wrap(type, raw) !== raw) {
    throw new OverflowException(
      `Value was either too large or too small for ${underlyingOf(type).overflowTarget}.`,
    );
  }

  return raw;
}

function indexOfName<E>(type: EnumType<E>, name: string, ignoreCase: boolean): number {
  if (!ignoreCase) {
    return type.names.indexOf(name);
  }

  const lower = name.toLowerCase();

  return type.names.findIndex((candidate) => candidate.toLowerCase() === lower);
}
