import type { Int64Descriptor, IntegerTypeName, NumericEnumDescriptor } from "./types.ts";

/** The underlying type of a numeric enum whose descriptor names none, the C# default. */
export const DEFAULT_ENUM_INTEGER_TYPE: IntegerTypeName = "System.Int32";

/** A numeric enum member's value: a `number`, or a `bigint` for a 64-bit enum (#701). */
export type NumericEnumValue = number | bigint;

/** A numeric enum descriptor of a 64-bit enum, which names its underlying type (#701). */
export type Int64EnumDescriptor = NumericEnumDescriptor & {
  readonly numberType: Int64Descriptor["numberType"];
};

/** Whether a numeric enum's values are `bigint`s: its underlying type is 64-bit (#701). */
export function holdsBigInts(descriptor: NumericEnumDescriptor): descriptor is Int64EnumDescriptor {
  return descriptor.numberType === "System.Int64" || descriptor.numberType === "System.UInt64";
}

/**
 * The declared members of a numeric enum, as `[name, value]` pairs: the
 * entries of its `as const` object whose value is a number of the enum's
 * representation (a `bigint` for a 64-bit enum, a `number` otherwise). A
 * hand-written TS `enum` object's reverse mapping (string values) is skipped.
 */
export function numericEnumMembers(
  descriptor: NumericEnumDescriptor,
): ReadonlyArray<readonly [name: string, value: NumericEnumValue]> {
  const representation = holdsBigInts(descriptor) ? "bigint" : "number";

  return Object.entries(descriptor.values).filter(
    (member): member is [string, NumericEnumValue] => typeof member[1] === representation,
  );
}
