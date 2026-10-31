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

    // A positional member read through `?.` keeps the short-circuit: `p?.[1]`.
    [Test]
    public async Task PositionalMemberAccess_ThroughNullConditional_LowersToOptionalIndex()
    {
        var result = TranspileHelper.Transpile(
            """
            #nullable enable
            using Metano.Annotations.TypeScript;

            [Transpile, JsTuple]
            public record Pair<A, B>(A First, B Second);

            [Transpile]
            public class Reader
            {
                public string? ReadSecond(Pair<int, string>? p) => p?.Second;
            }
            """
        );

        await Assert.That(result["reader.ts"]).Contains("return p?.[1];");
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

    // A [JsTuple] is a type alias with no runtime value, so a type test
    // checks the array shape and arity instead of `instanceof`, and the file
    // never value-imports the alias (#253).
    [Test]
    public async Task PositionalPatternOnJsTuple_TestsArrayShapeWithoutValueImport()
    {
        var result = TranspileHelper.Transpile(
            """
            using Metano.Annotations.TypeScript;

            [Transpile, JsTuple]
            public record JPair(int First, string Second);

            [Transpile]
            public class Matcher
            {
                public bool StartsWithOne(object o) => o is JPair(1, _);
                public bool IsNotPair(object o) => o is not JPair;
            }
            """
        );

        var output = result["matcher.ts"];
        await Assert
            .That(output)
            .Contains("return Array.isArray(o) && o.length === 2 && o[0] === 1;");
        await Assert.That(output).Contains("return !(Array.isArray(o) && o.length === 2);");
        // Nothing references the alias as a value, so no value import exists.
        await Assert.That(output).DoesNotContain("import { JPair }");
        await Assert.That(output).DoesNotContain("instanceof JPair");
    }

    // A designated type pattern binds the matched array (#253).
    [Test]
    public async Task TypePatternWithDesignatorOnJsTuple_BindsTheMatchedArray()
    {
        var result = TranspileHelper.Transpile(
            """
            using Metano.Annotations.TypeScript;

            [Transpile, JsTuple]
            public record JPair(int First, string Second);

            [Transpile]
            public class Matcher
            {
                public int FirstOrZero(object o) => o switch { JPair p => p.First, _ => 0 };
            }
            """
        );

        var output = result["matcher.ts"];
        await Assert.That(output).Contains("if (Array.isArray($s) && $s.length === 2) {");
        await Assert.That(output).Contains("const p = $s;");
        await Assert.That(output).Contains("return p[0];");
        await Assert.That(output).DoesNotContain("instanceof JPair");
    }

    // A [JsTuple] from a referenced package has no source syntax; its arity
    // comes from the record's synthesized Deconstruct (#253).
    [Test]
    public async Task TypeTestOnReferencedJsTuple_ReadsArityFromMetadata()
    {
        var result = TranspileHelper.TranspileWithLibrary(
            """
            using Metano.Annotations.TypeScript;

            [assembly: TranspileAssembly]
            [assembly: EmitPackage("tuple-lib")]

            [JsTuple]
            public record Triple(int A, int B, int C);
            """,
            """
            [Transpile]
            public class Matcher
            {
                public bool IsTriple(object o) => o is Triple;
            }
            """
        );

        await Assert
            .That(result["matcher.ts"])
            .Contains("return Array.isArray(o) && o.length === 3;");
        await Assert.That(result["matcher.ts"]).DoesNotContain("instanceof Triple");
    }

    // A side-effecting subject is evaluated once even though the array-shape
    // test reads it twice (#253).
    [Test]
    public async Task TypeTestOnSideEffectingSubject_EvaluatesItOnce()
    {
        var result = TranspileHelper.Transpile(
            """
            using System;
            using Metano.Annotations.TypeScript;

            [Transpile, JsTuple]
            public record JPair(int First, string Second);

            [Transpile]
            public class Matcher
            {
                public bool IsPair(Func<object> next) => next() is JPair;
            }
            """
        );

        var output = result["matcher.ts"];
        await Assert.That(output).Contains("Array.isArray($v) && $v.length === 2");
        await Assert.That(output).Contains("(next())");
        await Assert.That(output).DoesNotContain("Array.isArray(next())");
    }

    // A pattern that reads the subject once (`not null`) stays inline even on
    // a side-effecting subject; one that reads it repeatedly binds an awaited
    // subject as the argument of a non-async arrow (#253).
    [Test]
    public async Task PatternSubjectBinding_OnlyWhenThePatternReadsItRepeatedly()
    {
        var result = TranspileHelper.Transpile(
            """
            using System.Threading.Tasks;

            [Transpile]
            public record Box(int X);

            [Transpile]
            public class Probe
            {
                public Box? Next() => null;
                public Task<Box> Load() => Task.FromResult(new Box(1));

                public bool HasNext() => Next() is not null;
                public async Task<bool> IsOne() => await Load() is { X: 1 };
            }
            """
        );

        var output = result["probe.ts"];
        await Assert.That(output).Contains("return !(this.next() == null);");
        await Assert.That(output).Contains("(($v) => $v != null && $v.x === 1)(await this.load())");
        await Assert.That(output).DoesNotContain("async ($v)");
    }

    // An explicit cast keeps the TS assertion like `as` (#253).
    [Test]
    public async Task ExplicitCastToJsTuple_KeepsTypeAssertion()
    {
        var result = TranspileHelper.Transpile(
            """
            using Metano.Annotations.TypeScript;

            [Transpile, JsTuple]
            public record JPair(int First, string Second);

            [Transpile]
            public class Matcher
            {
                public JPair Force(object o) => (JPair)o;
            }
            """
        );

        await Assert.That(result["matcher.ts"]).Contains("return o as JPair;");
    }

    // `Array.isArray` narrows to `any[]`, not to the tuple, so a cast to a
    // [JsTuple] keeps a TS assertion for the result to type-check (#253).
    [Test]
    public async Task AsCastToJsTuple_KeepsTypeAssertion()
    {
        var result = TranspileHelper.Transpile(
            """
            #nullable enable
            using Metano.Annotations.TypeScript;

            [Transpile, JsTuple]
            public record JPair(int First, string Second);

            [Transpile]
            public class Matcher
            {
                public JPair? AsPair(object o) => o as JPair;
            }
            """
        );

        await Assert
            .That(result["matcher.ts"])
            .Contains("return Array.isArray(o) && o.length === 2 ? o as JPair : null;");
    }
}
