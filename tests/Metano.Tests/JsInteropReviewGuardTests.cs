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
///   <item>Deconstruction reads each part the way the right-hand side
///   lowers: a JS array (an array, a <c>[JsTuple]</c> record, a
///   <c>ValueTuple</c>) by position, a user type through its emitted
///   <c>Deconstruct</c> call (#779).</item>
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

    // Guard 1a — a ValueTuple literal is taken apart element by element (#779):
    // `var (a, b) = (1, 2);` declares each local from its element.
    [Test]
    public async Task ValueTupleLiteralDeconstruction_DeclaresEachElement()
    {
        var body = ExtractFromMethod(
            """
            void M()
            {
                var (a, b) = (1, 2);
            }
            """
        );

        await Assert.That(body.Count).IsEqualTo(2);
        await Assert.That(((IrVariableDeclaration)body[0]).Name).IsEqualTo("a");
        await Assert.That(((IrVariableDeclaration)body[1]).Name).IsEqualTo("b");
    }

    // Guard 1b — a user type with a custom Deconstruct is NOT a JS array: the
    // deconstruction reads the multi-value result of its emitted Deconstruct
    // call (#779), never the receiver itself.
    [Test]
    public async Task UserDeconstructType_ReadsTheDeconstructCall()
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

        var deconstruction = (IrTupleDeconstruction)body[0];

        await Assert.That(deconstruction.Initializer).IsTypeOf<IrOutArgumentCall>();
        await Assert.That(deconstruction.BoundNames.ToList()).IsEquivalentTo(["x", "y"]);
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

        var extractor = new IrStatementExtractor(model, IrExtractionContext.Empty);

        return extractor.ExtractBody(target.Body, target.ExpressionBody, isVoid: true).Statements;
    }
}
