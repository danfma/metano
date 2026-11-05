# Contract: The overload naming algorithm

**Normative for**: 013 FR-001–FR-006. One algorithm names the constructor factory (`from` +
suffix), the selector symbol (`by` + suffix) and the method fast path of every overload of a
dispatched group. It lives in the core (`Metano.Compiler`, `OverloadNamePolicy` — DECIDED-1) and
reads the IR only. Evidence for every "today" row: research R7.

## The algorithm

For each overload `o` of a group `G` (two or more overloads of one constructor or method):

1. **Base name.** A factory and a selector use the prefixes `from` / `by` + the suffix. A fast
   path uses the camelCased method name + the suffix. The suffix is computed below.
2. **Suffix, pass 1 — parameter names.** `string.Concat(o.Parameters.Select(Capitalize(p.Name)))`:
   `Add(int width, int height)` → `WidthHeight`; `Format(string fmt, params object[] args)` →
   `FmtArgs`. **No joiner** (DECIDED-2): today's `ComputeFastPathNames` joins nothing (`addXY`,
   `addP`), and 013 keeps that so names that already work stay identical.
   A parameterless overload's suffix is `Default` (DECIDED-5).
3. **Distinctness, pass 1.** If every suffix in `G` is distinct (ordinal), done.
4. **Suffix, pass 2 — type names.** On any collision, **every** overload of `G` switches to the
   Spellings table below applied to its parameter types, concatenated in order:
   `F(int a)` / `F(string a)` → `Int32` / `String`.
5. **Distinctness, pass 2.** If every suffix in `G` is distinct, done.
6. **The climb.** Otherwise qualify every type spelling with the declaring type's containing type
   (for nested types) and then its namespace segments, **nearest first**, one level at a time,
   uniformly for the whole group, stopping at the first depth where all suffixes are distinct:
   `K(Numerics.Decimal)` / `K(Other.Decimal)` → `NumericsDecimal` / `OtherDecimal`. A type in the
   global namespace contributes `Global`. (DECIDED-4)
7. **Still colliding at `Global`.** The C# declarations are indistinguishable at this projection;
   report `MS0003` (`AmbiguousConstruct`) and emit the `Global`-depth names.
8. **`ClaimName`.** If the resulting member name is claimed by a user-written member of the same
   scope (a class member, or a namespace function), append `$` until free, and report `MS0067`
   (DECIDED-13). The class path runs this check too — today it does not (R7's duplicate `now`).
9. **`[Name]`.** An overload carrying `[Name("…")]` replaces the suffix with the attribute's value
   verbatim (OQ-1 recommendation: the suffix only). An invalid identifier reports `MS0003`.
10. **Determinism.** All passes are pure functions of the IR; tie-breaks (there are none above —
    distinctness is checked over sets) are stable. Same source, same names (NFR-005).

The suffix feeds all three names of the overload:

| Overload | Factory | Selector | Fast path |
|---|---|---|---|
| `Dimension(int size)` | `fromSize` | `bySize` | `newDimensionSize` (a constructor fast path does not exist — the selector branch holds the body; the row shows what a method with the same signature would be named) |
| `Money(int cents)` | `fromCents` | `byCents` | — |
| `Add(Point p)` | — | — | `addP` |

Method overload groups have no factory or selector; constructor groups have no fast-path method
(the body lives in the selector branch, FR-007). The suffix is the shared vocabulary.

## Spellings (pass 2)

Exact C# type names; the legacy shortenings (`Int32 → Int`, `Int64 → Long`, `Boolean → Bool`,
`Single → Float`, `Float64 → Double`) are retired (DECIDED-3):

| C# type | Spelling | Example |
|---|---|---|
| `sbyte` … `long`, `nint` | `SByte`, `Byte`, `Int16`, `UInt16`, `Int32`, `UInt32`, `Int64`, `nint → IntPtr` | `F(int a)` / `F(long a)` → `Int32` / `Int64` |
| `ulong`, `nuint` | `UInt64`, `UIntPtr` | — |
| `float` | `Single` | — |
| `double` | `Double` | — |
| `decimal` | `Decimal` | — |
| `bool` | `Boolean` | — |
| `string` | `String` | — |
| `char` | `Char` | — |
| `object` | `Object` | — |
| `T[]` | `<Element>Array` | `int[]` → `Int32Array`; `Point[]` → `PointArray` |
| `T?` (nullable) | `<Element>OrNull` | `int?` → `Int32OrNull`; `string?` → `StringOrNull` |
| `C<A, B>` | `<C>Of<A><B>` | `List<int>` → `ListOfInt32`; `Dictionary<string, int>` → `DictionaryOfStringInt32` |
| `(A a, B b)` tuple | `TupleOf<A><B>` | `(int, string)` → `TupleOfInt32String` |
| `params T` | the expanded element spelling | `params int[]` → `Int32Array` |
| `ref T` | `RefOf<T>` (unreachable in TS today — `MS0001`; defined for Dart) | — |
| `out T` | `OutOf<T>` (as above) | — |
| nested type `Outer.Inner` | `Inner`, climbing to `OuterInner` on collision | DECIDED-4 |
| type parameter `T` | `T` (or the parameter's name as written) | `F(T v)` / `F(string v)` → `T` / `String` |
| delegate | the generic rule on its display name | `Func<int, bool>` → `FuncOfInt32Boolean`; a named delegate type uses its own name |

Unmapped / error types fall back to `Value` (today's fallback, kept).

## Worked examples (all from research R7)

| C# group | Today | 013 |
|---|---|---|
| `Add(int x, int y)` / `Add(Point p)` | `addXY`, `addP` | unchanged |
| `F(int a)` / `F(string a)` | `fInt`, `fString` | `fInt32`, `fString` |
| `G(int v)` / `G(long v)` | `gInt`, `gLong` | `gInt32`, `gInt64` |
| `Sum(int[] xs)` / `Sum(double[] xs)` | `sumIntArray`, `sumDoubleArray` | unchanged shape |
| `H(int? maybe)` / `H(string? maybe)` | `hIntOrNull`, `hStringOrNull` | unchanged shape |
| `K(Numerics.Decimal)` / `K(Other.Decimal)` | `kDecimal`, `kDecimal` (TS2393) | `kNumericsDecimal`, `kOtherDecimal` |
| `Now()` / `Now(int tick)` | `now`, `nowTick` (TS2393 + TS2385) | `nowDefault`, `nowTick` |
| `Format(string fmt, int count)` / `Format(string fmt, params object[] args)` | `formatFmtCount`, `formatFmtArgs` | unchanged |
| `Identity<T>(T value)` / `Identity(string value)` | `identityValue<T>`, `identityString` | unchanged |
| constructors `Money(decimal amount)` / `Money(int cents)` / `Money(int cents, bool rounded)` | dispatcher branches only | `fromAmount` / `fromCents` / `fromCentsRounded` (pass 1 already distinct); `R7`'s plain-type example `Money(decimal)` / `Money(int)` has no parameter names in the issue text, so its suffixes are pass 2's `Decimal` / `Int32` — `fromDecimal` / `fromInt32` |

## Stability

- Renaming a C# parameter renames the factory, the selector and the fast path (D7).
- Adding one overload can flip the whole group from pass 1 to pass 2 (or into a climb).
- Moving a type in namespace space can rename its group's suffixes through the climb.
- `[Name]` pins the suffix against all of the above.
