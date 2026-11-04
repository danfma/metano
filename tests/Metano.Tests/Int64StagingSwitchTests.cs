using Metano.Compiler.TypeScript;

namespace Metano.Tests;

/// <summary>
/// The <see cref="Int64Representation"/> staging switch of spec 012
/// (DECIDED-22, FR-071) and the IR's unsigned 64-bit primitive (FR-001): the
/// default <see cref="Int64Representation.Number"/> mode keeps the shipped
/// output byte for byte, <c>ulong</c> included, although the IR no longer folds
/// <c>ulong</c> into <c>long</c>.
/// </summary>
public class Int64StagingSwitchTests
{
    /// <summary>
    /// Every 64-bit position this slice's IR change touches: types, literals
    /// beyond <c>long.MaxValue</c>, defaults, type tests, overload dispatch and
    /// arithmetic. The golden is the output of the compiler before slice 012a.
    /// </summary>
    private const string SixtyFourBitSource = """
        using System.Collections.Generic;

        [Transpile]
        public class Ops
        {
            public const ulong Max = ulong.MaxValue;
            public const ulong Small = 42UL;
            public const long LMax = long.MaxValue;
            public ulong Field;
            public ulong Initialized = 7;
            public long LField;

            public ulong Add(ulong a, ulong b) => a + b;
            public ulong Shift(ulong a, int n) => a >> n;
            public ulong Div(ulong a, ulong b) => a / b;
            public ulong Def(ulong x = 5UL, ulong y = 18446744073709551615UL) => x + y;
            public ulong Lit() => 18446744073709551615UL;
            public ulong Zero() => default;
            public bool Is(object o) => o is ulong;
            public string Over(long x) => "long";
            public string Over(ulong x) => "ulong";
            public string Over(int x) => "int";
            public ulong Inc(ulong a) { a++; a += 2; return a; }
            public ulong? Lifted(ulong? a) => a + 1;
            public ulong Mixed(ulong a, uint b) => a * b;
            public double ToD(ulong a) => (double)a;
            public long ToL(ulong a) => (long)a;
            public ulong FromL(long a) => (ulong)a;
            public int Narrow(ulong a) => (int)a;
            public ulong Sw(ulong a) => a switch { 0 => 1, 1UL => 2, _ => a };
            public bool Pat(ulong a) => a is > 5 and < 10;
            public string Fmt(ulong a) => $"{a} {a.ToString()}";
            public int Cmp(ulong a, ulong b) => a.CompareTo(b);
            public ulong[] Arr() => new ulong[] { 1, 2, 3 };
            public ulong Sum(List<ulong> xs) { ulong s = 0; foreach (var x in xs) s += x; return s; }
            public ulong Lambda() { Func<ulong, ulong> f = x => x * 2; return f(3); }
        }

        [Transpile, GenerateGuard]
        public record Holder(long L, ulong U, ulong? NU, long? NL, ulong[] Us);

        [Transpile, Branded]
        public readonly record struct UId(ulong Value);
        """;

    [Test]
    public async Task NumberMode_ReproducesThePre012aOutput()
    {
        var (files, _) = TranspileHelper.TranspileWithDiagnostics(
            SixtyFourBitSource,
            Int64Representation.Number
        );

        await Assert
            .That(files["ops.ts"])
            .IsEqualTo(TranspileHelper.ReadExpected("int64-number-mode.ts"));
    }

    [Test]
    public async Task DefaultMode_IsNumber()
    {
        var defaultFiles = TranspileHelper.Transpile(SixtyFourBitSource);

        var (numberFiles, _) = TranspileHelper.TranspileWithDiagnostics(
            SixtyFourBitSource,
            Int64Representation.Number
        );

        await Assert.That(defaultFiles).IsEquivalentTo(numberFiles);
    }

    [Test]
    public async Task UInt64_StillEmitsNumber_InNumberMode()
    {
        var (files, _) = TranspileHelper.TranspileWithDiagnostics(
            SixtyFourBitSource,
            Int64Representation.Number
        );

        var holder = files["holder.ts"];

        await Assert.That(holder).Contains("readonly u: number,");
        await Assert.That(holder).Contains("readonly nU: number | null,");
        await Assert.That(holder).Contains("readonly us: number[]");
        await Assert.That(holder).Contains("typeof v.u === \"number\"");
        await Assert.That(files["u-id.ts"]).Contains("number & { readonly __brand: \"UId\" }");
    }

    [Test]
    public async Task TypeScriptTarget_DefaultsToNumber()
    {
        var target = new TypeScriptTarget();

        await Assert.That(target.Int64Representation).IsEqualTo(Int64Representation.Number);
    }

    [Test]
    public async Task TypeScriptTarget_PinsTheRepresentationInTheCacheFingerprint()
    {
        var numberTarget = new TypeScriptTarget();

        var bigIntTarget = new TypeScriptTarget
        {
            Int64Representation = Int64Representation.BigInt,
        };

        await Assert.That(numberTarget.ConfigurationFingerprint).EndsWith(";int64=Number");
        await Assert.That(bigIntTarget.ConfigurationFingerprint).EndsWith(";int64=BigInt");
    }
}
