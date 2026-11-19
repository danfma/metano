import type { EnumType, RuntimeType } from "./runtime-type.ts";

/**
 * .NET's `ToString()` of a boxed value: an enum prints its member name, a combination of
 * `[Flags]` members the comma-joined names (`Read, Write`), anything undeclared its number
 * (`99`, and `11` for a flags value with a bit no member names, as .NET prints it); any other
 * boxed type (an `[InlineWrapper]`) prints `String(value)`.
 */
export function boxedText(value: unknown, type: RuntimeType): string {
  const { values, names, isFlags } = type as EnumType<unknown>;
  const index = values?.indexOf(value) ?? -1;

  return index >= 0
    ? (names[index] as string)
    : (isFlags && flagNames(type as EnumType<unknown>, value as number)) || String(value);
}

/** The names of the members whose bits make up `value`, ascending by value (declaration order does not matter, as in .NET); `""` when a bit is left over. */
function flagNames({ values, names }: EnumType<unknown>, value: number): string {
  const found: string[] = [];
  let rest = value | 0;
  const descending = [...values.keys()].sort(
    (left, right) => (values[right] as number) - (values[left] as number),
  );

  for (const index of descending) {
    const member = (values[index] as number) | 0;

    if (member && (rest & member) === member) {
      found.unshift(names[index] as string);
      rest &= ~member;
    }
  }

  return rest ? "" : found.join(", ");
}
