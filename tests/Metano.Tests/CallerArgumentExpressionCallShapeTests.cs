namespace Metano.Tests;

/// <summary>
/// The call shapes whose <c>[CallerArgumentExpression]</c> texts the compiler
/// fills besides a plain call (FR-052, #676): extension syntax, constructor
/// initializers, an explicit delegate <c>Invoke</c> and an <c>[Emit]</c>
/// callee. The struct <c>default</c> parameter that shares the issue is pinned
/// here too.
/// </summary>
public class CallerArgumentExpressionCallShapeTests
{
    [Test]
    public async Task ExtensionSyntax_PassesTheReceiverText()
    {
        var output = TranspileUse(
            """
            [Transpile]
            public static class Ext
            {
                public static string Show(
                    this int value,
                    [CallerArgumentExpression(nameof(value))] string? text = null
                ) => text ?? "";

                public static string Pair(
                    this int value,
                    int other,
                    int step = 1,
                    [CallerArgumentExpression(nameof(other))] string? text = null
                ) => text ?? "";
            }

            [Transpile]
            public static class Use
            {
                public static string Plain(int count) => count.Show();

                public static string Static(int count) => Ext.Show(count);

                public static string Positional(int count) => count.Pair(count + 1);

                public static string Named(int count) => count.Pair(other: count * 2);
            }
            """
        );

        await Assert.That(output).Contains("return show(count, \"count\");");
        await Assert.That(output).Contains("return Ext.show(count, \"count\");");
        await Assert.That(output).Contains("pair(count, count + 1, undefined, \"count + 1\")");
        await Assert.That(output).Contains("pair(count, count * 2, 1, \"count * 2\")");
    }

    [Test]
    public async Task BaseInitializer_PassesTheCallerText()
    {
        var files = TranspileHelper.Transpile(
            """
            using System.Runtime.CompilerServices;

            [Transpile]
            public class Base
            {
                public string Text { get; }

                public Base(int value, [CallerArgumentExpression(nameof(value))] string? text = null) =>
                    Text = text ?? "";
            }

            [Transpile]
            public sealed class Derived : Base
            {
                public Derived(int count) : base(count + 1) { }
            }
            """
        );

        await Assert.That(files["derived.ts"]).Contains("super(count + 1, \"count + 1\");");
    }

    [Test]
    public async Task ThisInitializer_PassesTheCallerText()
    {
        var files = TranspileHelper.Transpile(
            """
            using System.Runtime.CompilerServices;

            [Transpile]
            public sealed class Holder
            {
                public string Text { get; }

                public Holder(int value, [CallerArgumentExpression(nameof(value))] string? text = null) =>
                    Text = text ?? "";

                public Holder() : this(40 + 2) { }
            }
            """
        );

        var output = files["holder.ts"];

        await Assert.That(output).Contains("const __chain0Arg0 = 40 + 2;");
        await Assert.That(output).Contains("const __chain0Arg1 = \"40 + 2\";");
        await Assert.That(output).Contains("const text = __chain0Arg1;");
    }

    [Test]
    [Arguments("probe.Invoke(count + 1)", "probe(count + 1, ")]
    [Arguments("probe?.Invoke(count + 1)", "probe?.(count + 1, ")]
    [Arguments("probe(count + 1)", "probe(count + 1, ")]
    public async Task DelegateCall_PassesTheCallerText(string call, string emitted)
    {
        var files = TranspileHelper.Transpile(
            $$"""
            using System.Runtime.CompilerServices;

            [Transpile]
            public delegate string Probe(
                int value,
                [CallerArgumentExpression(nameof(value))] string? text = null
            );

            [Transpile]
            public static class Use
            {
                public static string? Run(Probe probe, int count) => {{call}};
            }
            """
        );

        await Assert.That(files["use.ts"]).Contains($"{emitted}\"count + 1\")");
    }

    [Test]
    public async Task EmitCallee_PassesTheCallerTextToTheTemplate()
    {
        var output = TranspileUse(
            """
            [Transpile]
            public static class Native
            {
                [Emit("$0.describe($1)")]
                public static extern string Describe(
                    int value,
                    [CallerArgumentExpression(nameof(value))] string? text = null
                );
            }

            [Transpile]
            public static class Use
            {
                public static string Run(int count) => Native.Describe(count + 1);
            }
            """
        );

        await Assert.That(output).Contains("return (count + 1).describe(\"count + 1\");");
        await Assert.That(output).DoesNotContain("$1");
    }

    [Test]
    public async Task StructDefaultParameter_IsAConstructedValueInTheCalleeAndNamedCalls()
    {
        var files = TranspileHelper.Transpile(
            """
            [Transpile]
            public struct Spot
            {
                public int X;
                public int Y;
            }

            [Transpile]
            public static class Probe
            {
                public static int Sum(int a, Spot spot = default, int z = 0) => spot.X + a + z;

                public static int Positional() => Sum(1);

                public static int Named() => Sum(1, z: 3);
            }
            """
        );

        var output = files["probe.ts"];

        await Assert
            .That(output)
            .Contains("static sum(a: number, spot: Spot = new Spot(), z: number = 0)");

        await Assert.That(output).Contains("return Probe.sum(1);");
        await Assert.That(output).Contains("return Probe.sum(1, new Spot(), 3);");
    }

    /// <summary>
    /// A struct the target emits without a parameterless constructor (a
    /// declared or primary constructor) has no value that equals its C#
    /// <c>default</c>: the opaque default stays, as before.
    /// </summary>
    [Test]
    public async Task StructWithDeclaredConstructor_KeepsTheOpaqueDefault()
    {
        var files = TranspileHelper.Transpile(
            """
            [Transpile]
            public readonly record struct Pair(int A, int B);

            [Transpile]
            public static class Probe
            {
                public static int Sum(Pair pair = default) => pair.A + pair.B;
            }
            """
        );

        await Assert.That(files["probe.ts"]).Contains("pair: Pair = undefined");
    }

    private static string TranspileUse(string source) =>
        TranspileHelper.Transpile(
            $"""
            using System.Runtime.CompilerServices;
            using Metano.Annotations;

            {source}
            """
        )["use.ts"];
}
