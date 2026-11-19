import type { NumericEnumDescriptor } from "./types.ts";

/** A numeric enum member's value: a `number`, or a `bigint` for a 64-bit enum (#701). */
export type NumericEnumValue = number | bigint;

/** Whether a numeric enum's values are `bigint`s: its underlying type is 64-bit (#701). */
export function holdsBigInts(descriptor: NumericEnumDescriptor): boolean {
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
