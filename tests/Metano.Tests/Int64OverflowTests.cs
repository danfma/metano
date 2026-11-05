using Metano.Compiler.Diagnostics;
using Metano.Compiler.TypeScript;

namespace Metano.Tests;

/// <summary>
/// The 64-bit overflow reduction of spec 012 slice 012c (FR-021–FR-026) in
/// the staging <see cref="Int64Representation.BigInt"/> mode: unchecked
/// results wrap with <c>BigInt.asIntN</c> / <c>asUintN</c>, checked ones go
/// through <c>checkedInt64</c> / <c>checkedUInt64</c>, increments and compound
/// assignments store the reduced value, lifted operators reduce their non-null
/// result. Every golden type-checks under <c>tsgo --strict</c>.
/// </summary>
public class Int64OverflowTests
{
    private const string UncheckedSource = """
        [Transpile]
        public class Wrap
        {
            public long Add(long l, long m) => l + m;
            public long Sub(long l, long m) => l - m;
            public long Mul(long l, long m) => l * m;
            public ulong UAdd(ulong u, ulong v) => u + v;
            public ulong USub(ulong u, ulong v) => u - v;
            public ulong UMul(ulong u, ulong v) => u * v;
            public long AddInt(long l, int i) => l + i;
            public ulong UAddConst(ulong u) => u + 1;
            public long Neg(long l) => -l;
            public long Not(long l) => ~l;
            public ulong UNot(ulong u) => ~u;
            public long Bitwise(long l, long m) => l & m | l ^ m;
            public bool Compare(long l, long m) => l + 1 < m;
            public long Promote(uint ui, int i) => ui + i;
            public long PromoteMul(uint ui, int i) => ui * i;
            public long PromoteNeg(uint ui) => -ui;
            public int PromoteNarrow(uint ui, int i) => (int)(ui + i);
            public long Unchecked(long l, long m) => unchecked(l * m);
            public long Chain(long a, long b, long c) => a * b + c;
            public nint Native(nint a, nint b) => a + b;
            public nuint UNative(nuint a, nuint b) => a * b;
            public long? Lifted(long? nl) => nl + 1;
            public long? LiftedPair(long? nl, long? nm) => nl * nm;
            public long? LiftedNeg(long? nl) => -nl;
            public ulong? LiftedNot(ulong? nu) => ~nu;
            public int Narrow(int a, int b) => a + b;
            public double Floating(double a, double b) => a * b;
        }
        """;

    private const string CheckedSource = """
        using System;

        [Transpile]
        public class Check
        {
            public long Add(long l, long m) => checked(l + m);
            public long Sub(long l, long m) => checked(l - m);
            public long Mul(long l, long m) => checked(l * m);
            public ulong UAdd(ulong u, ulong v) => checked(u + v);
            public ulong USub(ulong u, ulong v) => checked(u - v);
            public ulong UMul(ulong u, ulong v) => checked(u * v);
            public long AddInt(long l, int i) => checked(l + i);
            public long Neg(long l) => checked(-l);
            public ulong UNot(ulong u) => checked(~u);
            public long Promote(uint ui, int i) => checked(ui + i);
            public long? Lifted(long? nl) => checked(nl + 1);
            public long Inner(long l, long m) => checked(l + unchecked(l * m));

            public long Block(long l, long m)
            {
                checked
                {
                    var sum = l + m;
                    var product = unchecked(l * m);
                    return sum - product;
                }
            }

            public Func<long> Lambda(long l)
            {
                checked
                {
                    return () => l + 1;
                }
            }

            public Func<long, long> Nested(long l)
            {
                unchecked
                {
                    checked
                    {
                        Func<long, long> twice = x => x * 2;
                        return twice;
                    }
                }
            }

            public int Narrow(int a, int b) => checked(a + b);
        }
        """;

    private const string ProjectCheckedSource = """
        [Transpile]
        public class Project
        {
            private long _total;

            public long Add(long l, long m) => l + m;
            public ulong USub(ulong u, ulong v) => u - v;
            public long Neg(long l) => -l;
            public long Wrapped(long l) => unchecked(l + 1);

            public long Step(long l)
            {
                l++;
                _total += l;
                return _total;
            }

            public int Narrow(int a) => a + 1;
        }
        """;

    private const string IncrementsSource = """
        using System;

        [Transpile]
        public class Counter
        {
            private long _field;
            private int _cursor;

            public long Total { get; set; }
            public ulong Unsigned;
            public long? Maybe;
            public long[] Slots = new long[] { 1, 2, 3 };

            public int Next() => _cursor++;

            public long Locals(long l, ulong u)
            {
                l++;
                ++l;
                l--;
                --l;
                u++;
                u--;
                return l + (long)u;
            }

            public long Postfix(long l) { var old = l++; return old; }
            public long Prefix(long l) { var updated = ++l; return updated; }
            public long PostfixDecrement(long l) { var old = l--; return old - l; }

            public void Members(int i)
            {
                _field++;
                Total--;
                Total += i;
                Unsigned *= 3;
                _field -= Total;
            }

            public long Element()
            {
                Slots[Next()] *= 2;
                Slots[Next()]++;
                var old = Slots[Next()]++;
                return old;
            }

            public long Loop(long n)
            {
                long sum = 0;
                for (long i = 0; i < n; i++)
                    sum += i;
                return sum;
            }

            public long Bitwise(long l, long m)
            {
                l &= m;
                l |= 4;
                l ^= m;
                return l;
            }

            public long? Lifted(long? nl, long l)
            {
                nl++;
                nl += l;
                Maybe--;
                Maybe *= 2;
                return nl;
            }

            public long CheckedBlock(long l, int i)
            {
                checked
                {
                    l++;
                    l += i;
                    l *= 2;
                    Total -= 1;
                    _field = l--;
                }

                return l;
            }

            public Func<long> CheckedLambda(long l)
            {
                checked
                {
                    return () => ++l;
                }
            }

            public int Narrow(int a)
            {
                checked
                {
                    a++;
                    a += 2;
                }

                return a;
            }
        }
        """;

    [Test]
    public async Task BigIntMode_UncheckedArithmetic_Wraps()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            UncheckedSource,
            Int64Representation.BigInt
        );

        await Assert
            .That(files["wrap.ts"])
            .IsEqualTo(TranspileHelper.ReadExpected("int64-overflow-unchecked.ts"));

        await Assert.That(UnsupportedFeatures(diagnostics)).IsEmpty();
    }

    [Test]
    public async Task BigIntMode_CheckedArithmetic_CallsTheRangeChecks()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            CheckedSource,
            Int64Representation.BigInt
        );

        await Assert
            .That(files["check.ts"])
            .IsEqualTo(TranspileHelper.ReadExpected("int64-overflow-checked.ts"));

        await Assert.That(UnsupportedFeatures(diagnostics)).IsEmpty();
    }

    /// <summary>
    /// A project compiled with <c>CheckForOverflowUnderflow</c> checks every
    /// 64-bit operation outside an <c>unchecked</c> context (DECIDED-4).
    /// </summary>
    [Test]
    public async Task BigIntMode_ProjectWideOverflowChecks_CheckEveryOperation()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithOverflowChecks(
            ProjectCheckedSource,
            Int64Representation.BigInt
        );

        await Assert
            .That(files["project.ts"])
            .IsEqualTo(TranspileHelper.ReadExpected("int64-overflow-project-checked.ts"));

        await Assert.That(UnsupportedFeatures(diagnostics)).IsEmpty();
    }

    [Test]
    public async Task BigIntMode_IncrementsAndCompoundAssignments_StoreTheReducedValue()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            IncrementsSource,
            Int64Representation.BigInt
        );

        await Assert
            .That(files["counter.ts"])
            .IsEqualTo(TranspileHelper.ReadExpected("int64-increments-compound.ts"));

        await Assert.That(UnsupportedFeatures(diagnostics)).IsEmpty();
    }

    /// <summary>
    /// The shipped <c>Number</c> mode keeps the 2.x output (DECIDED-22): no
    /// wrap, no check, native increments and compound operators.
    /// </summary>
    [Test]
    public async Task NumberMode_KeepsTheShippedArithmetic()
    {
        var (files, _) = TranspileHelper.TranspileWithDiagnostics(
            IncrementsSource + CheckedSource.Replace("using System;", ""),
            Int64Representation.Number
        );

        var counter = files["counter.ts"];
        var check = files["check.ts"];

        await Assert.That(counter).Contains("l++;");
        await Assert.That(counter).Contains("this.total += i;");
        await Assert.That(counter).Contains("const old = l++;");
        await Assert.That(check).Contains("return l + m;");
        await Assert.That(counter + check).DoesNotContain("asIntN");
        await Assert.That(counter + check).DoesNotContain("checkedInt64");
    }

    /// <summary>
    /// Dart's <c>int</c> already wraps, so a reduction prints as its operand
    /// and a checked context is a documented gap (DECIDED-19); an increment
    /// keeps the stored-value form the core builds.
    /// </summary>
    [Test]
    public async Task Dart_PrintsTheReductionAsItsOperand()
    {
        var (files, diagnostics) = DartTranspileHelper.Transpile(
            UncheckedSource + IncrementsSource.Replace("using System;", "")
        );

        var wrap = files["wrap.dart"];
        var counter = files["counter.dart"];

        await Assert.That(wrap).Contains("int add(int l, int m) => l + m;");
        await Assert.That(wrap).Contains("int uNot(int u) => ~u;");
        await Assert.That(counter).Contains("l = l + 1;");
        await Assert.That(counter).Contains("this.total = this.total + i;");
        await Assert.That(UnsupportedFeatures(diagnostics)).IsEmpty();
    }

    private const string ExtensionSource = """
        [Transpile]
        public class Holder
        {
            public long Raw;
        }

        [Transpile]
        public static class HolderExtensions
        {
            extension(Holder h)
            {
                public long Bonus
                {
                    get => h.Raw;
                    set => h.Raw = value;
                }

            }

            extension(Holder)
            {
                public static long Shared
                {
                    get => 0;
                    set { }
                }
            }
        }

        [Transpile]
        public class Uses
        {
            public void Bump(Holder h)
            {
                h.Bonus++;
                h.Bonus += 5;
                Holder.Shared--;
                Holder.Shared *= 3;
                checked
                {
                    h.Bonus *= 2;
                }
            }
        }
        """;

    /// <summary>
    /// An extension property increment and compound assignment, instance or
    /// static, store the reduced value through the setter helper (FR-024),
    /// stepping by <c>1n</c>, so no <c>bigint</c> / <c>number</c> mix reaches
    /// the output. Extension indexers take the same lowering but do not
    /// compile on the current Roslyn (see <c>ExtensionIndexerTests</c>).
    /// </summary>
    [Test]
    public async Task BigIntMode_ExtensionMembers_StoreTheReducedValue()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            ExtensionSource,
            Int64Representation.BigInt
        );

        await Assert
            .That(files["uses.ts"])
            .IsEqualTo(TranspileHelper.ReadExpected("int64-extension-members.ts"));

        await Assert.That(UnsupportedFeatures(diagnostics)).IsEmpty();
    }

    private static List<MetanoDiagnostic> UnsupportedFeatures(
        IEnumerable<MetanoDiagnostic> diagnostics
    ) => diagnostics.Where(d => d.Code == DiagnosticCodes.UnsupportedFeature).ToList();
}
