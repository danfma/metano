using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// The <c>char</c>, <c>int</c>-wrap and user-conversion gaps left by wave 2
/// (#695): a lifted <c>char?</c> store, the narrow BCL limits, the
/// declaration's array shorthand, the <c>string</c> constructors, a string
/// walked by code unit, the throwing <c>int</c> remainder and
/// <c>Math.Abs</c>, the <c>decimal</c> / <c>float</c> conversions of a
/// <c>double</c>, and the numeric step a cast applies before a user
/// conversion, and a nested enum's <c>default</c>. The values run against .NET in <c>tests/Conformance</c>
/// (<c>CharIntGapProbes</c>).
/// </summary>
public class CharIntConversionGapTests
{
    private const string Source = """
        using System;
        using System.Linq;

        [Transpile]
        public readonly struct Wrap
        {
            public readonly uint V;

            public Wrap(uint v) => V = v;

            public static explicit operator Wrap(uint v) => new Wrap(v);
        }

        [Transpile]
        public readonly struct Code
        {
            public readonly int V;

            public Code(int v) => V = v;

            public static implicit operator Code(int v) => new Code(v);
        }

        [Transpile]
        public class Outer
        {
            public enum Inner { A, B }
        }

        [Transpile]
        public class Counter
        {
            public int Count;

            public void Bump() => Count++;
        }

        [Transpile]
        public static class Gaps
        {
            public static char? NullableIncrement(char? c) { c++; return c; }
            public static char? NullableCompound(char? c) { c += (char)2; return c; }
            public static char FromDecimal(decimal m) => (char)m;
            public static int CharLimit() { int max = char.MaxValue; return max; }
            public static int IntLimit() => int.MaxValue;
            public static int[] Shorthand(char c) { int[] a = { c, 'b' }; return a; }
            public static string Join(char[] cs) => new string(cs);
            public static string Repeat(char c, int n) => new string(c, n);
            public static int Walk(string s) { int n = 0; foreach (var ch in s) n += ch; return n; }
            public static int Abs(int x) => Math.Abs(x);
            public static int Remainder(int a, int b) => a % b;
            public static int ConstantRemainder(int a) => a % 10;
            public static decimal FromDouble(double d) => (decimal)d;
            public static decimal FromSingle(float f) => (decimal)f;
            public static float ToSingle(double d) => (float)d;
            public static Code? Lifted(char? c) => (Code?)c;
            public static Wrap Checked(long l) => checked((Wrap)l);
            public static Wrap Truncated(double d) => (Wrap)d;
            public static Outer.Inner NestedZero() => default;
        }
        """;

    [Test]
    [Arguments("c = c == null ? null : String.fromCharCode((c.charCodeAt(0) + 1) & 0xFFFF);")]
    [Arguments("c = c == null ? null : String.fromCharCode((c.charCodeAt(0) + 2) & 0xFFFF);")]
    [Arguments("return String.fromCharCode(charCodeFromDecimal(m));")]
    [Arguments("const max = 65535;")]
    [Arguments("return 2147483647;")]
    [Arguments("const a = [c.charCodeAt(0), 98];")]
    [Arguments("return cs.join(\"\");")]
    [Arguments("return c.repeat(n);")]
    [Arguments("for (const ch of s.split(\"\")) {")]
    [Arguments("return absInt32(x);")]
    [Arguments("return intRem(a, b);")]
    [Arguments("return a % 10;")]
    [Arguments("return new Decimal(decimalTextFromDouble(d, 15));")]
    [Arguments("return new Decimal(decimalTextFromDouble(f, 7));")]
    [Arguments("return Math.fround(d);")]
    [Arguments("Code.__implicitFromInt32(c == null ? null : c.charCodeAt(0))")]
    [Arguments("return Wrap.__fromUInt32(Number(checkedNarrow(l, 32, false)));")]
    [Arguments("return Wrap.__fromUInt32(uint32FromDouble(d));")]
    [Arguments("return Outer.Inner.A;")]
    public async Task Gap_LowersAsDotNetComputes(string expected)
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(Source);

        await Assert.That(files["gaps.ts"]).Contains(expected);
        await Assert.That(UnsupportedFeatures(diagnostics)).IsEmpty();
    }

    [Test]
    public async Task VoidExpressionBody_StoresWithoutYieldingThePreviousValue()
    {
        var files = TranspileHelper.Transpile(Source);

        await Assert.That(files["counter.ts"]).Contains("this.count = (this.count + 1) | 0;");
    }

    private static List<MetanoDiagnostic> UnsupportedFeatures(
        IEnumerable<MetanoDiagnostic> diagnostics
    ) => diagnostics.Where(d => d.Code == DiagnosticCodes.UnsupportedFeature).ToList();
}
