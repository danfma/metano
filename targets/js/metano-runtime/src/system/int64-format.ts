/**
 * `ToString(format)` and the interpolation holes `{value,alignment:format}` of
 * C#'s 64-bit integers, which are `bigint` values, in the invariant culture.
 * The integers held in a `number` share the implementation through
 * `formatInteger` (`integer-format.ts`).
 */
import { formatIntegerText } from "./integer-standard-format.ts";
import { alignText } from "./strings.ts";

/** The bit width of `long` and `ulong`, whose hex digits are their 64-bit two's complement. */
const INT64_BITS = 64;

/** How the exceptions name the values {@link formatInt64} formats. */
const INT64_VALUES = "64-bit integers";

/**
 * `value.ToString(format)` for a `long` or `ulong`, padded to `alignment` as
 * an interpolation hole is (left when the alignment is negative). `format`
 * `null`, `undefined` or `""` is `G`; a `null` value (a C# `long?`) formats
 * as the empty string. Implements `G[n]`, `R[n]` (`G` for an integer), `D[n]`, `N[n]`, `F[n]`,
 * `E[n]`, `P[n]`, `C[n]` and `X[n]` / `x[n]`; hex prints a negative value as
 * its 64-bit two's complement.
 *
 * @throws NotSupportedException for a valid .NET format outside that set
 *   (`B` or a custom format such as `#,##0`).
 * @throws FormatException for an invalid format (`Q`).
 */
export function formatInt64(
  value: bigint | null | undefined,
  format?: string | null,
  alignment?: number,
): string {
  const text =
    value == null ? "" : formatIntegerText(value, format ?? "", INT64_BITS, INT64_VALUES);

  return alignText(text, alignment);
}
