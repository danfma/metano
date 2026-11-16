namespace Metano.Tests;

/// <summary>
/// A class that declares <c>Equals(T)</c> beside the <c>Equals(object)</c>
/// override emits one dispatched <c>equals</c>, not two methods of the same
/// name (#677); the same holds for any override of a BCL virtual that an
/// overload accompanies.
/// </summary>
public class EqualsOverloadPairTests
{
    private const string Counter = """
        [Transpile]
        public class Counter
        {
            public int V;

            public bool Equals(Counter? other) => other is not null && other.V == V;

            public override bool Equals(object? obj) => Equals(obj as Counter);

            public override int GetHashCode() => V;
        }
        """;

    [Test]
    public async Task EqualsOverloadAndOverride_FormOneDispatchedGroup()
    {
        var output = TranspileHelper.Transpile(Counter)["counter.ts"];

        await Assert.That(CountOccurrences(output, "  equals(")).IsEqualTo(3);
        await Assert.That(output).Contains("equals(...args: unknown[]): boolean {");
        await Assert.That(output).Contains("equalsOther(other: Counter | null): boolean {");
        await Assert.That(output).Contains("equalsObj(obj: Object | null): boolean {");
    }

    [Test]
    public async Task DirectCalls_BindToTheFastPathOfTheOverloadTheyResolve()
    {
        var files = TranspileHelper.Transpile(
            $$"""
            {{Counter}}

            [Transpile]
            public static class Use
            {
                public static bool Typed(Counter a, Counter b) => a.Equals(b);

                public static bool Boxed(Counter a, object b) => a.Equals(b);
            }
            """
        );

        await Assert.That(files["use.ts"]).Contains("return a.equalsOther(b);");
        await Assert.That(files["use.ts"]).Contains("return a.equalsObj(b);");
    }

    [Test]
    public async Task DerivedOverride_ReusesTheFastPathOfTheBaseOverride()
    {
        var files = TranspileHelper.Transpile(
            $$"""
            {{Counter}}

            [Transpile]
            public sealed class Doubled : Counter
            {
                public override bool Equals(object? obj) => obj is Doubled && base.Equals(obj);
            }
            """
        );

        await Assert.That(files["doubled.ts"]).Contains("override equalsObj(obj: Object | null)");
        await Assert.That(files["doubled.ts"]).Contains("super.equalsObj(obj)");
    }

    [Test]
    public async Task ToStringOverrideBesideAnOverload_FormsOneDispatchedGroup()
    {
        var output = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Money
            {
                public int V;

                public override string ToString() => V.ToString();

                public string ToString(string prefix) => prefix + V;
            }
            """
        )["money.ts"];

        await Assert.That(output).Contains("toString(...args: unknown[]): string {");
        await Assert
            .That(CountOccurrences(output, "  toString(prefix: string): string {"))
            .IsEqualTo(0);
    }

    private static int CountOccurrences(string text, string value) => text.Split(value).Length - 1;
}
