# Contract: `metano-runtime` 64-bit helpers

**Normative for**: FR-012–FR-017, FR-019, FR-022, FR-027, FR-035–FR-042, FR-045–FR-048. Module:
`targets/js/metano-runtime/src/system/int64.ts`, re-exported from the package root (DECIDED-12). LINQ
terminals live in `src/system/linq/terminals.ts`; ticks helpers in `src/system/temporal-helpers.ts`; type
checks in `src/type-checking/primitive-type-checks.ts`. Every export is a function or a `const` literal with
no module-scope side effect (NFR-003). Every exception is the 011 FR-013 runtime class (DECIDED-20) with
the exact .NET message listed here (NFR-007); messages marked † are pinned by the reference fixtures of
FR-064 rather than by research R2.

## Ranges

```ts
export const INT64_MIN = -9223372036854775808n;
export const INT64_MAX = 9223372036854775807n;
export const UINT64_MAX = 18446744073709551615n;
```

## Overflow and narrowing

```ts
/** Returns `value` when it fits `long`; throws OverflowException("Arithmetic operation resulted in an overflow.") otherwise. */
export function checkedInt64(value: bigint): bigint;
/** Same for `ulong` (0 … 2^64 − 1). */
export function checkedUInt64(value: bigint): bigint;
/** Checked narrowing of a 64-bit value to a `bits`-wide integer (8, 16, 32); same message. */
export function checkedNarrow(value: bigint, bits: 8 | 16 | 32, signed: boolean): bigint;
/** `Convert.ToXxx(long)`: same range test, message "Value was either too large or too small for an Int32." and its family †. */
export function convertNarrow(value: bigint, target: "SByte" | "Byte" | "Int16" | "UInt16" | "Int32" | "UInt32" | "Int64" | "UInt64" | "Char"): bigint;
/** Lifted conversion: `null` and `undefined` give `null`; any other value goes through `convert` once. */
export function mapNullable<T, R>(value: T | null | undefined, convert: (value: T) => R): R | null;
```

## Division

```ts
export function divInt64(dividend: bigint, divisor: bigint): bigint;
export function divInt64(dividend: bigint | null | undefined, divisor: bigint | null | undefined): bigint | null;
export function remInt64(dividend: bigint, divisor: bigint): bigint;
export function remInt64(dividend: bigint | null | undefined, divisor: bigint | null | undefined): bigint | null;
export function divUInt64(dividend: bigint, divisor: bigint): bigint;   // + nullable overload
export function remUInt64(dividend: bigint, divisor: bigint): bigint;   // + nullable overload
```

- `divisor === 0n` → `DivideByZeroException("Attempted to divide by zero.")`.
- `divInt64` / `remInt64` with `dividend === INT64_MIN && divisor === -1n` → `OverflowException("Arithmetic operation resulted in an overflow.")`, in every context (R2).
- Otherwise the native `/` and `%` (truncating, dividend's sign; R4).

## Floating point, decimal and `BigInteger`

```ts
/** Unchecked `(long)d`: truncate; NaN → 0n; ≥ 2^63 → INT64_MAX; < −2^63 → INT64_MIN (.NET 9+ saturation, R2). */
export function int64FromDouble(value: number): bigint;
/** Checked `(long)d`: truncate, then OverflowException("Arithmetic operation resulted in an overflow.") outside the range; NaN and ±Infinity throw. */
export function int64FromDoubleChecked(value: number): bigint;
export function uint64FromDouble(value: number): bigint;          // NaN → 0n, negatives → 0n, ≥ 2^64 → UINT64_MAX
export function uint64FromDoubleChecked(value: number): bigint;   // (ulong)-0.9 → 0n (truncate first, R2)
/** `(int)d` / `(uint)d` for any source (FR-070): truncate, saturate, NaN → 0 (.NET 9+); `…Checked` throws OverflowException("Arithmetic operation resulted in an overflow."). Narrower targets saturate to `int` first and keep the low bits (R11: `(byte)300.5` → 44). */
export function int32FromDouble(value: number): number;
export function int32FromDoubleChecked(value: number): number;
export function uint32FromDouble(value: number): number;
export function uint32FromDoubleChecked(value: number): number;
/** `(float)l` and `(float)u` (the whole `long` and `ulong` range): rounds once to the nearest single (ties to even), via round-to-odd to 53 bits then Math.fround (R5). */
export function int64ToSingle(value: bigint): number;
/** `(long)m`: truncate toward zero; OverflowException("Value was either too large or too small for an Int64.") outside the range, in every context. */
export function int64FromDecimal(value: DecimalLike): bigint;
export function uint64FromDecimal(value: DecimalLike): bigint;    // "… for a UInt64."
/** `(long)b`: OverflowException("Value was either too large or too small for an Int64.") outside the range, in every context. */
export function int64FromBigInteger(value: bigint): bigint;
export function uint64FromBigInteger(value: bigint): bigint;      // "… for a UInt64."; a value in (int.MinValue, −1] throws "Arithmetic operation resulted in an overflow." instead, as .NET 10 does (BigInteger keeps it inline and converts it with a checked cast)
```

`DecimalLike` is the decimal.js surface the helpers read: `trunc()`, `toFixed()` and `e` (the same type
`numeric-converters.ts` already uses).

## `Math` and `INumber` statics

| Export | .NET source | Behavior |
|---|---|---|
| `absInt64(x)` | `Math.Abs(long)`, `long.Abs` | `x < 0n ? -x : x`; `INT64_MIN` → `OverflowException("Negating the minimum value of a twos complement number is invalid.")` |
| `maxInt64(a, b)`, `minInt64(a, b)`, `maxUInt64`, `minUInt64` | `Math.Max` / `Min`, `long.Max` / `Min` | evaluate each argument once |
| `signInt64(x): number` | `Math.Sign(long)`, `long.Sign` | `-1`, `0`, `1` |
| `clampInt64(v, lo, hi)`, `clampUInt64` | `Math.Clamp`, `long.Clamp` | `lo > hi` → `ArgumentException("'<lo>' cannot be greater than <hi>.")` |
| `divRemInt64(a, b): [quotient: bigint, remainder: bigint]`, `divRemUInt64` (mutable, as C#'s `(long, long)` maps to `[bigint, bigint]`) | `Math.DivRem`, `long.DivRem` | the division rules above |
| `bigMulInt64(a, b): readonly [high: bigint, low: bigint]`, `bigMulUInt64` | `Math.BigMul(long, long, out long)` / `(ulong, ulong, out ulong)` | high and low 64 bits of the 128-bit product; `low` reinterpreted as `long` for the signed form (R2: `BigMul(long.MaxValue, long.MaxValue)` → `[4611686018427387903n, 1n]`) |

## Comparison and hashing

```ts
export function compareInt64(a: bigint, b: bigint): number;   // -1 | 0 | 1, also used for ulong
export function hashInt64(value: bigint): number;             // Number(BigInt.asIntN(32, v)) ^ Number(BigInt.asIntN(32, v >> 32n))
```

`hash-code.ts` `hashValue` and `equality.ts` `hashCode` call `hashInt64` for every `bigint` (FR-046).

## Formatting

```ts
/**
 * `value.ToString(format)` / an interpolation hole `{value,alignment:format}` in the invariant culture.
 * `format` null or "" → "G". Supported: G[n] (scientific past the precision, rounded half away from zero), D[n], N[n] (default 2 decimals), F[n] (default 2), X[n] / x[n],
 * R. Hex of a negative value prints its 64-bit two's complement. Alignment pads with spaces, left when
 * negative. Any other standard or custom format → NotSupportedException; an invalid one →
 * FormatException("Format specifier was invalid.").
 */
export function formatInt64(value: bigint, format?: string | null, alignment?: number): string;
```

Reference outputs (R2): `formatInt64(1234567n, "N0")` → `1,234,567`; `"N2"` → `1,234,567.00`;
`formatInt64(-1n, "X")` → `FFFFFFFFFFFFFFFF`; `formatInt64(255n, "x4")` → `00ff`;
`formatInt64(255n, "X20")` → `000000000000000000FF`; `formatInt64(-1234n, "D8")` → `-00001234`;
`formatInt64(1234567n, "F2")` → `1234567.00`; `formatInt64(5n, null, 8)` → `       5`.

## Parsing

```ts
/** `style` is the C# `NumberStyles` flags value, folded at compile time (`NumberStyles.Integer` = 7 by default). */
export function parseInt64(text: string | null | undefined, style?: number): bigint;
export function parseUInt64(text: string | null | undefined, style?: number): bigint;
export function tryParseInt64(text: string | null | undefined, style?: number): readonly [found: boolean, value: bigint];
export function tryParseUInt64(text: string | null | undefined, style?: number): readonly [found: boolean, value: bigint];
```

- Supported styles: `Integer` (7), `HexNumber` (515), `Integer | AllowThousands` (71) and `Number` (111,
  which adds a trailing sign, group separators and a decimal point). Any other flags value throws
  `NotSupportedException`; a constant one is also reported by `MS0066` at compile time.
- Whitespace: .NET's set (U+0009–U+000D and U+0020) before and after; U+00A0 is not whitespace (R2).
- `Integer`: optional `+` / `-`, ASCII digits. `ulong` accepts `-0` (R2: `ulong.Parse("-0")` is `0`) and
  throws `OverflowException` for any other negative.
- `HexNumber`: hex digits only, no sign, no prefix; up to 16 digits, read as two's complement for `long`
  (`FFFFFFFFFFFFFFFF` → `-1n`); 17 significant digits → `OverflowException`.
- `Integer | AllowThousands` / `Number`: `,` group separators; `Number` also accepts a trailing sign and `.`
  followed by digits, which must all be `0` (a non-zero fraction is `OverflowException`, R2).
- Failures: `null` → `ArgumentNullException("Value cannot be null. (Parameter 's')")`; bad syntax →
  `FormatException("The input string '<text>' was not in a correct format.")`; out of range →
  `OverflowException("Value was either too large or too small for an Int64.")` (`a UInt64`). `TryParse`
  returns `[false, 0n]` for each of them.
- Until slice 012k the signed parser is exported as `tryParseInt64Value`: the 2.x `number` `tryParseInt64` (`system/number-parsing.ts`) still serves the shipped `Number` mode, and replacing it before the flip would break that output. 012k removes it and renames `tryParseInt64Value`.
- The existing `tryParseInt64` (`system/number-parsing.ts`) moves here and returns `bigint` (breaking for
  hand-written callers; migration notes).

## `Convert`

| Export | `Convert` overload | Rule |
|---|---|---|
| `convertDoubleToInt64(d)` / `…ToUInt64` | `ToInt64(double)`, `ToInt64(float)` | round half to even, then the checked range test; NaN → `OverflowException("Arithmetic operation resulted in an overflow.")` (R2) |
| `convertDecimalToInt64(m)` / `…ToUInt64` | `ToInt64(decimal)` | round half to even, then `int64FromDecimal`'s range test |
| `convertStringToInt64(s, fromBase?)` / `…ToUInt64` | `ToInt64(string)`, `ToInt64(string, int)` | `null` → `0n`; base 10 → `parseInt64(s)`; bases 2, 8, 16 → digits of that base, two's complement for 16 (`"FFFFFFFFFFFFFFFF"` → `-1n`); other bases → `ArgumentException` † |
| `convertObjectToInt64(o)` / `…ToUInt64` | `ToInt64(object)` | dispatches on the runtime value (`bigint`, `number`, `string`, `boolean`, decimal.js) |
| inline `b ? 1n : 0n` | `ToInt64(bool)` | — |
| inline `BigInt(c.charCodeAt(0))` | `ToInt64(char)` | — |
| `convertNarrow(x, target)` | `ToInt32(long)` and the other narrowing overloads | see above |
| `checkedUInt64(BigInt(i))` with `convertNarrow`'s message | `ToUInt64(int)` (negative) | `OverflowException("Value was either too large or too small for a UInt64.")` |

## LINQ terminals (`system/linq/terminals.ts`)

```ts
export function sumInt64<T>(selector?: (item: T) => bigint | null): SumTerm<T>;      // checked; nulls skipped; 0n when empty
export function sumUInt64<T>(selector?: (item: T) => bigint | null): SumTerm<T>;
export function averageInt64<T>(selector?: (item: T) => bigint): AverageTerm<T>;     // checked sum, Number(sum) / count; empty → InvalidOperationException("Sequence contains no elements")
export function averageNullableInt64<T>(selector?: (item: T) => bigint | null): AverageTerm<T>; // null when no non-null element
export function minInt64Of<T>(selector?: (item: T) => bigint): MinTerm<T>;          // empty → InvalidOperationException
export function maxInt64Of<T>(selector?: (item: T) => bigint): MaxTerm<T>;
export function minNullableInt64Of<T>(selector?: (item: T) => bigint | null): MinTerm<T>; // skips null; null when empty
export function maxNullableInt64Of<T>(selector?: (item: T) => bigint | null): MaxTerm<T>;
// As landed (012g): Enumerable has no Sum / Average over ulong, so sumUInt64 and averageUInt64 do not exist.
// UInt64 twins: averageUInt64, minUInt64Of, maxUInt64Of (ordering is the same; only the checked sum differs)
```

They keep the `queryable?: QueryableMeta` parameter of the existing terminals.

## Ticks (`system/temporal-helpers.ts`)

```ts
export function durationTicks(duration: Temporal.Duration): bigint;          // total nanoseconds / 100n (balanced, no calendar units)
export function durationFromTicks(ticks: bigint): Temporal.Duration;         // ticks * 100n ns, balanced to days … nanoseconds
export function plainDateTimeTicks(value: Temporal.PlainDateTime): bigint;   // 100 ns units since 0001-01-01T00:00:00
export function plainDateTimeFromTicks(ticks: bigint): Temporal.PlainDateTime;
```

Reference values (R2): `durationTicks(Temporal.Duration.from({ seconds: 1 }))` → `10000000n`;
`plainDateTimeTicks(Temporal.PlainDateTime.from("2026-01-01T00:00"))` → `639028224000000000n`.

## Type checks (`type-checking/primitive-type-checks.ts`)

```ts
export function isInt64(value: unknown): value is bigint;   // typeof bigint and INT64_MIN ≤ v ≤ INT64_MAX
export function isUInt64(value: unknown): value is bigint;  // typeof bigint and 0n ≤ v ≤ UINT64_MAX
export function isBigInt(value: unknown): value is bigint;  // unchanged
```

## `BigInteger` conversions (`system/big-integer.ts`, slice 012g)

```ts
export function int32FromBigInteger(value: bigint): number;     // (int)b
export function uint32FromBigInteger(value: bigint): number;    // (uint)b
export function narrowFromBigInteger(value: bigint, bits: 8 | 16, signed: boolean): number; // (short), (ushort), (sbyte), (byte), (char) b: through int first
export function doubleFromBigInteger(value: bigint): number;    // (double)b: .NET's truncated significand; ±Infinity past 1024 bits; (float)b is Math.fround of it
export function decimalTextFromBigInteger(value: bigint): string; // the digits of (decimal)b, for new Decimal(…)
export function bigIntegerFromDouble(value: number): bigint;    // (BigInteger)d / (BigInteger)f: truncate
export function bigIntegerFromDecimal(value: DecimalLike): bigint; // (BigInteger)m: truncate
```

Every explicit `BigInteger` conversion is checked in any context. .NET holds a value in
(`int.MinValue`, `int.MaxValue`] inline and converts it with a checked cast, so `(int)b` for a value in
[2^31, 2^32), `(uint)b` for a negative inline value, and the narrower targets after the `int` step throw
`OverflowException("Arithmetic operation resulted in an overflow.")`; the other out-of-range values throw
`"Value was either too large or too small for an Int32."` (`a UInt32.`). `(decimal)b` beyond ±(2^96 − 1)
throws `"… for a Decimal."`; `(BigInteger)NaN` throws `"The value is not a number."` and an infinity
`"BigInteger cannot represent infinity."`.
