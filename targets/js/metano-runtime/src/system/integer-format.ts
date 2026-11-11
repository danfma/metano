/**
 * `ToString(format)` and the interpolation holes `{value,alignment:format}` of
 * C#'s integers of 32 bits or fewer, which are `number` values, in the
 * invariant culture, through the implementation `formatInt64` uses.
 */
import { formatIntegerText } from "./integer-standard-format.ts";
import { alignText } from "./strings.ts";

/** The bit widths of the integers held in a `number`: `sbyte` / `byte`, `short` / `ushort`, `int` / `uint`. */
export type IntegerBits = 8 | 16 | 32;

/** How the exceptions name the values {@link formatInteger} formats. */
const INTEGER_VALUES = "integers";

/**
 * `value.ToString(format)` for an integer of `bits` bits held in a `number`
 * (`sbyte`, `byte`, `short`, `ushort`, `int`, `uint`), padded to `alignment`
 * as an interpolation hole is. The formats are `formatInt64`'s; hex
 * prints a negative value as its two's complement on `bits` bits.
 *
 * @throws NotSupportedException for a valid .NET format the runtime does not implement.
 * @throws FormatException for an invalid format.
 */
export function formatInteger(
  value: number | null | undefined,
  bits: IntegerBits,
  format?: string | null,
  alignment?: number,
): string {
  const text =
    value == null ? "" : formatIntegerText(BigInt(value), format ?? "", bits, INTEGER_VALUES);

  return alignText(text, alignment);
}
