using Metano.Compiler.TypeScript;

namespace Metano.Tests;

/// <summary>
/// The 64-bit dispatch and guard surface of spec 012 slice 012f (FR-048,
/// FR-049): the
/// dispatcher checks a 64-bit argument with the <c>bigint</c> range checks and
/// orders the sibling sets that differ only among <c>long</c>,
/// <c>ulong</c> and <c>BigInteger</c> long, ulong, BigInteger (OQ-6) —
/// including the multi-parameter, operator and constructor shapes — while
/// overloads that name a non-64-bit parameter where a neighbor names a 64-bit
/// one keep the declaration order. The synthesized guards and the type
/// patterns (<c>o is long</c>, a <c>switch</c> over the 64-bit kinds) test a
/// 64-bit value with the runtime range checks.
/// </summary>
public class Int64DispatchGuardTests
{
    /// <summary>
    /// Same-arity overloads that differ only among the 64-bit integer kinds,
    /// with an <c>int</c> overload beside them: the dispatcher's guards
    /// overlap only among <c>long</c>, <c>ulong</c> and <c>BigInteger</c>, so
    /// the golden pins both the OQ-6 order and the distinct fast-path
    /// suffixes of spec 013 (the exact CLR spellings).
    /// </summary>
    private const string DispatchSource = """
        using System.Numerics;

        [Transpile]
        public class Gates
        {
            public string Pick(int x) => "int";
            public string Pick(long x) => "long";
            public string Pick(ulong x) => "ulong";
            public string Pick(BigInteger x) => "bigInteger";
        }
        """;

    /// <summary>
    /// The sibling-set shapes of the OQ-6 reorder: multi-parameter overloads
    /// (a <c>string</c> first parameter shared), nullable 64-bit siblings, a
    /// branded value where a neighbor names a 64-bit integer (which must keep
    /// the declaration order), an operator group (the static and <c>$</c>
    /// instance dispatchers share the order) and a constructor group.
    /// </summary>
    private const string SiblingSource = """
        using System.Numerics;

        [Transpile]
        public class Shapes
        {
            public string F(string label, ulong u) => "ulong";
            public string F(string label, long l) => "long";
            public string H(BigInteger? b) => "bigInteger?";
            public string H(long? l) => "long?";
        }

        [Transpile, Branded]
        public readonly record struct UId(long Value);

        [Transpile]
        public class Mixed
        {
            public string H(long l) => "long";
            public string H(UId u) => "uid";
        }

        [Transpile]
        public class Money
        {
            public static Money operator +(Money m, ulong u) => m;
            public static Money operator +(Money m, long l) => m;
        }

        [Transpile]
        public class Stamp
        {
            public Stamp(ulong ticks) { }
            public Stamp(long ticks) { }
        }
        """;

    /// <summary>
    /// The type patterns over a boxed value: <c>o is long</c> /
    /// <c>o is ulong</c> read the runtime range checks (DECIDED-14), and a
    /// <c>switch</c> over <c>long</c> / <c>ulong</c> / <c>BigInteger</c> emits
    /// three distinct tests instead of three identical <c>typeof</c> arms.
    /// </summary>
    private const string PatternSource = """
        using System.Numerics;

        [Transpile]
        public class Names
        {
            public string Kind(object o) => o switch
            {
                long l => "long",
                ulong u => "ulong",
                BigInteger b => "bigInteger",
                _ => "other",
            };

            public bool IsLong(object o) => o is long;
            public bool IsULong(object o) => o is ulong;
        }
        """;

    private const string GuardSource = """
        [Transpile, GenerateGuard]
        public record Snowflake(long Id, ulong Mask, long? Parent);
        """;

    [Test]
    public async Task Dispatch_Golden()
    {
        var (files, _) = TranspileHelper.TranspileWithDiagnostics(DispatchSource);

        await Assert
            .That(files["gates.ts"])
            .IsEqualTo(TranspileHelper.ReadExpected("int64-guards-dispatch.ts"));
    }

    /// <summary>
    /// A <c>bigint</c> argument reaches the first guard of the OQ-6 order: a
    /// value that fits <c>long</c> picks the <c>long</c> overload even when it
    /// also fits <c>ulong</c>, and one beyond it walks down the chain.
    /// </summary>
    [Test]
    public async Task Dispatch_TriesLongThenUlongThenBigInteger()
    {
        var (files, _) = TranspileHelper.TranspileWithDiagnostics(DispatchSource);

        var output = files["gates.ts"];

        await Assert.That(output).Contains("isInt64(args[0])");
        await Assert.That(output).Contains("isUInt64(args[0])");
        await Assert.That(output).Contains("isBigInt(args[0])");

        var intBranch = output.IndexOf("isInt32(args[0])", StringComparison.Ordinal);
        var longBranch = output.IndexOf("isInt64(args[0])", StringComparison.Ordinal);
        var ulongBranch = output.IndexOf("isUInt64(args[0])", StringComparison.Ordinal);
        var bigIntegerBranch = output.IndexOf("isBigInt(args[0])", StringComparison.Ordinal);

        await Assert.That(longBranch).IsGreaterThan(intBranch);
        await Assert.That(ulongBranch).IsGreaterThan(longBranch);
        await Assert.That(bigIntegerBranch).IsGreaterThan(ulongBranch);
    }

    /// <summary>
    /// Multi-parameter siblings reorder on the 64-bit position only
    /// (<c>F(string, long)</c> before <c>F(string, ulong)</c>, declared the
    /// other way around), and nullable siblings do the same
    /// (<c>H(long?)</c> before <c>H(BigInteger?)</c>, declared the other way
    /// around).
    /// </summary>
    [Test]
    public async Task Siblings_ReorderOnTheSixtyFourBitPositions()
    {
        var (files, _) = TranspileHelper.TranspileWithDiagnostics(SiblingSource);

        var output = files["shapes.ts"];

        var fLong = output.IndexOf("isInt64(args[1])", StringComparison.Ordinal);
        var fUlong = output.IndexOf("isUInt64(args[1])", StringComparison.Ordinal);

        await Assert.That(fLong).IsGreaterThan(-1);
        await Assert.That(fUlong).IsGreaterThan(fLong);

        var hLong = output.IndexOf("isInt64(args[0])", StringComparison.Ordinal);
        var hBigInteger = output.IndexOf("isBigInt(args[0])", StringComparison.Ordinal);

        await Assert.That(hLong).IsGreaterThan(-1);
        await Assert.That(hBigInteger).IsGreaterThan(hLong);
    }

    /// <summary>
    /// An overload that names a non-64-bit parameter where the neighbor names
    /// a 64-bit one is not a sibling: <c>Mixed.H(long)</c> is declared before
    /// <c>Mixed.H(UId)</c> and keeps its slot, so the branded arm (a
    /// <c>typeof</c> test) never captures the plain <c>long</c> call.
    /// </summary>
    [Test]
    public async Task Siblings_BrandedNeighborKeepsDeclarationOrder()
    {
        var (files, _) = TranspileHelper.TranspileWithDiagnostics(SiblingSource);

        var output = files["mixed.ts"];

        var longBranch = output.IndexOf("isInt64(args[0])", StringComparison.Ordinal);
        var brandedBranch = output.IndexOf(
            "typeof args[0] === \"bigint\"",
            StringComparison.Ordinal
        );

        await Assert.That(longBranch).IsGreaterThan(-1);
        await Assert.That(brandedBranch).IsGreaterThan(longBranch);
    }

    /// <summary>
    /// The operator group's two dispatchers (the static <c>__add</c> and the
    /// instance <c>$add</c>) share the sorted order: the <c>long</c> fast path
    /// is tried before the <c>ulong</c> one although it is declared after.
    /// </summary>
    [Test]
    public async Task Operators_TryLongBeforeUlong()
    {
        var (files, _) = TranspileHelper.TranspileWithDiagnostics(SiblingSource);

        var output = files["money.ts"];

        await Assert.That(output).Contains("__addMoneyInt64(");
        await Assert.That(output).Contains("__addMoneyUInt64(");

        var longBranch = output.IndexOf("isInt64(args", StringComparison.Ordinal);
        var ulongBranch = output.IndexOf("isUInt64(args", StringComparison.Ordinal);

        await Assert.That(longBranch).IsGreaterThan(-1);
        await Assert.That(ulongBranch).IsGreaterThan(longBranch);
    }

    /// <summary>
    /// The constructor dispatcher orders its branches the same way:
    /// <c>new Stamp(long)</c> is declared after <c>new Stamp(ulong)</c> but
    /// tried first.
    /// </summary>
    [Test]
    public async Task Constructors_TryLongBeforeUlong()
    {
        var (files, _) = TranspileHelper.TranspileWithDiagnostics(SiblingSource);

        var output = files["stamp.ts"];

        var longBranch = output.IndexOf("isInt64(args", StringComparison.Ordinal);
        var ulongBranch = output.IndexOf("isUInt64(args", StringComparison.Ordinal);

        await Assert.That(longBranch).IsGreaterThan(-1);
        await Assert.That(ulongBranch).IsGreaterThan(longBranch);
    }

    /// <summary>
    /// The type patterns read the range checks:
    /// the import ships the three 64-bit helpers, <c>o is long</c> and
    /// <c>o is ulong</c> call them, and the switch arms over
    /// <c>long</c> / <c>ulong</c> / <c>BigInteger</c> are three distinct
    /// tests, not three identical <c>typeof</c> arms.
    /// </summary>
    [Test]
    public async Task Patterns_RangeCheckTheBoxedValue()
    {
        var (files, _) = TranspileHelper.TranspileWithDiagnostics(PatternSource);

        var output = files["names.ts"];

        await Assert.That(output).Contains("import { isInt64, isUInt64 } from \"metano-runtime\";");
        await Assert.That(output).Contains("isInt64(");
        await Assert.That(output).Contains("isUInt64(");

        var longTest = output.IndexOf("isInt64(", StringComparison.Ordinal);
        var ulongTest = output.IndexOf("isUInt64(", StringComparison.Ordinal);
        var bigIntegerTest = output.IndexOf("=== \"bigint\"", StringComparison.Ordinal);

        await Assert.That(longTest).IsGreaterThan(-1);
        await Assert.That(ulongTest).IsGreaterThan(longTest);
        await Assert.That(bigIntegerTest).IsGreaterThan(ulongTest);
    }

    [Test]
    public async Task Guard_TestsBigintMembers()
    {
        var (files, _) = TranspileHelper.TranspileWithDiagnostics(GuardSource);

        var output = files["snowflake.ts"];

        await Assert.That(output).Contains("typeof v.id === \"bigint\"");
        await Assert.That(output).Contains("typeof v.mask === \"bigint\"");

        // A nullable member admits null before the bigint test.
        await Assert.That(output).Contains("v.parent == null || typeof v.parent === \"bigint\"");
    }
}
