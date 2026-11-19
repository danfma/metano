import type { EnumType, RuntimeType } from "./runtime-type.ts";

/**
 * .NET's `ToString()` of a boxed value: an enum prints its member name, a combination of
 * `[Flags]` members the comma-joined names (`Read, Write`), anything undeclared its number
 * (`99`, and `11` for a flags value with a bit no member names, as .NET prints it); any other
 * boxed type (an `[InlineWrapper]`) prints `String(value)`.
 */
export function boxedText(value: unknown, type: RuntimeType): string {
  if (type.kind !== "enum" && type.kind !== "stringEnum") {
    return String(value);
  }

  const index = type.values.indexOf(value);

  return index >= 0
    ? (type.names[index] as string)
    : (type.isFlags && flagNames(type, value as number | bigint)) || String(value);
}

/**
 * The names of the members whose bits make up `value`, ascending by value (declaration order
 * does not matter, as in .NET); `""` when a bit is left over. The bits are `bigint`s, so a
 * 64-bit `[Flags]` enum decomposes as exactly as a 32-bit one.
 */
function flagNames({ values, names }: EnumType<unknown>, value: number | bigint): string {
  const bits = values.map((member) => BigInt(member as number | bigint));
  const descending = [...bits.keys()].sort((left, right) => Number(bits[right]! - bits[left]!));

  const { rest, found } = descending.reduce(
    (state, index) => {
      const member = bits[index]!;

      return member && (state.rest & member) === member
        ? { rest: state.rest & ~member, found: [names[index] as string, ...state.found] }
        : state;
    },
    { rest: BigInt(value), found: [] as string[] },
  );

  return rest ? "" : found.join(", ");
}
