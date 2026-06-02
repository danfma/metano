namespace Metano.Tests;

/// <summary>
/// Tests for User Story 3 (tuple deconstruction) of feature
/// 003-js-interop-primitives. A C# deconstructing declaration
/// <c>var (a, b) = expr;</c> lowers to a TS array-destructuring binding
/// <c>const [a, b] = expr;</c>. Discards (<c>_</c>) become empty holes
/// (<c>const [, b] = expr;</c>); later references resolve to the bound names
/// (not index access); a mutable bound local downgrades the binding to
/// <c>let</c>. The tuple value is produced by a <c>[JsTuple, Import]</c> factory.
/// Covers contracts D1–D4 in
/// <c>specs/003-js-interop-primitives/contracts/deconstruction-lowering.contract.md</c>.
/// </summary>
public class DeconstructionTranspileTests
{
    private const string Prelude = """
        using System;
        using Metano.Annotations;
        using Metano.Annotations.TypeScript;

        [Transpile, JsTuple, Import("Pair", from: "pairs")]
        public record Pair<A, B>(A First, B Second);

        [Transpile, NoContainer]
        public static class Pairs
        {
            [Import("makePair", from: "pairs")]
            public static Pair<int, int> MakePair() => throw new NotSupportedException();
        }
        """;

    private static string WithEntryPoint(string body) =>
        Prelude
        + $$"""

            [Transpile, NoContainer]
            public static class Program
            {
                [ModuleEntryPoint]
                public static void Main()
                {
            {{body}}
                }
            }
            """;

    // Contract D1 — basic deconstruction → const array binding.
    [Test]
    public async Task BasicDeconstruction_LowersToConstArrayBinding()
    {
        var result = TranspileHelper.Transpile(
            WithEntryPoint(
                """
                        var (a, b) = Pairs.MakePair();
                        System.Console.WriteLine(a);
                        System.Console.WriteLine(b);
                """
            )
        );

        var output = result["program.ts"];
        var expected = TranspileHelper.ReadExpected("deconstruction-basic.ts");
        await Assert.That(output).IsEqualTo(expected);
    }

    // Contract D2 — discard → empty hole.
    [Test]
    public async Task Discard_LowersToEmptyHole()
    {
        var result = TranspileHelper.Transpile(
            WithEntryPoint(
                """
                        var (_, b) = Pairs.MakePair();
                        System.Console.WriteLine(b);
                """
            )
        );

        var output = result["program.ts"];
        var expected = TranspileHelper.ReadExpected("deconstruction-discard.ts");
        await Assert.That(output).IsEqualTo(expected);
        // Only `b` is bound; the discarded slot is an empty hole.
        await Assert.That(output).Contains("const [, b] = makePair();");
    }

    // Contract D3 — later references resolve to the destructured names, not index.
    [Test]
    public async Task References_ResolveToDestructuredNames()
    {
        var result = TranspileHelper.Transpile(
            WithEntryPoint(
                """
                        var (count, setCount) = Pairs.MakePair();
                        System.Console.WriteLine(count);
                        System.Console.WriteLine(setCount);
                """
            )
        );

        var output = result["program.ts"];
        await Assert.That(output).Contains("const [count, setCount] = makePair();");
        await Assert.That(output).Contains("console.log(count);");
        await Assert.That(output).Contains("console.log(setCount);");
        // No index access — bound names, not `pair[0]` / `pair[1]`.
        await Assert.That(output).DoesNotContain("[0]");
        await Assert.That(output).DoesNotContain("[1]");
    }

    // Contract D4 — a reassigned bound local downgrades the binding to `let`.
    [Test]
    public async Task ReassignedBoundLocal_LowersToLet()
    {
        var result = TranspileHelper.Transpile(
            WithEntryPoint(
                """
                        var (a, b) = Pairs.MakePair();
                        a = a + 1;
                        System.Console.WriteLine(a);
                        System.Console.WriteLine(b);
                """
            )
        );

        var output = result["program.ts"];
        await Assert.That(output).Contains("let [a, b] = makePair();");
        await Assert.That(output).DoesNotContain("const [a, b]");
    }
}
