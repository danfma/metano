using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// The null guards of string concatenation, interpolation, list patterns and
/// exception messages follow the declared type of the operand, not Roslyn's
/// flow state (#649): that state is optimistic after <c>x!</c>, after a null
/// check a call then undoes, and for a local a lambda clears, and it is absent
/// when nullable is disabled.
/// </summary>
public class NullGuardDeclaredTypeTests
{
    private const string Holder = """
        public sealed class Holder
        {
            public string? S;

            public int[]? A;

            public void Clear() { S = null; A = null; }
        }
        """;

    [Test]
    [Arguments("\"a\" + s! + \"b\"", "\"a\" + (s ?? \"\") + \"b\"")]
    [Arguments("$\"[{s!}]\"", "`[${s ?? \"\"}]`")]
    [Arguments("\"a\" + s", "\"a\" + (s ?? \"\")")]
    public async Task NullableDeclaredOperand_KeepsTheGuardAfterTheNullForgivingOperator(
        string expression,
        string lowered
    )
    {
        var files = TranspileHelper.Transpile(
            $$"""
            #nullable enable
            [Transpile]
            public static class Probe
            {
                public static string Run(string? s) => {{expression}};
            }
            """
        );

        await Assert.That(files["probe.ts"]).Contains(lowered);
    }

    [Test]
    public async Task CompoundConcatenation_KeepsTheGuardOfAForgivenOperand()
    {
        var files = TranspileHelper.Transpile(
            """
            #nullable enable
            [Transpile]
            public static class Probe
            {
                public static string Run(string? s)
                {
                    var r = "a";
                    r += s!;

                    return r;
                }
            }
            """
        );

        await Assert.That(files["probe.ts"]).Contains("r = (r ?? \"\") + (s ?? \"\");");
    }

    [Test]
    public async Task FieldClearedByACallAfterANullCheck_KeepsTheGuard()
    {
        var files = TranspileHelper.Transpile(
            $$"""
            #nullable enable
            {{Holder}}

            [Transpile]
            public static class Probe
            {
                public static string Run(Holder h)
                {
                    if (h.S == null)
                        return "";

                    h.Clear();

                    return "a" + h.S + "b";
                }
            }
            """
        );

        await Assert.That(files["probe.ts"]).Contains("\"a\" + (h.s ?? \"\") + \"b\"");
    }

    [Test]
    public async Task LocalClearedByALambda_KeepsTheGuard()
    {
        var files = TranspileHelper.Transpile(
            """
            #nullable enable
            using System;

            [Transpile]
            public static class Probe
            {
                public static string Run()
                {
                    string? local = "z";
                    Action clear = () => local = null;

                    if (local == null)
                        return "";

                    clear();

                    return "a" + local + "b";
                }
            }
            """
        );

        await Assert.That(files["probe.ts"]).Contains("\"a\" + (local ?? \"\") + \"b\"");
    }

    [Test]
    [Arguments("\"a\" + s + \"b\"", "\"a\" + (s ?? \"\") + \"b\"")]
    [Arguments("$\"[{s}]\"", "`[${s ?? \"\"}]`")]
    public async Task NullableDisabledOperand_KeepsTheGuard(string expression, string lowered)
    {
        var files = TranspileHelper.Transpile(
            $$"""
            #nullable disable
            [Transpile]
            public static class Probe
            {
                public static string Run(string s) => {{expression}};
            }
            """
        );

        await Assert.That(files["probe.ts"]).Contains(lowered);
    }

    /// <summary>
    /// What cannot be null by construction stays unguarded, so the common
    /// shapes of a nullable-enabled project do not grow a coalesce.
    /// </summary>
    [Test]
    public async Task OperandThatCannotBeNull_NeedsNoGuard()
    {
        var files = TranspileHelper.Transpile(
            """
            #nullable enable
            [Transpile]
            public static class Probe
            {
                private const string Tag = "t";

                public static string Run(string known, int n, string other) =>
                    "a" + known + n + Tag + 'c' + (known + other) + $"{n}" + n.ToString();
            }
            """
        );

        var probe = files["probe.ts"];

        await Assert.That(probe).DoesNotContain("??");
    }

    [Test]
    public async Task ListPatternOverANullableDeclaredSubject_KeepsTheNullTest()
    {
        var files = TranspileHelper.Transpile(
            $$"""
            #nullable enable
            {{Holder}}

            [Transpile]
            public static class Probe
            {
                public static bool Forgiven(int[]? a) => a! is [1, ..];

                public static bool Cleared(Holder h)
                {
                    if (h.A == null)
                        return false;

                    h.Clear();

                    return h.A is [1, ..];
                }

                public static bool NonNull(int[] a) => a is [1, ..];
            }
            """
        );

        var probe = files["probe.ts"];

        await Assert.That(probe).Contains("return a! != null && a!.length >= 1 && a![0] === 1;");
        await Assert.That(probe).Contains("return h.a != null && h.a.length >= 1 && h.a[0] === 1;");
        await Assert.That(probe).Contains("return a.length >= 1 && a[0] === 1;");
    }

    [Test]
    public async Task ExceptionMessageDeclaredNullable_KeepsTheDefaultMessageFallback()
    {
        var files = TranspileHelper.Transpile(
            """
            #nullable enable
            using System;

            [Transpile]
            public sealed class Failure : Exception
            {
                public Failure(string? message) : base(message!) { }
            }

            [Transpile]
            public sealed class Plain : Exception
            {
                public Plain(string message) : base(message) { }
            }
            """
        );

        await Assert
            .That(files["failure.ts"])
            .Contains("super(message! ?? \"Exception of type 'Failure' was thrown.\");");

        await Assert.That(files["plain.ts"]).Contains("super(message);");
    }

    [Test]
    public async Task ExceptionMessageInNullableDisabledCode_KeepsTheDefaultMessageFallback()
    {
        var files = TranspileHelper.Transpile(
            """
            #nullable disable
            using System;

            [Transpile]
            public sealed class Failure : Exception
            {
                public Failure(string message) : base(message) { }
            }
            """
        );

        await Assert
            .That(files["failure.ts"])
            .Contains("super(message ?? \"Exception of type 'Failure' was thrown.\");");
    }
}
