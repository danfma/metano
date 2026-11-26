namespace Metano.Tests;

/// <summary>
/// <c>new T(...)</c> of a <c>[PlainObject]</c> through a constructor that chains with
/// <c>: this(...)</c> builds the object literal from the arguments the chain ends with,
/// defaults included, each caller argument evaluated once and in order (#441).
/// </summary>
public class PlainObjectChainedConstructorTests
{
    [Test]
    public async Task ChainedConstructor_KeepsEveryArgument()
    {
        var output = TranspileHelper.Transpile(
            """
            [Transpile, PlainObject]
            public record PoDto(int A, string B, int C = 7)
            {
                public PoDto(int a) : this(a, "x") { }
                public PoDto() : this(2) { }
            }

            [Transpile]
            public static class UsePo
            {
                public static PoDto One() => new PoDto(1);
                public static PoDto Two() => new PoDto();
            }
            """
        )["use-po.ts"];

        await Assert.That(output).Contains("return { a: 1, b: \"x\", c: 7 };");
        await Assert.That(output).Contains("return { a: 2, b: \"x\", c: 7 };");
    }

    [Test]
    public async Task ChainedConstructor_BindsSideEffectingArgumentsOnce()
    {
        var output = TranspileHelper.Transpile(
            """
            [Transpile, PlainObject]
            public record Pair(int A, int B)
            {
                public Pair(int a) : this(a, a) { }
            }

            [Transpile]
            public static class UsePair
            {
                private static int _n;

                public static int Next() => ++_n;

                public static Pair Make() => new Pair(Next());
            }
            """
        )["use-pair.ts"];

        await Assert.That(output).Contains("UsePair.next()");
        await Assert.That(output).Contains("const receiver$temp$0 = UsePair.next();");
        await Assert.That(output).Contains("return { a: receiver$temp$0, b: receiver$temp$0 };");
    }
}
