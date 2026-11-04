# Research: `long` and `ulong` as native `bigint`

Evidence tags: **[V]** = verified on 2026-11-04, either by reading the cited file on `main` at
`90be699e` or by running the cited probe; **[S]** = stated by the C# language specification (ECMA-334
7th edition draft, as published at `learn.microsoft.com/dotnet/csharp/language-reference/language-specification`)
and confirmed by a probe where one is quoted; **[I]** = inferred from code or docs, not executed;
**[M]** = maintainer decision.

Probe environment [V]: macOS arm64, .NET SDK 10.0.401 (`net10.0`, Release), Roslyn
`Microsoft.CodeAnalysis.CSharp` 5.3.0 (the version `Directory.Packages.props` pins), Node 24.11.0,
Bun 1.4.2. The probe programs live in the session scratchpad and are not committed; every output line
below is quoted verbatim, labels included.

## R1 — What Metano does with `long` today

**Type mapping** [V]:

- `src/Metano.Compiler/Extraction/IrTypeRefMapper.cs` L84-86 maps both `SpecialType.System_Int64`
  and `SpecialType.System_UInt64` to `IrPrimitive.Int64`. The IR has **no unsigned 64-bit primitive**;
  `uint`→`Int32`, `ushort`→`Int16` and `sbyte`→`Byte` collapse the same way (L75-83).
- `src/Metano.Compiler.TypeScript/Bridge/IrToTsTypeMapper.cs` L191:
  `IrPrimitive.Int64 => new TsNumberType()`. Only `IrPrimitive.BigInteger` (L195) maps to
  `TsBigIntType`.
- `System.Numerics.BigInteger` is recognised by name (`IrTypeRefMapper.cs` L116-118).
- `nint` / `nuint` (`SpecialType.System_IntPtr` / `System_UIntPtr`) are not in the primitive switch.
  They fall through to a named type and print as `IntPtr` (probe below).

**Today's output** [V]. A scratch project transpiled with `dotnet run --project src/Metano.Compiler.TypeScript/`
on this branch:

```csharp
public const long Big = 9007199254740993L;
public static long Add(long a, int b) => a + b;
public static long Wrap(long a) => unchecked(a + 1);
public static long Checked(long a) => checked(a * 2);
public static int Narrow(long a) => (int)a;
public static long Shift(long a, int n) => a << n;
public static ulong UShift(ulong a, int n) => a >> n;
public static string Fmt(long a) => a.ToString("N0");
public static long Parse(string s) => long.Parse(s);
public static long Lit() => long.MaxValue;
public static nint Native(nint n) => n + 1;
public static long Ticks(TimeSpan t) => t.Ticks;
public static long Sum(long[] xs) => xs.Sum();
```

```text
warning MS0001: Expression 'unchecked(a + 1)' (UncheckedExpression) is not supported by the transpiler.
warning MS0001: Expression 'checked(a * 2)' (CheckedExpression) is not supported by the transpiler.
warning MS0001: 'long.Parse' has no declarative mapping to TypeScript: the call is emitted by name and has no JavaScript counterpart.
warning MS0001: 'long.MaxValue' has no declarative mapping to TypeScript: the read is emitted by name and has no JavaScript counterpart.
```

```ts
static readonly big: number = 9007199254740993;          // the literal rounds to …992 at run time
static add(a: number, b: number): number { return a + b; }
static wrap(a: number): number { return (() => { throw new Error("Unsupported expression: UncheckedExpression"); })(); }
static narrow(a: number): number { return a; }           // the cast is erased
static shift(a: number, n: number): number { return a << n; }   // a 32-bit JS shift
static uShift(a: number, n: number): number { return a >> n; }  // a signed 32-bit shift
static fmt(a: number): string { return a.toString("N0"); }       // a TS error: radix expected
static parse(s: string): number { return Int64.parse(s); }       // `Int64` is undefined
static lit(): number { return Int64.maxValue; }
static native(n: IntPtr): IntPtr { return n + 1; }               // `IntPtr` is undefined
static ticks(t: Temporal.Duration): number { return t.ticks; }   // `ticks` does not exist
static sum(xs: number[]): number { return linq(xs, sum()); }
```

A record `Snowflake(long Id, ulong Mask, long? Parent)` emits `id: number`, `mask: number`,
`parent: number | null`, compares the fields with `===` and hashes them with `HashCode.add` [V].

Conclusions: `long` is already broken beyond 2^53 (the issue's point), and several `long` constructs are
broken at any magnitude: `checked` / `unchecked` (both forms), shifts, narrowing casts, formatting,
`long.Parse`, `long.MaxValue`, `nint`, `Ticks`.

**Casts** [V]. `IrExpressionExtractor.ExtractCast` (`IrExpressionExtractor.cs` L4542-4625) rewrites only
the casts that change the JS representation (`BigInteger` ↔ `decimal`, `decimal` → integer via
`toNumber()`, integer → `BigInteger` via `BigInt(x)`). Every other cast becomes an `IrCastExpression`, which
`IrToTsExpressionBridge.Patterns.cs` `MapCast` (L267-277) erases. Implicit conversions are never
materialised: the only conversion-aware code path is `ClassifyNumericInContext` (`IrExpressionExtractor.cs`
L1629-1637), which turns a **literal** into a `Decimal` / `BigInteger` literal from its `ConvertedType`.
So `BigInteger b = i;` (a non-literal `int`) emits `b = i` today, a `number` in a `bigint` slot [I, from
the code].

**Integer division** [V]. `IrBinaryOp.IntegerDivide` (`IrExpression.cs` L414-422) is produced by
`IrExpressionExtractor.IntegerDivision.cs` for every built-in integral `/` (including `long`, `ulong`,
`nint`, `nuint`) and lowered by the bridge (`IrToTsExpressionBridge.cs` L153-154, L256) to the runtime
`intDiv`, which works on `number` and throws a plain `Error` with .NET's message on a zero divisor
(`targets/js/metano-runtime/src/system/integer-division.ts`).

**Samples** [V]: `grep -rnwE 'long|ulong|Int64|UInt64|nint|nuint' samples --include='*.cs'` finds nothing.
No sample uses a 64-bit integer. Tests that mention `long` (non-exhaustive):
`IntegerDivisionTranspileTests.cs`, `NullableIncrementTests.cs`, `AutoInitDefaultTests.cs`,
`GetValueOrDefaultMappingTests.cs`, `BrandedAttributeTranspileTests.cs`, `BrandedValueObjectTests.cs`,
`TupleLiteralTests.cs`, `OutArgumentTranspileTests.cs`, `SwitchStatementPatternTests.cs`,
`LocalNameCollisionTests.cs`, `ExtensionCallSiteTests.cs`, `QueryableExpressionTreeTests.cs`,
`DartBackendTests.cs`, `Json/JsonReadOptionsTests.cs`, `Json/JsonWebNamingTests.cs`.

## R2 — C# semantics of `long` / `ulong` (probe 1, .NET 10)

Overflow and context [S, V]. The C# spec makes `+ - * ++ --`, unary `-` and explicit numeric
conversions overflow-checked in a checked context and truncating in an unchecked one, and makes the
unchecked context the default unless the compilation option says otherwise (§12.8.20 "The checked and
unchecked operators", §13.12 "The checked and unchecked statements"). Shifts are not in that list.

```text
unchecked(long.MaxValue + 1) = -9223372036854775808 (Int64)
checked(long.MaxValue + 1) => OverflowException: Arithmetic operation resulted in an overflow.
unchecked(ulong.MaxValue + 1) = 0 (UInt64)
checked(ulong.MaxValue + 1) => OverflowException: Arithmetic operation resulted in an overflow.
unchecked(0UL - 1) = 18446744073709551615 (UInt64)
unchecked(long.MaxValue * 2) = -2 (Int64)
checked(long.MaxValue * 2) => OverflowException: Arithmetic operation resulted in an overflow.
unchecked(-long.MinValue) = -9223372036854775808 (Int64)
checked(-long.MinValue) => OverflowException: Arithmetic operation resulted in an overflow.
max++ (unchecked) = -9223372036854775808 (Int64)
max++ (checked) => OverflowException: Arithmetic operation resulted in an overflow.
checked(1L << 63) = -9223372036854775808 (Int64)
checked(max << 1) = -2 (Int64)
```

The last two lines confirm that shifts never check, even inside `checked(...)`.

Division and remainder [S, V]. §12.10.3 says integer division truncates toward zero and throws
`DivideByZeroException` on a zero divisor. For `MinValue / -1` the spec leaves the unchecked result
implementation-defined; .NET throws in both contexts, and §12.10.4 says `x % y` throws exactly when
`x / y` would:

```text
long.MinValue / -1 (unchecked) => OverflowException: Arithmetic operation resulted in an overflow.
long.MinValue % -1 (unchecked) => OverflowException: Arithmetic operation resulted in an overflow.
-7L / 2L = -3 (Int64)
-7L % 2L = -1 (Int64)
7L % -2L = 1 (Int64)
5L / 0L => DivideByZeroException: Attempted to divide by zero.
5L % 0L => DivideByZeroException: Attempted to divide by zero.
long % neg zero sign = 0
```

Shifts [S, V]. §12.11: for `long` / `ulong` the count is `count & 0x3F`; `>>` is arithmetic on
`long` and logical on `ulong`; `>>>` (C# 11) is always logical:

```text
1L << 64 = 1 (Int64)
1L << 65 = 2 (Int64)
1L << -1 = -9223372036854775808 (Int64)
-16L >> 2 = -4 (Int64)
-16L >>> 2 = 4611686018427387900 (Int64)
ulong.MaxValue >> 60 = 15 (UInt64)
ulong.MaxValue >>> 60 = 15 (UInt64)
1UL << 64 = 1 (UInt64)
ulong.MaxValue >> 64 (masked) = 18446744073709551615
```

Bitwise [V]:

```text
~5L = -6 (Int64)
~5UL = 18446744073709551610 (UInt64)
-1L & 0xFF = 255 (Int64)
-1L ^ 5L = -6 (Int64)
```

Integral conversions [S, V]. §10.2.3 lists the implicit widenings (`sbyte`, `byte`, `short`,
`ushort`, `int`, `uint`, `char` → `long`; `byte`, `ushort`, `uint`, `char` → `ulong`; `long` /
`ulong` → `float`, `double`, `decimal`). §10.3.2: an unchecked narrowing keeps the low bits, a checked
one throws `OverflowException`:

```text
(int)0x100000001L = 1 (Int32)
checked((int)0x100000001L) => OverflowException: Arithmetic operation resulted in an overflow.
(int)2147483648L = -2147483648 (Int32)
(uint)-1L = 4294967295 (UInt32)
(short)70000L = 4464 (Int16)
(byte)-1L = 255 (Byte)
(char)65L = A (Char)
(long)'A' = 65 (Int64)
(long)ulong.MaxValue unchecked = -1 (Int64)
checked((long)ulong.MaxValue) => OverflowException: Arithmetic operation resulted in an overflow.
checked((ulong)-1L) => OverflowException: Arithmetic operation resulted in an overflow.
(sbyte)-1 -> long = -1
int var -> ulong = 18446744073709551615
uint + long = 4000000005 (Int64)
```

Floating-point conversions [S, V]. §10.3.2 leaves an out-of-range unchecked `double` → `long` value
unspecified; .NET 9 and later saturate on every platform (the probe agrees). Checked throws. The value is
truncated before the range test:

```text
(long)1e19 unchecked = 9223372036854775807 (Int64)
(long)-1e19 unchecked = -9223372036854775808 (Int64)
(long)NaN unchecked = 0 (Int64)
(long)1e19 checked => OverflowException: Arithmetic operation resulted in an overflow.
(long)-3.7 = -3 (Int64)
(ulong)-1.0 unchecked = 0 (UInt64)
(ulong)1e20 unchecked = 18446744073709551615 (UInt64)
(long)9.2233720368547758E18 unchecked = 9223372036854775807 (Int64)
(long)9.2233720368547758E18 checked => OverflowException: Arithmetic operation resulted in an overflow.
(long)-9.2233720368547758E18 checked = -9223372036854775808
(long)-9223372036854777856.0 checked => OverflowException: Arithmetic operation resulted in an overflow.
(ulong)-0.9 checked = 0
(double)long.MaxValue = 9.223372036854776E+18
(double)(2^53+1) = 9007199254740992
(double)(2^53+3) = 9007199254740996
(float)long.MaxValue = 9.223372E+18
(float)16777217L = 16777216
```

Decimal conversions [S, V]. §10.3.2: `decimal` → integral truncates toward zero and **always** throws
`OverflowException` when the result is out of range, whatever the context:

```text
(decimal)long.MaxValue = 9223372036854775807 (Decimal)
(long)79228162514264337593543950335m unchecked => OverflowException: Value was either too large or too small for an Int64.
(long)-3.7m = -3 (Int64)
(ulong)-1m unchecked => OverflowException: Value was either too large or too small for a UInt64.
(long)decimal -9223372036854775808.9 = -9223372036854775808
(ulong)decimal -0.9 = 0
```

`BigInteger` → `long` is a user-defined explicit operator, so it throws in both contexts [V]:

```text
BigInteger -> long => OverflowException: Value was either too large or too small for an Int64.
unchecked BigInteger -> long => OverflowException: Value was either too large or too small for an Int64.
```

Equality, comparison, hashing [V]:

```text
5L == 5 = True (Boolean)
5L.Equals(5) = True (Boolean)            // binds Equals(long); the int converts
((object)5L).Equals(5) = False (Boolean) // a boxed long is not a boxed int
((object)5L).Equals(5L) = True (Boolean)
5L.CompareTo(3) = 1 (Int32)
5L.GetHashCode() = 5 (Int32)
(-1L).GetHashCode() = 0 (Int32)
(1L<<32).GetHashCode() = 1 (Int32)
ulong.MaxValue.GetHashCode() = 0 (Int32)
long.MinValue.CompareTo(0) = -1
5L is int = False (Boolean)
```

The hash values match `(int)value ^ (int)(value >> 32)` [I, from the four values].

Formatting (invariant culture) [V]:

```text
1234567L.ToString() = "1234567"
ToString("N0") = "1,234,567"
ToString("N2") = "1,234,567.00"
ToString("N") = 5.00
ToString("N0") negative = -1,234,567
(-1L).ToString("X") = "FFFFFFFFFFFFFFFF"
255L.ToString("x4") = "00ff"
ToString("X20") = 000000000000000000FF
1234L.ToString("D8") = "00001234"
(-1234L).ToString("D8") = "-00001234"
ToString("D") MinValue = -9223372036854775808
ToString("d0") = 5
ToString("G") = 1234567
ToString("F2") = 1234567.00
ToString("R") = 5
ToString("C") = ¤1,234,567.00
ToString("P0") = 500 %
1234567L.ToString("E2") = "1.23E+006"
1234567L.ToString("#,##0") = "1,234,567"
ToString("Q") => FormatException: Format specifier was invalid.
ulong.MaxValue.ToString() = "18446744073709551615"
interp {l:N0} = "1,234,567"
interp {l,8} = "[       5]"
string.Format {0:X} = "FF"
"a" + 5L = "a5"
```

Parsing [V]:

```text
long.Parse(" 12 ") = 12 (Int64)
long.Parse("+5") = 5 (Int64)
long.Parse("9223372036854775808") => OverflowException: Value was either too large or too small for an Int64.
long.Parse("abc") => FormatException: The input string 'abc' was not in a correct format.
long.Parse("") => FormatException: The input string '' was not in a correct format.
long.Parse(null) => ArgumentNullException: Value cannot be null. (Parameter 's')
long.Parse("1,000") => FormatException: The input string '1,000' was not in a correct format.
long.Parse("1,000", N) = 1000 (Int64)
long.Parse("1,000.00", Number) = 1000
long.Parse("1,000.50", Number) => OverflowException: Value was either too large or too small for an Int64.
long.Parse("FF", Hex) = 255 (Int64)
long.Parse(" -FF ", HexNumber) = 255          // label typo in the probe: the input was " FF "
long.Parse("-FF", HexNumber) => FormatException: The input string '-FF' was not in a correct format.
long.Parse("FFFFFFFFFFFFFFFF", Hex) = -1 (Int64)
long.Parse("10000000000000000", Hex) => OverflowException: Value was either too large or too small for an Int64.
long.Parse("(5)", AllowParentheses) = -5
long.Parse("0x10") => FormatException: The input string '0x10' was not in a correct format.
long.Parse("1e3") => FormatException: The input string '1e3' was not in a correct format.
long.Parse(" 12") => FormatException: The input string ' 12' was not in a correct format.
long.Parse("٣") => FormatException: The input string '٣' was not in a correct format.
ulong.Parse("-0") = 0 (UInt64)
ulong.Parse("-1") => OverflowException: Value was either too large or too small for a UInt64.
ulong.Parse(" +7 ") = 7
ulong.Parse("18446744073709551615") = 18446744073709551615 (UInt64)
long.TryParse("9223372036854775808") = "False 0"
```

`Convert` [V]. `Convert.ToInt64(double)` rounds half to even and checks the range (NaN throws);
`Convert.ToInt64(string)` parses with `NumberStyles.Integer` and maps `null` to `0`:

```text
Convert.ToInt64(2.5) = 2 (Int64)
Convert.ToInt64(3.5) = 4 (Int64)
Convert.ToInt64(-2.5) = -2 (Int64)
Convert.ToInt64(1e19) => OverflowException: Arithmetic operation resulted in an overflow.
Convert.ToInt64(9.2233720368547758E18) => OverflowException: Arithmetic operation resulted in an overflow.
Convert.ToInt64(-9.2233720368547758E18) = -9223372036854775808
Convert.ToInt64(double.NaN) => OverflowException: Arithmetic operation resulted in an overflow.
Convert.ToInt64(1e19f) => OverflowException: Arithmetic operation resulted in an overflow.
Convert.ToInt64("12") = 12 (Int64)
Convert.ToInt64(null string) = 0 (Int64)
Convert.ToInt64("-9223372036854775809") => OverflowException: Value was either too large or too small for an Int64.
Convert.ToInt64("ff",16) = 255 (Int64)
Convert.ToInt64("FFFFFFFFFFFFFFFF",16) = -1
Convert.ToInt64(2.5m) = 2 (Int64)
Convert.ToInt64(true) = 1 (Int64)
Convert.ToInt64('A') = 65
Convert.ToInt64(object 5) = 5
Convert.ToInt64(object "5") = 5
Convert.ToUInt64(-1) => OverflowException: Value was either too large or too small for a UInt64.
Convert.ToUInt64("-1") => OverflowException: Value was either too large or too small for a UInt64.
Convert.ToInt32(5000000000L) => OverflowException: Value was either too large or too small for an Int32.
Convert.ToDouble(long.MaxValue) = 9.223372036854776E+18
```

`Math` and the `INumber` statics [V]. (The probe's `Math.Max(1L,2L)` line passed the variable `l = 5`
as the second argument; the result `5` is correct for `Math.Max(1L, 5L)`.)

```text
Math.Abs(-5L) = 5 (Int64)
Math.Abs(long.MinValue) => OverflowException: Negating the minimum value of a twos complement number is invalid.
Math.Abs(long.MinValue+1) = 9223372036854775807
Math.Max(int,long) binds = 7 (Int64)
Math.Min(5L, 3L) = 3
Math.Sign(-5L) = -1 (Int32)
Math.Sign(0L) = 0
Math.Clamp(10L,0L,5L) = 5 (Int64)
Math.Clamp(1L,5L,0L) => ArgumentException: '5' cannot be greater than 0.
Math.DivRem(-7L,2L) = (-3, -1) (ValueTuple`2)
Math.DivRem(7L,0L) => DivideByZeroException: Attempted to divide by zero.
Math.BigMul(long,long,out low) = "4611686018427387903 1"
Math.BigMul(long,long) Int128 = 85070591730234615847396907784232501249 (Int128)
Math.BigMul(int,int) = 4611686014132420609 (Int64)
Math.Pow on long -> double = 25
long.Max(1,2) = 2 (Int64)
long.Abs(-3) = 3 (Int64)
long.Sign = -1 (Int32)
long.IsPow2(8) = True (Boolean)
long.PopCount(-1) = 64 (Int64)
long.LeadingZeroCount(1) = 63 (Int64)
long.RotateLeft(1,64) = 1 (Int64)
```

LINQ [V]. `Sum` over `long` is checked; `Average` sums in a checked `long` and then divides as a
double; `Max` / `Min` / `Average` on an empty non-nullable sequence throw:

```text
Sum() overflow => OverflowException: Arithmetic operation resulted in an overflow.
Sum() ok = 3 (Int64)
Sum() empty = 0 (Int64)
Average() = 1.5
Average() of big => OverflowException: Arithmetic operation resulted in an overflow.
Average() empty => InvalidOperationException: Sequence contains no elements
Max() = 9 (Int64)
Max() empty => InvalidOperationException: Sequence contains no elements
Min() of long? = 1 (Int64)
LongCount() = 3 (Int64)
Sum(selector int->long) = 4294967294 (Int64)
Sum(long?) = 3 (Int64)
```

Arrays [S, V]. §12.8.12.2 allows `int`, `uint`, `long` and `ulong` array indices:

```text
arr[longIdx] = 20
arr[long 5] => IndexOutOfRangeException: Index was outside the bounds of the array.
arr.LongLength = 3
```

Ticks and native integers [V]:

```text
TimeSpan.FromSeconds(1).Ticks = 10000000 (Int64)
TimeSpan.FromTicks(15) = "00:00:00.0000015"
TimeSpan.FromTicks(long.MaxValue) = 10675199.02:48:05.4775807
DateTime(2026,1,1).Ticks = 639028224000000000 (Int64)
DateTime.MaxValue.Ticks = 3155378975999999999 (Int64)
new DateTime(0L).ToString("o") = 0001-01-01T00:00:00.0000000
nint size = 8 (Int32)
nint.MaxValue = 9223372036854775807 (IntPtr)
```

Miscellaneous [V]:

```text
default(long) = 0 (Int64)
(long?)null + 1 = null
long switch = hit                         // case 1099511627776 on (1L << 40)
enum : long beyond 2^53 = 9007199254740993
```

## R3 — Roslyn exposes the effective checked context and every implicit conversion (probe 2)

`IBinaryOperation.IsChecked`, `IUnaryOperation.IsChecked`, `IConversionOperation.IsChecked`,
`IIncrementOrDecrementOperation.IsChecked` and `ICompoundAssignmentOperation.IsChecked` already fold
`checked(...)`, `unchecked(...)`, `checked { }` / `unchecked { }` and the compilation option
`CSharpCompilationOptions.CheckOverflow` (MSBuild `CheckForOverflowUnderflow`, csc `-checked`) [V].
Excerpt for the same method compiled with `CheckOverflow = false` and `true`:

```text
=== CheckOverflow=False
Binary Add checked=False type=long :: a + i
Conversion implicit=True numeric=True checked=False int->long :: i
Binary Add checked=True type=long :: a + i                   // checked(a + i)
Binary Multiply checked=True type=long :: a * 2              // checked { x = a * 2; x++; }
Increment checked=True :: x++
Binary LeftShift checked=False type=long :: a << 70          // unchecked(a << 70)
Binary LeftShift checked=False type=long :: a << 1           // checked(a << 1): shifts never check
Conversion implicit=False numeric=True checked=True long->int :: (int)a    // checked((int)a)
Conversion implicit=False numeric=True checked=False long->int :: (int)a
Conversion implicit=True numeric=True checked=False char->long :: ch
Binary Add checked=False type=ulong :: u + ui
Unary Minus checked=True :: -a
Compound Add checked=False :: x += i
Conversion implicit=True numeric=True checked=False int->long :: i         // return i;
=== CheckOverflow=True
Binary Add checked=True type=long :: a + i
Conversion implicit=False numeric=True checked=True long->int :: (int)a    // the plain (int)a
Binary LeftShift checked=False type=long :: a << 1
Compound Add checked=True :: x += i
```

The semantic model reports every implicit numeric conversion as `GetConversion(e)` plus
`GetTypeInfo(e).ConvertedType`, at every syntactic position the issue lists, and `GetConstantValue`
folds constants [V] (probe 3):

```text
i                int->long numeric=True constant=- parent=Argument
i                int->long numeric=True constant=- parent=ConditionalExpression
1                int->long numeric=True constant=1 (Int32) parent=CollectionInitializerExpression
i                int->long numeric=True constant=- parent=CollectionInitializerExpression
i                int->long numeric=True constant=- parent=ArrayInitializerExpression
5                int->long numeric=True constant=5 (Int32) parent=ConstantPattern
7                int->long numeric=True constant=7 (Int32) parent=CaseSwitchLabel
10               int->long numeric=True constant=10 (Int32) parent=RelationalPattern
i                int->long numeric=True constant=- parent=ParenthesizedLambdaExpression
ui               uint->long numeric=True constant=- parent=EqualsValueClause
ch               char->long numeric=True constant=- parent=EqualsValueClause
1L << 40         long->long numeric=False constant=1099511627776 (Int64) parent=EqualsValueClause
long.MaxValue    long->long numeric=False constant=9223372036854775807 (Int64) parent=EqualsValueClause
ulong.MaxValue   ulong->ulong numeric=False constant=18446744073709551615 (UInt64) parent=EqualsValueClause
i                int->long numeric=True constant=- parent=ReturnStatement
```

An interpolation hole (`$"{a} {i}"`) has no numeric conversion: it is a boxing conversion to `object`
(or an interpolated-string-handler call), so `bigint` reaches the template as is.

Conclusion: the core can decide every 64-bit lowering from the semantic model, with no context
tracking of its own (D4, FR-010, FR-016).

## R4 — JavaScript `bigint` behavior (probe 4, Node 24.11.0 and Bun 1.4.2)

Both engines agree on every value; only the error messages differ. Node output, with Bun's message where it
differs:

```text
1n + 1 => TypeError: Cannot mix BigInt and other types, use explicit conversions
        (Bun: TypeError: Invalid mix of BigInt and other type in addition.)
MAX + 1n = 9223372036854775808n
BigInt.asIntN(64, MAX + 1n) = -9223372036854775808n
BigInt.asUintN(64, UMAX + 1n) = 0n
BigInt.asUintN(64, 0n - 1n) = 18446744073709551615n
BigInt.asIntN(64, MAX * 2n) = -2n
BigInt.asIntN(64, -MIN) = -9223372036854775808n
-7n / 2n = -3n
-7n % 2n = -1n
7n % -2n = 1n
5n / 0n => RangeError: Division by zero        (Bun: RangeError: 0 is an invalid divisor value.)
MIN / -1n = 9223372036854775808n                // no exception, out of the long range
1n << 64n = 18446744073709551616n               // no count masking
1n << (65n & 63n) = 2n
BigInt.asIntN(64, 1n << 63n) = -9223372036854775808n
1n << -1n = 0n                                  // a negative count shifts right
-16n >> 2n = -4n
-16n >>> 2n => TypeError: BigInts have no unsigned right shift, use >> instead
BigInt.asUintN(64, -16n) >> 2n = 4611686018427387900n
UMAX >> 60n = 15n
1n << 5 => TypeError: Cannot mix BigInt and other types, use explicit conversions
~5n = -6n
BigInt.asUintN(64, ~5n) = 18446744073709551610n
-1n & 0xFFn = 255n
Number(BigInt.asIntN(32, 0x100000001n)) = 1
Number(BigInt.asIntN(32, 2147483648n)) = -2147483648
Number(BigInt.asUintN(32, -1n)) = 4294967295
Number(BigInt.asIntN(16, 70000n)) = 4464
Number(BigInt.asUintN(8, -1n)) = 255
String.fromCharCode(Number(BigInt.asUintN(16, 65n))) = "A"
Number(MAX) = 9223372036854776000
Number(2n**53n+1n) = 9007199254740992           // round half to even, as C#
Number(2n**53n+3n) = 9007199254740996
BigInt(1.5) => RangeError: The number 1.5 cannot be converted to a BigInt because it is not an integer
BigInt(Math.trunc(-3.7)) = -3n
BigInt(1e19) = 10000000000000000000n
BigInt(NaN) => RangeError: The number NaN cannot be converted to a BigInt because it is not an integer
BigInt(' 12 ') = 12n
BigInt('') = 0n                                 // not FormatException
BigInt('0x10') = 16n                            // hex prefix accepted
BigInt('+5') = 5n
BigInt('1e3') => SyntaxError: Cannot convert 1e3 to a BigInt
5n == 5 = true
5n === 5 = false
5n < 6 = true
typeof 5n = "bigint"
Math.max(1n, 2n) => TypeError: Cannot convert a BigInt value to a number
Math.abs(-1n) => TypeError: Cannot convert a BigInt value to a number
[10,20][1n] = 20                                // the key is the string "1"; TS rejects a bigint index
JSON.stringify(1n) => TypeError: Do not know how to serialize a BigInt
JSON.stringify({a: JSON.rawJSON(MAX.toString())}) = "{\"a\":9223372036854775807}"
JSON.parse reviver context.source = 12345678901234567890n
JSON.parse plain = 12345678901234567000
new Map([[1n,'a']]).get(1n) = "a"
new Set([1n]).has(1) = false
(-1n).toString(16) = "-1"
BigInt.asUintN(64,-1n).toString(16).toUpperCase() = "FFFFFFFFFFFFFFFF"
1234567n.toLocaleString('en-US') = "1,234,567"
Intl N2 = "1,234,567.00"
Intl N0 MAX = "9,223,372,036,854,775,807"
`${MAX}` = "9223372036854775807"
'a' + 5n = "a5"
0n ? 'truthy' : 'falsy' = "falsy"
+5n (unary plus) => TypeError: Cannot convert a BigInt value to a number
structuredClone(5n) = 5n
```

Consequences: every C# conversion must be explicit (`BigInt(x)` / `Number(x)`), the wrap must be explicit
(`asIntN` / `asUintN`), the shift count must be masked and converted, `>>>` needs a rewrite, a zero
divisor needs a guard that throws .NET's exception, and `MinValue / -1` needs a guard that throws
`OverflowException`. `BigInt(string)` is not .NET's grammar (it accepts `""`, `0x10`, surrounding
whitespace including U+00A0), so parsing cannot delegate to it. `Number(bigint)` rounds correctly to the
nearest double.

## R5 — `(float)long` needs a correctly rounded helper

C# converts `long` to `float` with one rounding. `Math.fround(Number(x))` rounds twice (to a double, then
to a single), which differs on halfway cases [V] (probe 1b and probe 4):

```text
C#:  (float)(2^60+2^36+1)         = 1.1529216E+18
C#:  (float)(double)(2^60+2^36+1) = 1.1529215E+18
JS:  Math.fround(Number(2n**60n + 2n**36n + 1n)) = 1152921504606847000   // = 2^60, the double-rounded value
```

`2^36` is half an ulp of a single at `2^60`, so the trailing `+1` decides the rounding. Double rounding loses
it. A round-to-odd step before `Math.fround` (truncate to 53 significant bits and OR the dropped bits into the
lowest kept bit) makes the double-then-single path exact, because 53 ≥ 24 + 2 [I, a standard result].
`long` → `double` needs no helper: `Number(bigint)` already rounds once (R4).

## R6 — Runtime surface that sees the values

- **Equality** [V], `targets/js/metano-runtime/src/system/equality.ts` `equals` L35-45: the
  `typeof a !== typeof b` test makes `5n` unequal to `5`, as C# boxed equality is; `5n === 5n` is
  settled by the first identity test. No change needed for equality itself.
- **Hashing** [V], `system/hash-code.ts` L81: `if (typeof value === "bigint") return Number(value & 0xffffffffn) | 0;`.
  It is consistent with equality but keeps only the low 32 bits, so ids or ticks that differ in the high
  word collide. .NET folds both words (R2).
- **Type checks** [V], `type-checking/primitive-type-checks.ts` L57-75: `isInt64` / `isUInt64` test a
  `number` in the safe-integer range; `isBigInt` (L96) tests `typeof === "bigint"`.
- **Compiler type checks** [V]: `IrTypeCheckBuilder.TypeofKind` (L214-230) puts `Int64` in the `"number"`
  kind; `PrimitiveCheck` (L246-276) maps `Int64` → `isInt64`; `IrRuntimeRequirementScanner`
  `PrimitiveTypeCheckHelper` (L254-268) imports the same helper; `TypeGuardBuilder.FieldTestOf`
  (L749-754) already emits `typeof x === "bigint"` for a `TsBigIntType` field with
  `GuardTestKind.BigInt`, so `[GenerateGuard]` follows the TS type with no change.
- **Dispatch names** [V]: `IrToTsOverloadDispatcherBridge.cs` L539 and `IrToTsClassBridge.Operators.cs`
  L293 name `Int64` overloads `…Long`.
- **Zero values** [V]: `IrToTsClassBridge.ComputeDefaultInitializer` (L112-130) and
  `IrToTsJsxComponentBridge` (L201-216) emit `0` for `Int64`.
- **LINQ** [V], `system/linq/terminals.ts`: `sum` (L226-237), `min` / `max` (L239-266) and `average`
  (L323-342) start from `0` / `±Infinity` and use `+` / `<`, so a `bigint` source throws `TypeError`.
  `compareKeys` (`compare-keys.ts`) already compares `bigint` with `<` / `>`, so `OrderBy` /
  `MinBy` / `MaxBy` keyed by a `long` work.
- **Parsing** [V], `system/number-parsing.ts`: `tryParseInt64` parses exactly with `BigInt` and returns
  `Number(...)`; only `(string, out long)` is mapped (`src/Metano/Runtime/Parsing.cs` L19-24).
- **Math** [V], `src/Metano/Runtime/Math.cs` L49-56: `Abs`, `Min`, `Max` map by name to the JS `Math`
  methods for every overload, which throw on `bigint` (R4).
- **Formatting** [V]: `IrInterpolationExpression.FormatSpecifier` (`IrExpression.cs` L616-619) is
  declared but read nowhere, so `$"{x:N0}"` drops the format for every type today. No numeric format
  helper exists in the runtime.

## R7 — JSON

STJ behavior on .NET 10 [V] (probe 1):

```text
STJ serialize long.MaxValue = "9223372036854775807"
STJ serialize ulong.MaxValue = "18446744073709551615"
STJ deserialize 9223372036854775807 = 9223372036854775807 (Int64)
STJ deserialize 9223372036854775808 => JsonException: The JSON value could not be converted to System.Int64. Path: $ | LineNumber: 0 | BytePositionInLine: 19.
STJ deserialize 1.0 into long => JsonException: The JSON value could not be converted to System.Int64. Path: $ | LineNumber: 0 | BytePositionInLine: 3.
STJ deserialize 1e3 into long => JsonException: The JSON value could not be converted to System.Int64. Path: $ | LineNumber: 0 | BytePositionInLine: 3.
STJ deserialize -0 into long = 0 (Int64)
STJ deserialize -1 into ulong => JsonException: The JSON value could not be converted to System.UInt64. Path: $ | LineNumber: 0 | BytePositionInLine: 2.
STJ deserialize "5" into long => JsonException: The JSON value could not be converted to System.Int64. Path: $ | LineNumber: 0 | BytePositionInLine: 3.
STJ deserialize "5" into long AllowReadingFromString = 5 (Int64)
STJ WriteAsString long = ""9223372036854775807""
STJ Dictionary<long,int> write = "{"9223372036854775807":1,"-3":2}"
STJ Dictionary<long,int> read = "9223372036854775807"
STJ Dictionary<long,int> read +5 = "5"
STJ Dictionary<long,int> read ' 5' => JsonException: The JSON value could not be converted to System.Int64. Path: $[' 5'] | LineNumber: 0 | BytePositionInLine: 6.
STJ Dictionary<long,int> read 05 = "5"
STJ Dictionary<ulong,int> read -0 => JsonException: The JSON value could not be converted to System.UInt64. Path: $.-0 | LineNumber: 0 | BytePositionInLine: 6.
STJ Dictionary<long,int> read 1e3 => JsonException: The JSON value could not be converted to System.Int64. Path: $.1e3 | LineNumber: 0 | BytePositionInLine: 7.
STJ long? null = "null"
STJ object boxed long = "9223372036854775807"
```

So a JSON number for `long` must be an integer token in range (`1.0` and `1e3` are rejected; `-0` is
accepted), and a dictionary key uses the same grammar as spec 009 FR-048 item 4 (signed:
`[+-]?[0-9]+`, unsigned: `[0-9]+`, leading zeros allowed, no whitespace).

Runtime today [V]:

- `system/json/number-from-string.ts` `integerReader` (L81-92) reads `System.Int64` / `System.UInt64`
  strings exactly with `BigInt`, range-checks them (L149-154), then returns `Number(value)`; its doc
  comment (L77-80) records the precision loss and names #450.
- `system/json/exact-json-number.ts` (#458) already exports `writeExactNumber(text, approximate)` and
  `rawJsonTextOf(value)`; its doc comment names `long` (#450) as the next user.
- `system/json/numeric-converters.ts` `primitiveConverterOf` passes values through both ways, so a
  `bigint` would reach `JSON.stringify` and throw (R4).
- `system/json/converters.ts` map converter (L130-166) builds keys with `String(keyConv.serialize(k))`
  and reads them with `keyConv.deserialize(k)`, where `k` is the JSON property name (a string). With a
  `primitive` key descriptor the key stays a string, so `Dictionary<int, V>` keys deserialize as
  strings today [I, from the code; no test covers numeric keys].
- `system/json/types.ts` `NumberTypeName` (L7-17) documents "a C# numeric type that maps to a TS
  `number`".
- The codegen adds `numberType` from `JsonTypeClassifier.NumberTypeNameOf` (L101-118, includes
  `Int64` / `UInt64`) in `JsonTypeDescriptorBuilder` (L224-240).
- Every read starts from a tree the caller parsed: `JsonSerializer.deserialize(data, spec)` takes
  `unknown` (`json-serializer.ts` L58-63, L113-125). The runtime never sees the JSON text, so it cannot
  recover digits `JSON.parse` already rounded. #457 owns the exact text path (reviver `context.source` or
  a pull tokenizer, chosen per root spec when the plan is compiled).

## R8 — Cost of `bigint` (probe 5)

Ten million additions in a loop, median of five runs after one warm-up [V]:

| Loop | Node 24.11 | Bun 1.4.2 |
|---|---|---|
| `number`, `(s + i) \| 0` | 4.7 ms | 3.2 ms |
| `number`, `s + i` | 9.7 ms | 3.2 ms |
| `bigint`, `s + i` (no wrap) | 178.9 ms | 160.1 ms |
| `bigint`, `asIntN(64, s + i)` and `asIntN(64, i + 1n)` | 56.0 ms | 304.5 ms |
| `bigint` sum of a `number` counter, `asIntN(64, s + BigInt(i))` | 154.9 ms | 218.1 ms |

`bigint` arithmetic costs 12–95× a `number` loop. V8 recognises `BigInt.asIntN(64, …)` and lowers it to
native 64-bit arithmetic, so on Node the wrapped loop is **faster** than the unwrapped one; JavaScriptCore
(Bun) does not, so there the wrap doubles the cost [V for the numbers, I for the cause]. The wrap is
therefore not a cost to avoid on V8, and the mixed loop shows that `BigInt(i)` conversions are the dominant
cost there. The probe is a micro-benchmark, not a gate; the plan's benchmark (plan.md) is the gate.

## R9 — Dart

`src/Metano.Compiler.Dart/Bridge/IrToDartTypeMapper.cs` L87-88 maps `Byte`, `Int16`, `Int32` and `Int64` to
`int` [V]. A Dart `int` is a 64-bit two's-complement integer on the VM and AOT, so `long` arithmetic
already wraps there; on the web it is a JS `number` [I, Dart language docs]. Unsigned 64-bit has no
native Dart type. The Dart target is a shape-only prototype (no bodies; CLAUDE.md).

## R10 — Interop surfaces that see the change

- `[PlainObject]` fields become `field?: bigint`. Hand-written code that sends such an object through
  `JSON.stringify` (for example `samples/SampleTodo.Service/Program.cs` L18-54, which calls Hono's
  `c.Json(...)` on transpiled objects) throws `TypeError` (R4) once a field is `bigint`. Only the
  generated `JsonSerializerContext` writes it [I].
- `[Import]` / `[Emit]` / `[External]` (`src/Metano/Annotations/`, `Annotations/TypeScript/ExternalAttribute.cs`; there is no `[NoEmit]` attribute in the source, although CLAUDE.md lists one) and user `[MapMethod]` / `[MapProperty]` templates declarations describe JavaScript the compiler does not
  generate. A `long` in their signature becomes `bigint` on the TS side, but the JS implementation may
  still pass or expect a `number`; TypeScript cannot catch that because the declaration itself is the
  only type source [I].
- Generated TS consumed by hand-written TS: the TS types change from `number` to `bigint`, so `tsgo`
  reports every mismatch in consumer code at build time [I].
- TypeScript requires `target` ≥ ES2020 for `bigint` literals (TS2737). Every `targets/js/*/tsconfig*.json`
  uses `ES2023` or `ESNext` [V].


## R11 — Review probes (2026-11-04, after the first `compiler-man` review)

`uint` with `int` promotes to `long` (the reviewer's probe, `scratchpad/p450`, rerun here) [V]:

```csharp
uint ui = 1; int i = -2;
var a = ui + i; var b = -ui; var c = ui < i; var d = ui * i;
```

```text
Int64 -1 Int64 -1 False Int64
```

So `ui + i`, `-ui` and `ui * i` are `long` expressions and `ui < i` compares in 64 bits (§12.4.7.3 binary
numeric promotions [S]). Code that never names `long` therefore produces 64-bit operations.

Floating point to `int` and narrower, and shifts in a checked block (.NET 10) [V]:

```text
(int)3.7 = 3; (int)-3.7 = -3; (int)1e10 = 2147483647; (int)NaN = 0; (uint)-3.7 = 0; (byte)300.5 = 44
checked((int)1e10) => OverflowException: Arithmetic operation resulted in an overflow.
checked(l <<= ...) = 0          // checked { l <<= 63; l <<= 1; } from l = 5: no exception
```

`(byte)300.5` is `44` (`300 mod 256`): a narrow target saturates to `int` and keeps the low bits.

Mixed `bigint` / `number` comparisons are exact; `BigInt` rejects fractions (Node 24.11) [V]:

```text
5n < 6 = true
9007199254740993n > 9007199254740992 = true
9007199254740993n == 9007199254740992 = false
BigInt(3.7) => The number 3.7 cannot be converted to a BigInt because it is not an integer
```

Release flow on `90be699e` [V]: `.github/workflows/release.yml` triggers on `workflow_dispatch` only;
`.releaserc.json` has `"branches": ["main"]`; there is no `release/3.0` branch; `main` holds the breaking
commits `a4b03cd2` and `e79a114b` after `v2.10.1`, so the next release from `main` is 3.0.0.

Other facts the review raised [S, I]: `long.MinValue / -1` with constant operands is CS0220 in a checked
context and folded under `unchecked`; `nint.MaxValue` is not a constant (CS0133 in a `const` initializer); a
`[Flags]` enum `: long` lowered to `number` loses members of 2^31 and above in JS `|` / `&` (32-bit
operators); a Dart `int` is signed, so a `ulong` ≥ 2^63 is negative there.