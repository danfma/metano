namespace Metano.Tests;

/// <summary>
/// <c>null!</c> / <c>default!</c> over a non-nullable reference type keeps the
/// assertion, so the emitted package type-checks under <c>--strict</c> (#794).
/// </summary>
public class NullForgivingLiteralTests
{
    private static string Emit(string source) =>
        TranspileHelper.Transpile(
            $$"""
            #nullable enable
            using System;
            using System.Collections.Generic;
            public interface IPort { void Run(); }
            public sealed class Computed<T> { public T Value = default!; }
            public sealed class Port : IPort { public void Run() { } }
            public sealed class ProbeNode { public ProbeNode? Next { get; set; } public int V; }
            [Transpile]
            public static class Probes
            {
                {{source}}
            }
            """
        )["probes.ts"];

    [Test]
    public async Task NullForgivingLiteral_KeepsTheAssertionForANonNullableReference()
    {
        var output = Emit(
            """
            private static readonly IPort Missing = default!;
            public static void Use(IPort port) { }
            public static void Call() { Use(null!); Use((IPort)null!); Use(default!); IPort x = null!; Use(x); }
            """
        );

        await Assert.That(output).Contains("private static readonly missing: IPort = null!;");
        await Assert.That(output).Contains("const x: IPort = null!;");
        await Assert.That(output).DoesNotContain("use(null);");
    }

    [Test]
    public async Task NullForgivingLiteral_OfANullableType_StaysBare()
    {
        var output = Emit("public static string? Run() { string? s = null!; return s; }");

        await Assert.That(output).DoesNotContain("null!");
    }

    [Test]
    public async Task ExplicitlyTypedLocal_InitializedWithNullForgiving_KeepsItsAnnotation()
    {
        var output = Emit(
            """
            public static void Locals<T>() where T : class
            {
                IPort handle = null!;
                ProbeNode node = null!;
                List<int> items = null!;
                Computed<int> computed = null!;
                T generic = null!;
                IPort viaDefault = default!;
                handle = new Port();
            }
            """
        );

        await Assert.That(output).Contains("let handle: IPort = null!;");
        await Assert.That(output).Contains("const node: ProbeNode = null!;");
        await Assert.That(output).Contains("const items: List<number> = null!;");
        await Assert.That(output).Contains("const computed: Computed<number> = null!;");
        await Assert.That(output).Contains("const generic: T = null!;");
        await Assert.That(output).Contains("const viaDefault: IPort = null!;");
    }

    [Test]
    public async Task NullForgivingStore_InAConstructorOrAfterANullTest_KeepsTheAssertion()
    {
        var output = TranspileHelper.Transpile(
            """
            #nullable enable
            [Transpile]
            public sealed class Node { }

            [Transpile]
            public sealed class CtorNull
            {
                public string Name;
                public string Prop { get; set; }
                public Node Link;

                public CtorNull() { Name = null!; Prop = null!; Link = null!; }

                public void Clear() { if (Name == null) { } Name = null!; }

                public void Nullable(string? keep) { keep = null!; }
            }
            """
        )["ctor-null.ts"];

        await Assert.That(output).Contains("this.name = null!;");
        await Assert.That(output).Contains("this.prop = null!;");
        await Assert.That(output).Contains("this.link = null!;");
        await Assert.That(output).DoesNotContain("this.name = null;");
        await Assert.That(output).Contains("keep = null;");
    }
}
