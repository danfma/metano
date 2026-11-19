namespace Metano.Tests;

/// <summary>Spec 011 FR-044 (#708): enum operators use spec 012's integer reductions.</summary>
public class NarrowEnumArithmeticTests
{
    private const string Source = """
        #nullable enable
        [Transpile, EmitInFile("numeric-enum-arithmetic")]
        public enum B : byte { One = 1 }
        [Transpile, EmitInFile("numeric-enum-arithmetic")]
        public static class EnumArithmetic
        {
            public static B Add(B b, byte n) => unchecked(b + n);
            public static B Subtract(B b, byte n) => unchecked(b - n);
            public static byte Difference(B a, B b) => unchecked(a - b);
            public static B CheckedAdd(B b, byte n) => checked(b + n);
            public static B CheckedSubtract(B b, byte n) => checked(b - n);
            public static B Postfix(B b) => b++;
            public static B Prefix(B b) => ++b;
            public static B Compound(B b, byte n) { b += n; return b; }
            public static B CheckedCompound(B b, byte n) { checked { b += n; } return b; }
            public static B CheckedIncrement(B b) { checked { b++; } return b; }
            public static B InnerUnchecked(B b) => checked(unchecked(b + 1));
            public static B? Lifted(B? b, byte? n) => b + n;
            public static B? LiftedCompound(B? b, byte? n) { b += n; return b; }
            public static B? LiftedPostfix(B? b) => b++;
            public static B? CheckedLifted(B? b, byte? n) => checked(b + n);
        }
        """;

    [Test]
    public async Task Operators_ReuseIntegerReductionsBeforeEnumAssertions()
    {
        var files = TranspileHelper.Transpile(Source);

        await TranspileHelper.AssertGolden(
            "numeric-enum-arithmetic.ts",
            files["numeric-enum-arithmetic.ts"]
        );
    }

    [Test]
    [Arguments("byte", "((b + n) & 0xFF)", "checkedNarrowNumber(b + n, 8, false)")]
    [Arguments("sbyte", "((b + n) << 24) >> 24", "checkedNarrowNumber(b + n, 8, true)")]
    [Arguments("short", "((b + n) << 16) >> 16", "checkedNarrowNumber(b + n, 16, true)")]
    [Arguments("ushort", "((b + n) & 0xFFFF)", "checkedNarrowNumber(b + n, 16, false)")]
    [Arguments("int", "((b + n) | 0)", "checkedInt32(b + n)")]
    [Arguments("uint", "(b + n) >>> 0", "checkedUInt32(b + n)")]
    public async Task EveryUnderlyingWidth_UsesTheExistingWrapAndCheck(
        string kind,
        string wrap,
        string check
    )
    {
        var source = $$"""
            [Transpile] public enum E : {{kind}} { One = 1 }
            [Transpile] public static class Arithmetic
            {
                public static E Add(E b, {{kind}} n) => unchecked(b + n);
                public static E CheckedAdd(E b, {{kind}} n) => checked(b + n);
            }
            """;

        var output = TranspileHelper.Transpile(source)["arithmetic.ts"];

        await Assert.That(output).Contains($"return {wrap} as E;");
        await Assert.That(output).Contains($"return {check} as E;");
    }

    [Test]
    public async Task ProjectCheckedArithmetic_RespectsAnInnerUnchecked()
    {
        var (files, _) = TranspileHelper.TranspileWithOverflowChecks(Source);
        var output = files["numeric-enum-arithmetic.ts"];

        await Assert.That(output).Contains("return ((b + 1) & 0xFF) as B;");
        await Assert.That(output).Contains("b = checkedNarrowNumber(b + 1, 8, false) as B;");
    }
}
