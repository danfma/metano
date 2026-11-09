using Metano.Compiler.TypeScript;

namespace Metano.Tests;

/// <summary>
/// The one 64-bit mapping of the TypeScript target after the flip of spec 012
/// (FR-071): <c>long</c>, <c>ulong</c>, <c>nint</c> and <c>nuint</c> are
/// native <c>bigint</c> values, with no option to select anything else, and
/// the IR keeps <c>ulong</c> apart from <c>long</c> (FR-001).
/// </summary>
public class SixtyFourBitMappingTests
{
    /// <summary>
    /// Every 64-bit position the IR's unsigned primitive touches: types,
    /// literals beyond <c>long.MaxValue</c>, defaults, type tests, overload
    /// dispatch and arithmetic.
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
            public ulong Defaults(ulong a = 10, ulong b = ulong.MaxValue, long c = -1) => a + b + (ulong)c;
            public string CallDefaults() => Defaults(b: 3).ToString() + Defaults(c: 2) + Defaults();
        }

        [Transpile, GenerateGuard]
        public record Holder(long L, ulong U, ulong? NU, long? NL, ulong[] Us);

        [Transpile, Branded]
        public readonly record struct UId(ulong Value);
        """;

    /// <summary>
    /// A named-argument call that skips a <c>ulong</c> default keeps the
    /// shipped opaque default.
    /// </summary>
    [Test]
    public async Task Dart_SkippedULongDefault_KeepsTheShippedOpaqueDefault()
    {
        var (files, _) = DartTranspileHelper.Transpile(SixtyFourBitSource);

        await Assert
            .That(files["ops.dart"])
            .Contains(
                "this.defaults(/* default */ null, b: 3, -1).toString()"
                    + " + this.defaults(/* default */ null, /* default */ null, c: 2)"
            );
    }

    [Test]
    public async Task UInt64_EmitsBigInt()
    {
        var (files, _) = TranspileHelper.TranspileWithDiagnostics(SixtyFourBitSource);

        var holder = files["holder.ts"];

        await Assert.That(holder).Contains("readonly u: bigint,");
        await Assert.That(holder).Contains("readonly nU: bigint | null,");
        await Assert.That(holder).Contains("readonly us: bigint[]");
        await Assert.That(holder).Contains("typeof v.u === \"bigint\"");
        await Assert.That(files["u-id.ts"]).Contains("bigint & { readonly __brand: \"UId\" }");
    }

    /// <summary>
    /// The cache fingerprint names the 64-bit mapping, so a group cache
    /// written by a compiler that mapped <c>long</c> to <c>number</c> is
    /// never reused.
    /// </summary>
    [Test]
    public async Task TypeScriptTarget_PinsTheBigIntMappingInTheCacheFingerprint()
    {
        var target = new TypeScriptTarget();

        await Assert.That(target.ConfigurationFingerprint).EndsWith(";int64=bigint");
    }

    /// <summary><c>nint</c> / <c>nuint</c> are 64-bit (DECIDED-2), so they map to <c>bigint</c> like <c>long</c>.</summary>
    [Test]
    public async Task NativeIntegers_MapToBigInt()
    {
        const string source = """
            [Transpile]
            public class Native
            {
                public nint Widen(nint n) => n;
                public nuint WidenU(nuint n) => n;
            }
            """;

        var (files, _) = TranspileHelper.TranspileWithDiagnostics(source);

        await Assert.That(files["native.ts"]).Contains("widen(n: bigint): bigint {");
        await Assert.That(files["native.ts"]).Contains("widenU(n: bigint): bigint {");
    }
}
