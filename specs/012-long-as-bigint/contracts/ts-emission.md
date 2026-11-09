# Contract: TypeScript emission for 64-bit integers

**Normative for**: FR-002, FR-004, FR-007–FR-044, FR-058–FR-060. Every row below is a golden (FR-063) and a
row of the reference conformance matrix (FR-064). `l`, `m` are `long`; `u`, `v` are `ulong`; `i` is `int`;
`ui` is `uint`; `d` is `double`; `f` is `float`; `dm` is `decimal`; `b` is `BigInteger`; `c` is `char`;
`n` is an `int` shift count. "unchecked" is the default context; "checked" is a `checked(...)` /
`checked { }` site or a project with `CheckForOverflowUnderflow` (FR-023).

Helpers come from `metano-runtime` (contracts/runtime-int64.md) and are imported by name only when used.

## Types

| C# | TypeScript |
|---|---|
| `long`, `ulong`, `nint`, `nuint` | `bigint` |
| `long?` | `bigint \| null` |
| `long[]`, `List<long>` (2.x mapping), `IEnumerable<ulong>` | `bigint[]`, `bigint[]`, `Iterable<bigint>` (the collection mapping itself is 011's) |
| `Dictionary<long, string>` | `Map<bigint, string>` (2.x) / `Dictionary<bigint, string>` (011) |
| `[PlainObject] record Dto(long Id, long? Ref)` | `interface Dto { id?: bigint; ref?: bigint \| null }` |
| `[Branded] struct UserId(long Value)` | `type UserId = bigint & { readonly __brand: "UserId" }` |
| `System.Numerics.BigInteger` | `bigint` (unchanged) |
| `enum Big : long { A = 1 }` | `export enum Big { A = 1 }` (values stay `number`, DECIDED-15) |

## Literals, constants and zero values

| C# | TypeScript |
|---|---|
| `5L`, `5UL`, `long x = 5;` | `5n`, `5n`, `let x: bigint = 5n;` |
| `0xFFFF_FFFF_FFFF_FFFFUL` | `18446744073709551615n` |
| `const long Big = 9007199254740993L;` | `static readonly big: bigint = 9007199254740993n;` |
| `long.MaxValue`, `long.MinValue`, `ulong.MaxValue` | `9223372036854775807n`, `-9223372036854775808n`, `18446744073709551615n` |
| `nint.MaxValue`, `IntPtr.Size` (member mappings; not C# constants) | `9223372036854775807n`, `8` |
| `1L << 40` | `1099511627776n` |
| `unchecked((long)ulong.MaxValue)` | `-1n` |
| `l + 1` | `BigInt.asIntN(64, l + 1n)` |
| `default(long)`, field without initializer | `0n` |
| `void M(long x = 5)` | `m(x: bigint = 5n)` |
| `new long[3]` | `new Array<bigint>(3).fill(0n)` |

## Conversions (FR-010–FR-019)

| C# | unchecked | checked |
|---|---|---|
| `long x = i;` / `M(i)` / `return i;` / `c ? i : l` / `new List<long> { i }` / `() => i` | `BigInt(i)` | same |
| `long x = ui;` | `BigInt(ui)` | same |
| `long x = c;` | `BigInt(c.charCodeAt(0))` | same |
| `(int)l` | `Number(BigInt.asIntN(32, l))` | `Number(checkedNarrow(l, 32, true))` |
| `(uint)l` | `Number(BigInt.asUintN(32, l))` | `Number(checkedNarrow(l, 32, false))` |
| `(short)l`, `(ushort)l`, `(sbyte)l`, `(byte)l` | `Number(BigInt.asIntN(16, l))`, `Number(BigInt.asUintN(16, l))`, `Number(BigInt.asIntN(8, l))`, `Number(BigInt.asUintN(8, l))` | `Number(checkedNarrow(l, 16, true))`, … |
| `(char)l` | `String.fromCharCode(Number(BigInt.asUintN(16, l)))` | `String.fromCharCode(Number(checkedNarrow(l, 16, false)))` |
| `(ulong)l` | `BigInt.asUintN(64, l)` | `checkedUInt64(l)` |
| `(long)u` | `BigInt.asIntN(64, u)` | `checkedInt64(u)` |
| `double x = l;` / `(double)l` | `Number(l)` | same |
| `float x = l;` / `(float)l` | `int64ToSingle(l)` | same |
| `(long)d`, `(long)f` | `int64FromDouble(d)` | `int64FromDoubleChecked(d)` |
| `(int)d`, `(uint)d` (any source, FR-070) | `int32FromDouble(d)`, `uint32FromDouble(d)` | `int32FromDoubleChecked(d)`, `uint32FromDoubleChecked(d)` |
| `(byte)d`, `(short)d`, … (FR-070) | saturate to `int`, keep the low bits (`int32FromDouble(d) & 0xFF`, `(int32FromDouble(d) << 16) >> 16`, …) | the `…Checked` helper, then the target range check |
| `long x = (int)d;` | `BigInt(int32FromDouble(d))` | `BigInt(int32FromDoubleChecked(d))` |
| `(ulong)d` | `uint64FromDouble(d)` | `uint64FromDoubleChecked(d)` |
| `decimal x = l;` / `(decimal)l` | `new Decimal(l.toString())` | same |
| `(long)dm` | `int64FromDecimal(dm)` | same (always checked, DECIDED-11) |
| `(ulong)dm` | `uint64FromDecimal(dm)` | same |
| `BigInteger x = l;` | `l` | same |
| `(long)b` | `int64FromBigInteger(b)` | same (always checked) |
| `(ulong)b` | `uint64FromBigInteger(b)` (`… for a UInt64.` outside the range, but `Arithmetic operation resulted in an overflow.` for a value in (int.MinValue, −1], as .NET 10 throws) | same (always checked) |
| `(int)b`, `(uint)b`, `(short)b`, `(char)b` | `int32FromBigInteger(b)`, `uint32FromBigInteger(b)`, `narrowFromBigInteger(b, 16, true)`, `String.fromCharCode(narrowFromBigInteger(b, 16, false))` | same (always checked, slice 012g) |
| `(double)b`, `(float)b`, `(decimal)b` | `doubleFromBigInteger(b)`, `Math.fround(doubleFromBigInteger(b))`, `new Decimal(decimalTextFromBigInteger(b))` | same |
| `(BigInteger)d`, `(BigInteger)dm` | `bigIntegerFromDouble(d)`, `bigIntegerFromDecimal(dm)` | same |
| `(long)e` (enum) | `BigInt(e)` | same |
| `(IntEnum)l` | `Number(BigInt.asIntN(32, l))` | `Number(checkedNarrow(l, 32, true))` |
| `(LongEnum)l` | `Number(l)` | same |
| `long? x = ni;` (`int? ni`, identifier) | `ni == null ? null : BigInt(ni)` (also catches `undefined`) | same |
| `long? x = F();` (`int? F()`) | `mapNullable(f(), BigInt)` | same |

## Overflow (FR-021–FR-026)

| C# | unchecked | checked |
|---|---|---|
| `l + m`, `l - m`, `l * m` | `BigInt.asIntN(64, l + m)` | `checkedInt64(l + m)` |
| `u + v`, `u - v`, `u * v` | `BigInt.asUintN(64, u + v)` | `checkedUInt64(u + v)` |
| `l + i` | `BigInt.asIntN(64, l + BigInt(i))` | `checkedInt64(l + BigInt(i))` |
| `-l` | `BigInt.asIntN(64, -l)` | `checkedInt64(-l)` |
| `~l`, `~u` | `~l`, `BigInt.asUintN(64, ~u)` | same |
| `l++;` (statement) | `l = BigInt.asIntN(64, l + 1n);` | `l = checkedInt64(l + 1n);` |
| `x = l++;` | `x = BigInt.asIntN(64, (l = BigInt.asIntN(64, l + 1n)) - 1n);` (the stored value stepped back, exact modulo 2^64, so no temporary and no IIFE) | `x = (l = checkedInt64(l + 1n)) - 1n;` (a step that did not throw was exact) |
| `this.Total += i;` | `this.total = BigInt.asIntN(64, this.total + BigInt(i));` | `this.total = checkedInt64(this.total + BigInt(i));` |
| `arr[k] *= 2;` (`long[] arr`, `k` with side effects) | the existing indexer read-modify-write lowering (`k` evaluated once) with `BigInt.asIntN(64, … * 2n)` | with `checkedInt64` |
| `nl + 1` (`long? nl`) | the lifted lowering with `BigInt.asIntN(64, nl + 1n)` in its non-null branch | with `checkedInt64` |
| `checked(e)`, `unchecked(e)` | the lowering of `e` | — |
| `checked { s; }` | `{ s; }` with `s` lowered in a checked context | — |
| `checked { Func<long> f = () => l + 1; }` | — | `const f = () => checkedInt64(l + 1n);` (the lambda inherits the context) |
| `checked { long G() => l * 2; }` (local function) | — | `const g = (): bigint => checkedInt64(l * 2n);` once local functions are supported; today the statement reports `MS0001` in every mode (slice 012c, deferred) |
| `checked { l <<= 1; }` | — | `l = BigInt.asIntN(64, l << 1n);` (shifts never check; no throw at `long.MaxValue`) |
| `l /= m;`, `l %= m;`, `l &= m;`, `l >>= n;`, `l >>>= 2;` | `l = divInt64(l, m);`, `l = remInt64(l, m);`, `l &= m;` (the native compound operator, which stores the same unwrapped value), `l = l >> BigInt(n & 63);`, `l = BigInt.asIntN(64, BigInt.asUintN(64, l) >> 2n);` | same (the division helpers check their own cases) |

## Operators (FR-027–FR-034)

| C# | TypeScript |
|---|---|
| `l / m`, `l % m` | `divInt64(l, m)`, `remInt64(l, m)` |
| `u / v`, `u % v` | `divUInt64(u, v)`, `remUInt64(u, v)` |
| `l / 7`, `l % 7` | `l / 7n`, `l % 7n` |
| `l / -1`, `l % -1` (constant) | `divInt64(l, -1n)`, `remInt64(l, -1n)` (a constant `0` divisor is C#'s CS0020, so it never reaches the output) |
| `l /= m;` | `l = divInt64(l, m);` |
| `nl / nm` (`long?`) | `nl == null \|\| nm == null ? null : divInt64(nl, nm)` (the lifted lowering every operator shares, as for `intDiv`; the helper also returns `null` for a `null` operand, for hand-written callers) |
| `l << 3`, `l << 70` | `BigInt.asIntN(64, l << 3n)`, `BigInt.asIntN(64, l << 6n)` |
| `l << n` | `BigInt.asIntN(64, l << BigInt(n & 63))` |
| `u << n` | `BigInt.asUintN(64, u << BigInt(n & 63))` |
| `l >> n`, `u >> n` | `l >> BigInt(n & 63)`, `u >> BigInt(n & 63)` |
| `l << c` (`char c`) | known gap: the implicit `char` → `int` conversion has no IR node, so the count stays a string (`BigInt(c & 63)`, rejected by `tsgo`); it needs a core `char` → `int` conversion, not a per-operator fix (slice 012d, deferred) |
| `l >>> 2` | `BigInt.asIntN(64, BigInt.asUintN(64, l) >> 2n)` |
| `u >>> 2` | `u >> 2n` |
| `l & m`, `l \| m`, `l ^ m` | `l & m`, `l \| m`, `l ^ m` |
| `l == m`, `l != 5`, `l == i` | `l === m`, `l !== 5n`, `l === BigInt(i)` |
| `l < i`, `l >= ui` | `l < i`, `l >= ui` (exact mixed comparison, FR-069) |
| `ui < i` (C# compares in `long`) | `ui < i` (both 32-bit or narrower, FR-069) |
| `ui + i`, `ui * i`, `-ui` (C# evaluates in `long`) | `BigInt.asIntN(64, BigInt(ui) + BigInt(i))`, `BigInt.asIntN(64, BigInt(ui) * BigInt(i))`, `BigInt.asIntN(64, -BigInt(ui))` |
| `int r = (int)(ui + i);` | `Number(BigInt.asIntN(32, BigInt.asIntN(64, BigInt(ui) + BigInt(i))))` |
| `l is 0 or > 10` | `l === 0n \|\| l > 10n` |
| `switch (l) { case 1L << 40: … }` | `switch (l) { case 1099511627776n: … }` |
| `l switch { 5 => "five", _ => "other" }` | the existing switch-expression lowering with `5n` |
| `"id=" + l`, `$"{l}"`, `l.ToString()` | `"id=" + l`, `` `${l}` ``, `l.toString()` |
| `+l` | `l` |

## BCL (FR-035–FR-044)

| C# | TypeScript |
|---|---|
| `Math.Abs(l)`, `long.Abs(l)` | `absInt64(l)` |
| `Math.Max(l, m)`, `Math.Min(u, v)` | `maxInt64(l, m)`, `minUInt64(u, v)` |
| `Math.Max(i, l)` | `maxInt64(BigInt(i), l)` |
| `Math.Sign(l)` | `signInt64(l)` |
| `Math.Clamp(l, lo, hi)` | `clampInt64(l, lo, hi)` |
| `Math.DivRem(l, m)` | `divRemInt64(l, m)` (a `[quotient, remainder]` tuple) |
| `Math.BigMul(l, m, out var low)` | `const [high, low] = bigMulInt64(l, m)` (out-parameter lowering) |
| `Math.Pow(l, 2)` | `Math.pow(Number(l), 2)` |
| `arr[l]`, `arr.LongLength`, `new int[l]` | `arr[Number(l)]`, `BigInt(arr.length)`, `new Array<number>(Number(l)).fill(0)` |
| `Console.WriteLine(l)` | `console.log(String(l))` |
| `xs.LongCount()` | `BigInt(<count terminal>)` |
| `l.ToString("N0")`, `$"{l:X}"`, `$"{l,8}"` | `formatInt64(l, "N0")`, `formatInt64(l, "X")`, `formatInt64(l, null, 8)` |
| `long.Parse(s)`, `ulong.Parse(s, NumberStyles.HexNumber)` | `parseInt64(s)`, `parseUInt64(s, 515)` (the folded `NumberStyles` flags) |
| `long.TryParse(s, out var x)` | `const [ok, x] = tryParseInt64(s)` |
| `Convert.ToInt64(d)`, `Convert.ToInt64(s)`, `Convert.ToInt64(s, 16)` | `convertDoubleToInt64(d)`, `convertStringToInt64(s)`, `convertStringToInt64(s, 16)` |
| `Convert.ToInt32(l)` | `Number(convertNarrow(l, "Int32"))` |
| `l.CompareTo(m)`, `l.Equals(m)`, `l.GetHashCode()` | `compareInt64(l, m)`, `l === m`, `valueHashCode(l)` |
| `ids.Sum()`, `ids.Average()`, `ids.Max()` | `linq(ids, sumInt64())`, `linq(ids, averageInt64())`, `linq(ids, maxInt64Of())` |
| `items.Sum(x => x.Size)` (`long Size`) | `linq(items, sumInt64((x) => x.size))` |
| `ts.Ticks`, `TimeSpan.FromTicks(l)` | `durationTicks(ts)`, `durationFromTicks(l)` |
| `dt.Ticks`, `new DateTime(l)` | `plainDateTimeTicks(dt)`, `plainDateTimeFromTicks(l)` |

The LINQ terminals keep LINQ's own names in the `system/linq` module; the `…Of` suffix in `maxInt64Of` /
`minInt64Of` keeps them distinct from the scalar `maxInt64` / `minInt64` of `system/int64` (the exact names
are fixed in contracts/runtime-int64.md).

## Interop (FR-058–FR-060)

| C# | TypeScript | Diagnostic |
|---|---|---|
| `enum Big : long { A = (1L << 53) + 1 }` | `A = 9007199254740992` | `MS0064` |
| `[Import("lib")] static extern long Count();` | `count(): bigint` declared by import | `MS0065` |
| `[PlainObject] record Dto(long Id); new Dto(i)` | `{ id: BigInt(i) }` | — |
