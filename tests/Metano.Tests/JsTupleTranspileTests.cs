using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// Tests for the <c>[JsTuple]</c> attribute (User Story 2, feature
/// 003-js-interop-primitives). A decorated positional record emits as a TS
/// tuple type alias (<c>export type T = [T0, T1]</c>) — the array-shape
/// sibling of <c>[PlainObject]</c>. Positional member access lowers to array
/// index (<c>p.First</c> → <c>p[0]</c>); <c>new T(a, b)</c> lowers to an array
/// literal <c>[a, b]</c>. A <c>[JsTuple, Import]</c> type is erased and
/// resolves to the imported library tuple. Misuse on a non-positional type
/// raises <c>MS0027</c>.
/// </summary>
public class JsTupleTranspileTests
{
    // Contract T1 — standalone [JsTuple] record → tuple type alias.
    [Test]
    public async Task StandaloneJsTuple_EmitsTupleTypeAlias()
    {
        var result = TranspileHelper.Transpile(
            """
            using Metano.Annotations.TypeScript;

            [Transpile, JsTuple]
            public record Pair<A, B>(A First, B Second);
            """
        );

        var output = result["pair.ts"];
        var expected = TranspileHelper.ReadExpected("js-tuple-pair.ts");
        await Assert.That(output).IsEqualTo(expected);
        // No class, no equals/hashCode/with, no constructor, no HashCode import.
        await Assert.That(output).DoesNotContain("class Pair");
        await Assert.That(output).DoesNotContain("equals(");
        await Assert.That(output).DoesNotContain("hashCode(");
        await Assert.That(output).DoesNotContain("metano-runtime");
    }

    // Contract T2 — positional member access → array index.
    [Test]
    public async Task PositionalMemberAccess_LowersToArrayIndex()
    {
        var result = TranspileHelper.Transpile(
            """
            using Metano.Annotations.TypeScript;

            [Transpile, JsTuple]
            public record Pair<A, B>(A First, B Second);

            [Transpile]
            public class Reader
            {
                public int ReadFirst(Pair<int, string> p) => p.First;
                public string ReadSecond(Pair<int, string> p) => p.Second;
            }
            """
        );

        var output = result["reader.ts"];
        await Assert.That(output).Contains("return p[0];");
        await Assert.That(output).Contains("return p[1];");
        await Assert.That(output).DoesNotContain(".first");
        await Assert.That(output).DoesNotContain(".second");
    }

    // Contract T3 — [JsTuple, Import] → erased; reference resolves to the
    // imported library tuple, no local Signal type emitted.
    [Test]
    public async Task ImportedJsTuple_IsErased_ResolvesToImportedType()
    {
        var result = TranspileHelper.Transpile(
            """
            using Metano.Annotations;
            using Metano.Annotations.TypeScript;

            [Transpile, JsTuple, Import("Signal", from: "solid-js")]
            public record Signal<T>(Func<T> Getter, Func<T, T> Setter);

            [Transpile]
            public class Holder
            {
                public Signal<int> Counter { get; set; }
            }
            """
        );

        // No Signal type file is emitted — the type is erased.
        await Assert.That(result.ContainsKey("signal.ts")).IsFalse();

        var holder = result["holder.ts"];
        // The field annotation resolves to the imported Signal<number> type.
        await Assert.That(holder).Contains("Signal<number>");
        await Assert.That(holder).Contains("from \"solid-js\"");
    }

    // Contract T4 — new <JsTuple>T(a, b) lowers to an array literal [a, b].
    [Test]
    public async Task NewJsTuple_LowersToArrayLiteral()
    {
        var result = TranspileHelper.Transpile(
            """
            using Metano.Annotations.TypeScript;

            [Transpile, JsTuple]
            public record Pair<A, B>(A First, B Second);

            [Transpile]
            public class Factory
            {
                public Pair<int, string> Make() => new Pair<int, string>(1, "x");
            }
            """
        );

        var output = result["factory.ts"];
        await Assert.That(output).Contains("return [1, \"x\"];");
        await Assert.That(output).DoesNotContain("new Pair");
    }

    // Contract T5 — [JsTuple] on a non-positional type → MS0027.
    [Test]
    public async Task JsTupleOnNonPositionalType_RaisesMS0027()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using Metano.Annotations.TypeScript;

            [Transpile, JsTuple]
            public record Bad
            {
                public int X { get; init; }
            }
            """
        );

        var diagnostic = diagnostics.FirstOrDefault(d => d.Code == DiagnosticCodes.InvalidJsTuple);
        await Assert.That(diagnostic).IsNotNull();
        await Assert.That(diagnostic!.Severity).IsEqualTo(MetanoDiagnosticSeverity.Error);
    }

    // Contract T5 (negative) — a well-formed positional record raises nothing.
    [Test]
    public async Task JsTupleOnPositionalRecord_NoDiagnostic()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using Metano.Annotations.TypeScript;

            [Transpile, JsTuple]
            public record Good(int A, int B);
            """
        );

        await Assert.That(diagnostics.Any(d => d.Code == DiagnosticCodes.InvalidJsTuple)).IsFalse();
    }
}
