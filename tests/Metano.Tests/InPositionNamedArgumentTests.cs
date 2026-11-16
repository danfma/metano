namespace Metano.Tests;

/// <summary>
/// A named argument written in its own position (C# 7.2) leaves the positional
/// arguments after it in place: each keeps its parameter, none becomes
/// <c>undefined</c> and no later default replaces an argument the call writes
/// (#666; named-argument evaluation order is pinned by
/// <c>NamedArgumentOrderTests</c>).
/// </summary>
public class InPositionNamedArgumentTests
{
    [Test]
    public async Task ConstructorCall_KeepsThePositionalArgumentsAfterTheNamedOne()
    {
        var files = TranspileHelper.Transpile(
            """
            [Transpile]
            public sealed record Item(string Name, bool Done, string Note, int? Rank = null, string? Tag = null);

            [Transpile]
            public static class Items
            {
                public static Item Make(string note) => new("a", Done: true, note, Rank: 1, "t");
            }
            """
        );

        await Assert
            .That(files["items.ts"])
            .Contains("return new Item(\"a\", true, note, 1, \"t\");");
    }

    [Test]
    public async Task MethodCall_KeepsThePositionalArgumentAfterTheNamedOne()
    {
        var files = TranspileHelper.Transpile(
            """
            [Transpile]
            public static class Items
            {
                public static int Sum(int a, int b, int c) => a + b + c;

                public static int CallSum() => Sum(1, b: 2, 3);
            }
            """
        );

        await Assert.That(files["items.ts"]).Contains("return Items.sum(1, 2, 3);");
    }

    [Test]
    public async Task InstanceExtensionAndDelegateCalls_KeepThePositionalArgumentsAfterTheNamedOne()
    {
        var files = TranspileHelper.Transpile(
            """
            [Transpile]
            public sealed class Calc
            {
                public int Opt(int a, int b, int c, int d = 20) => a + b + c + d;
            }

            [Transpile]
            public static class Ext
            {
                public static int Add3(this int a, int b, int c) => a + b + c;
            }

            [Transpile]
            public static class Use
            {
                public static int Instance(Calc calc, int k) => calc.Opt(1, b: 2, k);

                public static int Extension(int k) => k.Add3(b: 2, 5);

                public static int Delegate(Func<int, int, int, int> f, int k) => f.Invoke(1, arg2: 2, k);
            }
            """
        );

        var output = files["use.ts"];

        await Assert.That(output).Contains("return calc.opt(1, 2, k, 20);");
        await Assert.That(output).Contains("return add3(k, 2, 5);");
        await Assert.That(output).Contains("return f(1, 2, k);");
    }

    [Test]
    public async Task BaseInitializer_KeepsThePositionalArgumentAfterTheNamedOne()
    {
        var files = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Parent
            {
                public Parent(int a, int b, int c) { }
            }

            [Transpile]
            public sealed class Child : Parent
            {
                public Child(int x) : base(1, b: 2, x) { }
            }
            """
        );

        await Assert.That(files["child.ts"]).Contains("super(1, 2, x);");
    }
}
