namespace Metano.Tests;

/// <summary>
/// A <c>var</c> local C# declares nullable and later assigns a maybe-null value
/// carries its nullable annotation, where a local never reassigned, or
/// reassigned only with non-null values, keeps the inferred form (#796).
/// </summary>
public class VarLocalNullableAnnotationTests
{
    private static string Emit(string source) =>
        TranspileHelper.Transpile(
            $$"""
            #nullable enable
            using System;
            public interface IPort { void Run(); }
            public sealed class ProbeNode { public ProbeNode? Next { get; set; } public int V; }
            [Transpile]
            public static class Probes
            {
                {{source}}
            }
            """
        )["probes.ts"];

    [Test]
    public async Task VarLocal_ReassignedWithAMaybeNullValue_IsAnnotated()
    {
        var output = Emit(
            """
            public static int Count(ProbeNode start)
            {
                var current = start;
                var n = 0;
                while (current is not null) { n++; current = current.Next; }
                return n;
            }
            """
        );

        await Assert.That(output).Contains("let current: ProbeNode | null = start;");
    }

    [Test]
    public async Task VarLocal_NeverReassignedOrReassignedNonNull_KeepsTheInferredForm()
    {
        var output = Emit(
            """
            public static int Plain(ProbeNode start) { var c = start; return c.V; }
            public static int Swap(ProbeNode start, ProbeNode other) { var c = start; c = other; return c.V; }
            """
        );

        await Assert.That(output).Contains("const c = start;");
        await Assert.That(output).Contains("let c = start;");
        await Assert.That(output).DoesNotContain("| null");
    }
}
