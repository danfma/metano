using Metano.Compiler.Diagnostics;
using Metano.Compiler.TypeScript;

namespace Metano.Tests;

/// <summary>
/// <c>checked</c> / <c>unchecked</c> expressions and statements (spec 012
/// FR-023, DECIDED-4): Roslyn already folds the context into the
/// <c>IsChecked</c> flag of every operation inside, so the expression lowers to
/// its operand and the statement to its block, where both used to report
/// <c>MS0001</c> and emit a throwing placeholder. Operations narrower than 64
/// bits keep their unbounded <c>number</c> semantics in either context (OQ-4).
/// </summary>
public class CheckedContextTests
{
    private const string Int32Source = """
        [Transpile]
        public class Counter
        {
            private int _total;

            public int Add(int a, int b) => checked(a + b);

            public int Hash(int a) => unchecked(a * 31 + 7);

            public int Accumulate(int[] values)
            {
                checked
                {
                    foreach (var v in values)
                        _total += v;
                }

                return _total;
            }

            public int Nested(int a)
            {
                unchecked
                {
                    var h = a * 17;
                    checked
                    {
                        h += 1;
                    }

                    return h;
                }
            }

            public Func<int, int> Doubler()
            {
                checked
                {
                    return x => x * 2;
                }
            }

            public int ShiftValue(int n) => checked(n <<= 1);

            public string Text(int a) => checked(a + 1).ToString();
        }
        """;

    [Test]
    public async Task Int32Context_LowersTransparently()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(Int32Source);

        await Assert
            .That(files["counter.ts"])
            .IsEqualTo(TranspileHelper.ReadExpected("checked-unchecked-int32.ts"));

        await Assert.That(UnsupportedFeatures(diagnostics)).IsEmpty();
    }

    [Test]
    public async Task CheckedBlock_LowersToABlock_WithItsLambdaInside()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
            public class Ops
            {
                public long InLambda(long l)
                {
                    checked
                    {
                        Func<long> f = () => l + 1;
                        return f();
                    }
                }
            }
            """
        );

        await Assert
            .That(files["ops.ts"])
            .Contains(
                """
                    {
                      const f = () => checkedInt64(l + 1n);

                      return f();
                    }
                """
            );

        await Assert.That(UnsupportedFeatures(diagnostics)).IsEmpty();
    }

    [Test]
    public async Task CheckedShiftAssignment_LowersLikeTheUncheckedOne()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
            public class Ops
            {
                public long Field;
                public long ShiftLocal(long l) { var r = checked(l <<= 1); return r + l; }
                public long ShiftField() => checked(Field <<= 1);
                public long ShiftBlock(long l)
                {
                    checked
                    {
                        l <<= 1;
                    }

                    return l;
                }
            }
            """
        );

        var ops = files["ops.ts"];

        await Assert.That(ops).Contains("const r = l = BigInt.asIntN(64, l << 1n);");
        await Assert.That(ops).Contains("return this.field = BigInt.asIntN(64, this.field << 1n);");
        await Assert.That(ops).Contains("{\n      l = BigInt.asIntN(64, l << 1n);\n    }");
        await Assert.That(UnsupportedFeatures(diagnostics)).IsEmpty();
    }

    /// <summary>
    /// Local functions are not supported by the transpiler (<c>MS0001</c>
    /// <c>LocalFunctionStatement</c>), so only the <c>checked</c> block around
    /// one lowers; the local function keeps its own report.
    /// </summary>
    [Test]
    public async Task CheckedBlock_AroundALocalFunction_ReportsOnlyTheLocalFunction()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
            public class Ops
            {
                public long Local(long l)
                {
                    checked
                    {
                        long G() => l * 2;
                        return G();
                    }
                }
            }
            """
        );

        var messages = UnsupportedFeatures(diagnostics).Select(d => d.Message).ToList();

        await Assert.That(messages).HasSingleItem();
        await Assert.That(messages[0]).Contains("LocalFunctionStatement");
        await Assert.That(files["ops.ts"]).DoesNotContain("CheckedStatement");
    }

    /// <summary>
    /// C# evaluates a constant inside <c>unchecked</c> at compile time modulo
    /// its type; the lowering takes Roslyn's value rather than printing the
    /// unbounded operation.
    /// </summary>
    private const string WrappedConstantsSource = """
        [Transpile]
        public class Hashing
        {
            public const int Offset = unchecked((int)2166136261);
            public const int MinBit = unchecked((int)0x80000000);
            public const ulong AllOnes = unchecked((ulong)-1);
            public const uint U = unchecked((uint)-1);
            public const byte B = unchecked((byte)300);

            public int Fnv(string s)
            {
                int h = unchecked((int)2166136261);
                foreach (var c in s)
                    h = unchecked((h ^ c) * 16777619);

                return h;
            }

            public int Mix(int a)
            {
                unchecked
                {
                    return a * (int)2166136261;
                }
            }

            public ulong Borrow() => unchecked(0UL - 1UL);
            public int Wrap() => unchecked(int.MaxValue + 1);
            public decimal Money() => unchecked((int)2166136261);
            public int Named() => unchecked(Offset);
        }
        """;

    [Test]
    [Arguments("static readonly offset: number = -2128831035;")]
    [Arguments("static readonly minBit: number = -2147483648;")]
    [Arguments("static readonly allOnes: bigint = 18446744073709551615n;")]
    [Arguments("static readonly u: number = 4294967295;")]
    [Arguments("static readonly b: number = 44;")]
    [Arguments("let h = -2128831035;")]
    [Arguments("h = (h ^ c) * 16777619;")]
    [Arguments("return a * -2128831035;")]
    [Arguments("return 18446744073709551615n;")]
    [Arguments("return -2147483648;")]
    [Arguments("return new Decimal(\"-2128831035\");")]
    [Arguments("return Hashing.offset;")]
    public async Task UncheckedConstants_LowerToTheirWrappedValue(string expected)
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(WrappedConstantsSource);

        await Assert.That(files["hashing.ts"]).Contains(expected);
        await Assert.That(UnsupportedFeatures(diagnostics)).IsEmpty();
    }

    [Test]
    [Arguments("static final int offset = -2128831035;")]
    [Arguments("static final int minBit = -2147483648;")]
    [Arguments("static final int allOnes = -1;")]
    [Arguments("static final int u = 4294967295;")]
    [Arguments("static final int b = 44;")]
    [Arguments("int h = -2128831035;")]
    [Arguments("return a * -2128831035;")]
    [Arguments("int borrow() => -1;")]
    [Arguments("int wrap() => -2147483648;")]
    public async Task Dart_UncheckedConstants_LowerToTheirWrappedValue(string expected)
    {
        var (files, diagnostics) = DartTranspileHelper.Transpile(WrappedConstantsSource);

        await Assert.That(files["hashing.dart"]).Contains(expected);
        await Assert.That(UnsupportedFeatures(diagnostics)).IsEmpty();
    }

    /// <summary>
    /// Enum and <c>char</c> targets wrap like the integral types: an enum
    /// keeps the cast a constant enum cast lowers to, around its wrapped
    /// underlying value, and a <c>char</c> becomes a character literal.
    /// </summary>
    private const string WrappedEnumAndCharSource = """
        [Transpile]
        public enum Level : byte { Low = 1 }

        [Transpile]
        public class Codes
        {
            public Level Wrapped() => unchecked((Level)300);
            public string Text() => unchecked((char)70000).ToString();
            public char Letter() => unchecked((char)70000);
            public int Code() => unchecked((char)70000);
        }
        """;

    [Test]
    [Arguments("return 44;")]
    [Arguments("return \"\u1170\".toString();")]
    [Arguments("letter(): string {\n    return \"\u1170\";")]
    [Arguments("code(): number {\n    return 4464;")]
    public async Task UncheckedEnumAndCharConstants_LowerToTheirWrappedValue(string expected)
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            WrappedEnumAndCharSource
        );

        await Assert.That(files["codes.ts"]).Contains(expected);
        await Assert.That(UnsupportedFeatures(diagnostics)).IsEmpty();
    }

    [Test]
    [Arguments("Level wrapped() => 44 as Level;")]
    [Arguments("String text() => '\u1170'.toString();")]
    [Arguments("String letter() => '\u1170';")]
    [Arguments("int code() => 4464;")]
    public async Task Dart_UncheckedEnumAndCharConstants_LowerToTheirWrappedValue(string expected)
    {
        var (files, diagnostics) = DartTranspileHelper.Transpile(WrappedEnumAndCharSource);

        await Assert.That(files["codes.dart"]).Contains(expected);
        await Assert.That(UnsupportedFeatures(diagnostics)).IsEmpty();
    }

    [Test]
    public async Task Dart_LowersCheckedContextsToTheirOperandAndBlock()
    {
        var (files, diagnostics) = DartTranspileHelper.Transpile(
            """
            [Transpile]
            public class Ops
            {
                public long Check(long a) => checked(a * 2);

                public long Block(long l)
                {
                    unchecked
                    {
                        l++;
                    }

                    return l;
                }
            }
            """
        );

        var ops = files["ops.dart"];

        await Assert.That(ops).Contains("int check(int a) => a * 2;");
        await Assert.That(ops).Contains("{\n      l = l + 1;\n    }");
        await Assert.That(UnsupportedFeatures(diagnostics)).IsEmpty();
    }

    /// <summary>
    /// The <c>foreach</c> element conversion checks like .NET under the
    /// compilation's <c>CheckOverflow</c> option alone (spec 012 DECIDED-4):
    /// no explicit context surrounds the loop, yet Roslyn reports the
    /// conversion checked through <see cref="CheckedContexts.IsChecked"/>'s
    /// compilation-option branch.
    /// </summary>
    [Test]
    public async Task ProjectCheckedOption_ForeachElementsConvertChecked()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithOverflowChecks(
            """
            [Transpile]
            public class Loops
            {
                public int Last(double[] doubles)
                {
                    var last = 0;

                    foreach (int x in doubles)
                        last = x;

                    return last;
                }
            }
            """
        );

        await Assert
            .That(files["loops.ts"])
            .Contains("const x = int32FromDoubleChecked(x$element);");
        await Assert.That(UnsupportedFeatures(diagnostics)).IsEmpty();
    }

    /// <summary>
    /// The innermost context decides (spec 012 DECIDED-4): a
    /// <c>foreach</c> inside <c>checked { unchecked { … } }</c> converts
    /// unchecked, its sibling inside <c>checked</c> alone converts checked.
    /// </summary>
    [Test]
    public async Task NestedCheckedUnchecked_ForeachFollowsTheInnermostContext()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
            public class Loops
            {
                public int Last(double[] doubles)
                {
                    var last = 0;

                    checked
                    {
                        unchecked
                        {
                            foreach (int x in doubles)
                                last = x;
                        }
                    }

                    checked
                    {
                        foreach (int y in doubles)
                            last = y;
                    }

                    return last;
                }
            }
            """
        );

        var output = files["loops.ts"];

        await Assert.That(output).Contains("const x = int32FromDouble(x$element);");
        await Assert.That(output).Contains("const y = int32FromDoubleChecked(y$element);");
        await Assert.That(UnsupportedFeatures(diagnostics)).IsEmpty();
    }

    /// <summary>
    /// A lambda declared inside a <c>checked</c> <c>foreach</c> inherits the
    /// context, as C# specifies, so its own conversion checks too.
    /// </summary>
    [Test]
    public async Task LambdaInsideCheckedForeach_ConvertsChecked()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
            public class Loops
            {
                public int Last(double[] doubles)
                {
                    var last = 0;

                    checked
                    {
                        foreach (double d in doubles)
                        {
                            Func<int> read = () => (int)d;
                            last = read();
                        }
                    }

                    return last;
                }
            }
            """
        );

        await Assert
            .That(files["loops.ts"])
            .Contains("const read = () => int32FromDoubleChecked(d);");
        await Assert.That(UnsupportedFeatures(diagnostics)).IsEmpty();
    }

    private static List<MetanoDiagnostic> UnsupportedFeatures(
        IEnumerable<MetanoDiagnostic> diagnostics
    ) => diagnostics.Where(d => d.Code == DiagnosticCodes.UnsupportedFeature).ToList();
}
