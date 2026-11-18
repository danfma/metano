using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// <c>int</c> / <c>uint</c> arithmetic wraps in the default (unchecked)
/// context (spec 001 FR-074–FR-076, issue #639 layer 3): TypeScript reduces
/// the result of <c>+</c>, <c>-</c>, <c>*</c> and unary <c>-</c> and the
/// stores of <c>++</c>, <c>--</c>, <c>+=</c>, <c>-=</c>, <c>*=</c> with
/// <c>| 0</c>, <c>&gt;&gt;&gt; 0</c> and <c>Math.imul</c>; the core omits a
/// reduction C# proves unneeded; a JavaScript consumer that reduces modulo
/// 2^32 itself drops the trailing reduction of its operand. Dart is out of
/// scope and prints its 3.0 shapes. Every golden type-checks under
/// <c>tsgo --strict</c>.
/// </summary>
public class Int32WrapTests
{
    private const string ArithmeticSource = """
        #nullable enable

        [Transpile]
        public class Wraps
        {
            public int Field;
            public uint Unsigned;
            public int Prop { get; set; }
            public int[] Cells = new int[4];

            public int Add(int a, int b) => a + b;
            public int Sub(int a, int b) => a - b;
            public int Mul(int a, int b) => a * b;
            public int Neg(int a) => -a;
            public uint UAdd(uint a, uint b) => a + b;
            public uint USub(uint a, uint b) => a - b;
            public uint UMul(uint a, uint b) => a * b;
            public long UNeg(uint a) => -a;
            public int Bytes(byte a, byte b) => a * b * a;
            public int CharTimes(char c) => c * 70000;
            public int CharPlus(char c) => c + 1;
            public int Constant() => unchecked(int.MaxValue + 1);
            public int Divide(int a, int b) => a / b;
            public int? Lifted(int? a, int? b) => a + b;
            public int? LiftedProduct(int? a, int b) => a * b;

            public int Steps(int a)
            {
                a++;
                ++a;
                a--;
                a += 5;
                a -= 1;
                a *= 3;
                return a;
            }

            public uint UnsignedSteps(uint u)
            {
                u--;
                u *= 3;
                return u;
            }

            public int Postfix(int a)
            {
                var old = a++;
                return old + a;
            }

            public int Prefix(int a) => ++a;

            public void Stores(int k)
            {
                Field += k;
                Unsigned -= 1;
                Prop *= k;
                Cells[k] += 2;
                Cells[k]++;
            }
        }
        """;

    private const string ConsumerSource = """
        [Transpile]
        public class Consumers
        {
            public int ProductPlus(int a, int b, int c) => a * b + c;
            public int Chain(int a, int b, int c) => a + b + c - a * b;
            public uint UProductPlus(uint a, uint b) => a * b + 1;
            public int Masked(int a, int b) => (a + b) & 0xFF;
            public int Shifted(int a, int b, int n) => (a + b) << n;
            public int Complement(int a, int b) => ~(a + b);
            public byte Narrowed(int a, int b) => (byte)(a + b);
            public short NarrowedProduct(int a, int b) => (short)(a * b);
            public int Signed(uint a, uint b) => (int)(a + b);
            public int MinusNegated(int a, int b) => a - -b;
            public int MinusSum(int a, int b, int c) => a - (b + c);
            public int MinusDifference(int a, int b, int c) => a - (b - c);
            public int NegatedSum(int a) => -(a + 1);
            public int ProductOfSum(int a, int b, int c) => (a + b) * c;
            public bool Compared(int a, int b) => a + b < 0;
            public int Halved(int a, int b) => (a + b) / 2;
            public long Widened(int a, int b) => a + b;
            public double AsDouble(int a, int b) => a * b;
            public string Text(int a, int b) => "v" + (a + b);
            public int Argument(int a, int b) => Take(a + b);
            public int CheckedOuter(int a, int b, int c) => checked(a + unchecked(b * c));
            public byte CheckedNarrowing(int a, int b) => checked((byte)unchecked(a + b));

            private static int Take(int value) => value;
        }
        """;

    private const string ElisionSource = """
        using System.Collections.Generic;

        [Transpile]
        public sealed class Bag
        {
            public int Count => 3;
        }

        [Transpile]
        public class Elisions
        {
            public int LastIndex(int[] a) => a.Length - 1;
            public int TextLast(string s) => s.Length - 1;
            public int ListLast(List<int> l) => l.Count - 1;
            public int DictionaryLast(Dictionary<string, int> d) => d.Count - 1;
            public int SetDouble(HashSet<int> s) => s.Count * 2;
            public int ListNext(List<int> l) => l.Count + 1;
            public int UserCount(Bag b) => b.Count - 1;
            public int Interface(ICollection<int> c) => c.Count - 1;
            public int Masked(int x) => (x & 0xFF) + 1;
            public int Bytes(byte a, byte b) => a + b;

            public int Up(int[] a)
            {
                var s = 0;
                for (var i = 0; i < a.Length; i++) s += a[i];
                return s;
            }

            public int Down(int n)
            {
                var s = 0;
                for (var i = n; i > 0; i--) s += i;
                return s;
            }

            public int Mirrored(int n)
            {
                var s = 0;
                for (var i = 0; n > i; ++i) s += i;
                return s;
            }

            public int Inclusive(byte n)
            {
                var s = 0;
                for (var i = 0; i <= n; i++) s += i;
                return s;
            }

            public int DownInclusive()
            {
                var s = 0;
                for (var i = 10; i >= 0; i--) s += i;
                return s;
            }

            public int Chained(int n, bool go)
            {
                var s = 0;
                for (var i = 0; go && i < n && s < 100; i++) s += i;
                return s;
            }

            public uint Unsigned(uint n)
            {
                uint s = 0;
                for (uint i = 0; i < n; i++) s += i;
                return s;
            }

            public int NestedComparison(int n)
            {
                var s = 0;
                for (var i = 0; !(i >= n); i++) s += i;
                return s;
            }

            public int ComparedToTrue(int n)
            {
                var s = 0;
                for (var i = 0; (i < n) == true; i++) s += i;
                return s;
            }

            public int Converted(int n)
            {
                var s = 0;
                for (var i = 0; (short)i < 100; i++) s += i;
                return s;
            }

            public int Promoted(long n)
            {
                var s = 0;
                for (var i = 0; i < n; i++) s += i;
                return s;
            }

            public int WrittenInBody(int n)
            {
                var s = 0;
                for (var i = 0; i < n; i++) { i += 2; s++; }
                return s;
            }

            public int PassedByRef(int n)
            {
                var s = 0;
                for (var i = 0; i < n; i++) Bump(ref i);
                return s;
            }

            public int WrittenByLambda(int n)
            {
                var s = 0;
                for (var i = 0; i < n; i++) { System.Action skip = () => i++; skip(); s++; }
                return s;
            }

            public int UnboundedInclusive(int n)
            {
                var s = 0;
                for (var i = 0; i <= n; i++) s++;
                return s;
            }

            public uint UnsignedDownToZero(uint n)
            {
                uint s = 0;
                for (uint i = n; i >= 0; i--) { s++; if (s > 3) break; }
                return s;
            }

            public int Unbounded(int n)
            {
                var s = 0;
                for (var i = 0; i != n; i++) s++;
                return s;
            }

            private static void Bump(ref int value) => value++;
        }
        """;

    [Test]
    public async Task Arithmetic_WrapsWithNativeOperators()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(ArithmeticSource);
        var wraps = files["wraps.ts"];

        await TranspileHelper.AssertGolden("int32-wrap.ts", wraps);
        await Assert.That(BodyOf(wraps, "add")).IsEqualTo("return (a + b) | 0;");
        await Assert.That(BodyOf(wraps, "mul")).IsEqualTo("return Math.imul(a, b);");
        await Assert.That(BodyOf(wraps, "uSub")).IsEqualTo("return (a - b) >>> 0;");
        await Assert.That(BodyOf(wraps, "uMul")).IsEqualTo("return Math.imul(a, b) >>> 0;");
        await Assert.That(BodyOf(wraps, "constant")).IsEqualTo("return -2147483648;");
        await Assert.That(BodyOf(wraps, "charPlus")).IsEqualTo("return c.charCodeAt(0) + 1;");
        await Assert.That(UnsupportedFeatures(diagnostics)).IsEmpty();
    }

    /// <summary>
    /// The difference of two enum values is an <c>int</c> and wraps; an enum
    /// plus an <c>int</c> is an enum and does not (FR-074): it is asserted as
    /// the enum's closed union instead (spec 011 FR-044).
    /// </summary>
    [Test]
    public async Task EnumArithmetic_WrapsOnlyAnIntResult()
    {
        var (files, _) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
            public class Levels
            {
                public enum Level { Low = 1, High = 40 }

                public int Distance(Level a, Level b) => a - b;
                public Level Step(Level a) => a + 1;
            }
            """
        );

        var levels = files["levels.ts"];

        await Assert.That(BodyOf(levels, "distance")).IsEqualTo("return (a - b) | 0;");
        await Assert.That(BodyOf(levels, "step")).IsEqualTo("return a + 1 as Levels.Level;");
    }

    [Test]
    public async Task Consumers_ThatReduceThemselves_DropTheTrailingReduction()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(ConsumerSource);
        var consumers = files["consumers.ts"];

        await TranspileHelper.AssertGolden("int32-wrap-consumers.ts", consumers);

        await Assert
            .That(BodyOf(consumers, "productPlus"))
            .IsEqualTo("return (Math.imul(a, b) + c) | 0;");

        await Assert
            .That(BodyOf(consumers, "chain"))
            .IsEqualTo("return (a + b + c - Math.imul(a, b)) | 0;");

        await Assert
            .That(BodyOf(consumers, "uProductPlus"))
            .IsEqualTo("return (Math.imul(a, b) + 1) >>> 0;");

        await Assert.That(BodyOf(consumers, "minusNegated")).IsEqualTo("return (a - -b) | 0;");
        await Assert.That(BodyOf(consumers, "minusSum")).IsEqualTo("return (a - (b + c)) | 0;");

        await Assert
            .That(BodyOf(consumers, "minusDifference"))
            .IsEqualTo("return (a - (b - c)) | 0;");

        await Assert.That(BodyOf(consumers, "negatedSum")).IsEqualTo("return -(a + 1) | 0;");

        await Assert
            .That(BodyOf(consumers, "checkedOuter"))
            .IsEqualTo("return checkedInt32(a + Math.imul(b, c));");

        await Assert
            .That(BodyOf(consumers, "checkedNarrowing"))
            .IsEqualTo("return checkedNarrowNumber((a + b) | 0, 8, false);");

        await Assert.That(UnsupportedFeatures(diagnostics)).IsEmpty();
    }

    [Test]
    public async Task ProvenRanges_OmitTheReduction()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(ElisionSource);
        var elisions = files["elisions.ts"];

        await TranspileHelper.AssertGolden("int32-wrap-elisions.ts", elisions);
        await Assert.That(BodyOf(elisions, "lastIndex")).IsEqualTo("return a.length - 1;");
        await Assert.That(BodyOf(elisions, "listNext")).IsEqualTo("return (l.length + 1) | 0;");
        await Assert.That(BodyOf(elisions, "userCount")).Contains("| 0;");
        await Assert.That(BodyOf(elisions, "interface")).Contains("| 0;");
        await Assert.That(elisions).Contains("for (let i = 0; i < a.length; i++) {");
        await Assert.That(elisions).Contains("for (let i = n; i > 0; i--) {");
        await Assert.That(elisions).Contains("for (let i = 0; n > i; ++i) {");
        await Assert.That(elisions).Contains("for (let i = 0; i <= n; i++) {");
        await Assert.That(elisions).Contains("for (let i = 10; i >= 0; i--) {");

        // The `ref i` argument of PassedByRef is passed by value (an MS0001 of its own).
        await Assert.That(UnsupportedFeatures(diagnostics).Count).IsEqualTo(1);
    }

    [Test]
    [Arguments("nestedComparison", "i = (i + 1) | 0")]
    [Arguments("comparedToTrue", "i = (i + 1) | 0")]
    [Arguments("converted", "i = (i + 1) | 0")]
    [Arguments("promoted", "i = (i + 1) | 0")]
    [Arguments("writtenInBody", "i = (i + 1) | 0")]
    [Arguments("passedByRef", "i = (i + 1) | 0")]
    [Arguments("writtenByLambda", "i = (i + 1) | 0")]
    [Arguments("unboundedInclusive", "i = (i + 1) | 0")]
    [Arguments("unsignedDownToZero", "i = (i - 1) >>> 0")]
    [Arguments("unbounded", "i = (i + 1) | 0")]
    public async Task UnprovenCounterSteps_KeepTheWrap(string method, string step)
    {
        var (files, _) = TranspileHelper.TranspileWithDiagnostics(ElisionSource);

        await Assert.That(LoopHeaderOf(files["elisions.ts"], method)).Contains(step);
    }

    /// <summary>
    /// The proof of a bounded step holds in either context (FR-075), so a
    /// checked project keeps the native step too.
    /// </summary>
    [Test]
    public async Task BoundedStep_InACheckedProject_KeepsTheNativeOperator()
    {
        var (files, _) = TranspileHelper.TranspileWithOverflowChecks(ElisionSource);
        var elisions = files["elisions.ts"];

        await Assert.That(elisions).Contains("for (let i = 0; i < a.length; i++) {");
        await Assert.That(LoopHeaderOf(elisions, "unbounded")).Contains("checkedInt32(i + 1)");
    }

    /// <summary>
    /// A dictionary entry has no read-modify-write (the read of a missing
    /// key would not throw). The store FR-074 adds, an unchecked
    /// <c>int</c> / <c>uint</c> arithmetic store that now wraps, reports
    /// MS0001 as an error: the build fails, as it did when <c>tsc</c>
    /// rejected the <c>counts.get(key)++</c> it emitted before. The stores
    /// reported before FR-074 (checked, narrower than <c>int</c>) keep their
    /// warning.
    /// </summary>
    [Test]
    [Arguments("counts[key]++;", "int", MetanoDiagnosticSeverity.Error)]
    [Arguments("counts[key]--;", "uint", MetanoDiagnosticSeverity.Error)]
    [Arguments("counts[key] += 2;", "int", MetanoDiagnosticSeverity.Error)]
    [Arguments("counts[key] *= 3;", "uint", MetanoDiagnosticSeverity.Error)]
    [Arguments("checked { counts[key]++; }", "int", MetanoDiagnosticSeverity.Warning)]
    [Arguments("counts[key]++;", "byte", MetanoDiagnosticSeverity.Warning)]
    [Arguments("counts[key] <<= 1;", "int", MetanoDiagnosticSeverity.Warning)]
    public async Task StoreOnADictionaryEntry_ReportsUnsupported(
        string store,
        string valueType,
        MetanoDiagnosticSeverity severity
    )
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            $$"""
            using System.Collections.Generic;

            [Transpile]
            public class Tally
            {
                public void Count(Dictionary<string, {{valueType}}> counts, string key) { {{store}} }
            }
            """
        );

        var unsupported = UnsupportedFeatures(diagnostics);

        await Assert.That(unsupported.Count).IsEqualTo(1);
        await Assert.That(unsupported[0].Severity).IsEqualTo(severity);
    }

    /// <summary>
    /// A lifted store on a dictionary entry (<c>int?</c>, <c>uint?</c>) was
    /// reported as a warning before FR-074, with a throwing placeholder that
    /// compiles, and still is: only the non-lifted wrapped store is an error.
    /// </summary>
    [Test]
    [Arguments("counts[key]++;", "int?")]
    [Arguments("counts[key] += 1u;", "uint?")]
    public async Task LiftedStoreOnADictionaryEntry_StaysAWarning(string store, string valueType)
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            $$"""
            using System.Collections.Generic;

            [Transpile]
            public class Tally
            {
                public void Count(Dictionary<string, {{valueType}}> counts, string key) { {{store}} }
            }
            """
        );

        var unsupported = UnsupportedFeatures(diagnostics);

        await Assert.That(unsupported.Count).IsEqualTo(1);
        await Assert.That(unsupported[0].Severity).IsEqualTo(MetanoDiagnosticSeverity.Warning);
    }

    /// <summary>
    /// A checked <c>byte</c> store on a dictionary entry was reported as a
    /// warning before FR-074 and still is, so Dart still transpiles it, and
    /// its output is byte-identical to the integration base's.
    /// </summary>
    [Test]
    public async Task Dart_CheckedByteStoreOnADictionaryEntry_StaysAWarning()
    {
        var (files, diagnostics) = DartTranspileHelper.Transpile(
            """
            using System.Collections.Generic;

            [Transpile]
            public class Tally
            {
                public void Count(Dictionary<string, byte> counts, string key)
                {
                    checked { counts[key]++; }
                }
            }
            """
        );

        var unsupported = UnsupportedFeatures(diagnostics);

        await Assert.That(unsupported.Count).IsEqualTo(1);
        await Assert.That(unsupported[0].Severity).IsEqualTo(MetanoDiagnosticSeverity.Warning);
        await TranspileHelper.AssertGolden("dart-dictionary-entry-store.dart", files["tally.dart"]);
    }

    /// <summary>
    /// Dart is out of scope (FR-074): unchecked <c>int</c> / <c>uint</c>
    /// arithmetic keeps its 3.0 shapes, with no <c>toSigned(32)</c>.
    /// </summary>
    [Test]
    public async Task Dart_KeepsUncheckedArithmeticUnwrapped()
    {
        var (files, diagnostics) = DartTranspileHelper.Transpile(ArithmeticSource);
        var wraps = files["wraps.dart"];

        await Assert.That(wraps).Contains("int add(int a, int b) => a + b;");
        await Assert.That(wraps).Contains("int mul(int a, int b) => a * b;");
        await Assert.That(wraps).Contains("int uSub(int a, int b) => a - b;");
        await Assert.That(wraps).Contains("a++;");
        await Assert.That(wraps).DoesNotContain("toSigned(32)");
        await Assert.That(wraps).DoesNotContain("toUnsigned(32)");
        await Assert.That(UnsupportedFeatures(diagnostics)).IsEmpty();
    }

    /// <summary>
    /// The facts FR-075 reads for TypeScript (a <c>char</c> code, a
    /// collection count, a bounded loop step) are gated off for Dart, so
    /// Dart prints its pre-#639-layer-3 narrowings and checked steps: the
    /// golden is byte-identical to the output of the integration base.
    /// </summary>
    [Test]
    public async Task Dart_KeepsItsNarrowingsAndCheckedSteps()
    {
        var (files, _) = DartTranspileHelper.Transpile(
            """
            using System.Collections.Generic;

            [Transpile]
            public class Scope
            {
                public uint TextLength(string s) => (uint)s.Length;
                public uint ArrayLength(int[] a) => (uint)a.Length;
                public uint ListCount(List<int> l) => (uint)l.Count;
                public uint CharNext(char c) => (uint)(c + 1);
                public int CharUnsigned(char c) => (int)(uint)c;
                public ushort CharSame(char c) => (ushort)(c + 0);
                public byte Masked(int x) => (byte)(x & 0xFF);
                public int Add(int a, int b) => a + b;

                public int Sum(int n)
                {
                    var s = 0;
                    checked { for (var i = 0; i < n; i++) s += i; }
                    return s;
                }
            }
            """
        );

        await TranspileHelper.AssertGolden("dart-int32-wrap-scope.dart", files["scope.dart"]);
    }

    private static string BodyOf(string source, string name)
    {
        var lines = source.Split('\n');
        var header = Array.FindIndex(lines, line => line.Trim().StartsWith(name + "("));

        if (header < 0)
            throw new InvalidOperationException($"No method '{name}' in the generated source.");

        return lines[header + 1].Trim();
    }

    /// <summary>The header of the first <c>for</c> loop in the method <paramref name="name"/>.</summary>
    private static string LoopHeaderOf(string source, string name)
    {
        var lines = source.Split('\n');
        var header = Array.FindIndex(lines, line => line.Trim().StartsWith(name + "("));

        if (header < 0)
            throw new InvalidOperationException($"No method '{name}' in the generated source.");

        return lines.Skip(header).First(line => line.TrimStart().StartsWith("for (")).Trim();
    }

    private static List<MetanoDiagnostic> UnsupportedFeatures(
        IEnumerable<MetanoDiagnostic> diagnostics
    ) => diagnostics.Where(d => d.Code == DiagnosticCodes.UnsupportedFeature).ToList();
}
