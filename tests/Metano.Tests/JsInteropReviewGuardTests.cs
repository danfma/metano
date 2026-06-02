using Metano.Compiler.Diagnostics;
using Metano.Compiler.Extraction;
using Metano.Compiler.IR;
using Metano.Tests.IR;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Tests;

/// <summary>
/// Guard tests added during the dual-agent review of feature
/// 003-js-interop-primitives. They pin the three semantic gates that were
/// missing from the first cut:
/// <list type="number">
///   <item>Deconstruction is intercepted at extraction only when the right-hand
///   side is actually a JS array (an array type or a <c>[JsTuple]</c> record). A
///   native <c>ValueTuple</c> or a user type with a custom <c>Deconstruct</c>
///   must NOT silently become an <see cref="IrTupleDeconstruction"/> (which
///   would emit <c>const [a, b] = …</c> array-destructuring a non-array); it
///   falls through to <see cref="IrUnsupportedStatement"/> (loud, per
///   Constitution V — native ValueTuple deconstruction is out of scope,
///   research D7).</item>
///   <item>A <c>[JsTuple]</c> record must be positional-only: an extra
///   instance member raises <c>MS0027</c> instead of being silently dropped and
///   mis-accessed on the array.</item>
///   <item>A <c>[JsCallable]</c> interface used in type position lowers to an
///   inline function type (no dangling erased name), and a non-<c>Invoke</c>
///   member inherited from a base interface still raises <c>MS0028</c>.</item>
/// </list>
/// </summary>
public class JsInteropReviewGuardTests
{
    // ─── Guard 1: deconstruction interception is semantic, not syntactic ───

    // Guard 1a — native ValueTuple deconstruction is OUT OF SCOPE (research D7).
    // `var (a, b) = (1, 2);` parses as the deconstruction syntax but the RHS is a
    // ValueTuple, not a JS array — it must NOT be intercepted as an
    // IrTupleDeconstruction. It surfaces as a non-intercepted statement carrying
    // an unsupported node (loud — the body coverage probe rejects it).
    [Test]
    public async Task NativeValueTupleDeconstruction_NotInterceptedAndSurfacesUnsupported()
    {
        var body = ExtractFromMethod(
            """
            void M()
            {
                var (a, b) = (1, 2);
            }
            """
        );

        await Assert.That(body[0]).IsNotTypeOf<IrTupleDeconstruction>();
        await Assert.That(ContainsUnsupported(body[0])).IsTrue();
    }

    // Guard 1b — a user type exposing a custom Deconstruct is NOT a JS array, so
    // `var (x, y) = point;` must NOT be intercepted as array destructuring.
    [Test]
    public async Task UserDeconstructType_NotInterceptedAndSurfacesUnsupported()
    {
        var body = ExtractFromMethod(
            """
            void M(Point point)
            {
                var (x, y) = point;
            }
            """,
            """
            public class Point
            {
                public int X { get; set; }
                public int Y { get; set; }
                public void Deconstruct(out int x, out int y) { x = X; y = Y; }
            }
            """
        );

        await Assert.That(body[0]).IsNotTypeOf<IrTupleDeconstruction>();
        await Assert.That(ContainsUnsupported(body[0])).IsTrue();
    }

    // Guard 1c (positive) — a [JsTuple] record RHS IS a JS array, so it is
    // intercepted.
    [Test]
    public async Task JsTupleDeconstruction_IsInterceptedAsTupleDeconstruction()
    {
        var body = ExtractFromMethod(
            """
            void M(Pair p)
            {
                var (a, b) = p;
            }
            """,
            """
            [Metano.Annotations.TypeScript.JsTuple]
            public record Pair(int First, int Second);
            """
        );

        await Assert.That(body[0]).IsTypeOf<IrTupleDeconstruction>();
    }

    // ─── Guard 2: [JsTuple] must be positional-only ───

    // A [JsTuple] record with an extra (non-positional) instance member raises
    // MS0027. The positional ctor exists, so the old "no positional ctor" branch
    // would pass it through silently.
    [Test]
    public async Task JsTupleWithExtraMember_RaisesMS0027()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using Metano.Annotations.TypeScript;

            [Transpile, JsTuple]
            public record Pair(int A, int B)
            {
                public int Sum => A + B;
            }
            """
        );

        var diagnostic = diagnostics.FirstOrDefault(d => d.Code == DiagnosticCodes.InvalidJsTuple);
        await Assert.That(diagnostic).IsNotNull();
        await Assert.That(diagnostic!.Message).Contains("positional-only");
    }

    // Negative — a positional-only [JsTuple] record raises nothing, including its
    // synthesized record members (Equals/GetHashCode/…).
    [Test]
    public async Task JsTuplePositionalOnly_NoDiagnostic()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using Metano.Annotations.TypeScript;

            [Transpile, JsTuple]
            public record Pair(int A, int B);
            """
        );

        await Assert.That(diagnostics.Any(d => d.Code == DiagnosticCodes.InvalidJsTuple)).IsFalse();
    }

    // ─── Guard 3: [JsCallable] type position + inherited members ───

    // Guard 3a — a [JsCallable]-typed parameter annotation is self-contained: it
    // lowers to an inline TS function type, with no dangling erased interface
    // name and no import of it.
    [Test]
    public async Task JsCallableInTypePosition_LowersToInlineFunctionType()
    {
        var result = TranspileHelper.Transpile(
            """
            using Metano.Annotations.TypeScript;

            [Transpile, JsCallable]
            public interface ISetter { void Invoke(int value); }

            [Transpile]
            public class Consumer
            {
                public void Go(ISetter setCount) => setCount.Invoke(5);
            }
            """
        );

        var output = result["consumer.ts"];
        // Self-contained inline function type — no dangling erased name.
        await Assert.That(output).Contains("(value: number) => void");
        await Assert.That(output).DoesNotContain("ISetter");
        await Assert.That(output).DoesNotContain("import");
    }

    // Guard 3b — a non-Invoke member inherited from a BASE interface escapes the
    // declared-members-only scan; AllInterfaces must be walked → MS0028.
    [Test]
    public async Task JsCallableWithInheritedNonInvokeMember_RaisesMS0028()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using Metano.Annotations.TypeScript;

            [Transpile]
            public interface IBase { int Other(); }

            [Transpile, JsCallable]
            public interface ICallable : IBase { void Invoke(int x); }
            """
        );

        await Assert
            .That(diagnostics.Any(d => d.Code == DiagnosticCodes.InvalidJsCallable))
            .IsTrue();
    }

    /// <summary>
    /// True when <paramref name="statement"/> is, or directly wraps, an
    /// unsupported IR node — covering both the
    /// <see cref="IrUnsupportedStatement"/> form and the
    /// <see cref="IrExpressionStatement"/> wrapping an
    /// <see cref="IrUnsupportedExpression"/> (the shape a non-intercepted
    /// deconstruction assignment falls through to: its
    /// <see cref="DeclarationExpressionSyntax"/> left side has no IR mapping). A
    /// deconstruction that escaped the JS-array guard lands in one of these, so
    /// the body coverage probe rejects the body rather than emitting wrong code.
    /// </summary>
    private static bool ContainsUnsupported(IrStatement statement) =>
        statement switch
        {
            IrUnsupportedStatement => true,
            IrExpressionStatement es => ContainsUnsupported(es.Expression),
            _ => false,
        };

    private static bool ContainsUnsupported(IrExpression expression) =>
        expression switch
        {
            IrUnsupportedExpression => true,
            IrBinaryExpression bin => ContainsUnsupported(bin.Left)
                || ContainsUnsupported(bin.Right),
            _ => false,
        };

    /// <summary>
    /// Extracts the statements of the subject method's body via
    /// <see cref="IrStatementExtractor"/> — the same harness
    /// <c>IrExpressionExtractionTests</c> uses. The subject method is the last
    /// one declared so optional <paramref name="extraMembers"/> (sibling type
    /// declarations or helper members) can precede it.
    /// </summary>
    private static IReadOnlyList<IrStatement> ExtractFromMethod(
        string method,
        string? extraMembers = null
    )
    {
        var csharp = $$"""
            {{extraMembers ?? ""}}
            public class Subject
            {
                {{method}}
            }
            """;
        var compilation = IrTestHelper.Compile(csharp);
        var tree = compilation.SyntaxTrees.First();
        var model = compilation.GetSemanticModel(tree);
        var target = tree.GetRoot()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Last(m => m.Identifier.ValueText == "M");
        var extractor = new IrStatementExtractor(model);
        return extractor.ExtractBody(target.Body, target.ExpressionBody, isVoid: true);
    }
}
