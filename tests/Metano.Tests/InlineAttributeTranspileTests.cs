using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// Tests for <c>[Inline]</c> from <c>Metano.Annotations</c>. The
/// attribute expands a member access into the member's initializer
/// (or expression-bodied getter) at every call site, so the
/// declaration itself never materializes in the generated output.
/// Combines with <c>[NoContainer]</c> on the container and
/// <c>[PlainObject]</c>/<c>[Branded]</c> on the initializer type to
/// replicate TypeScript's literal-type dispatch without a helper
/// indirection.
/// </summary>
public class InlineAttributeTranspileTests
{
    [Test]
    public async Task Inline_Property_LiteralInitializer_InlinesAtAccessSite()
    {
        var result = TranspileHelper.Transpile(
            """
            using Metano.Annotations;
            [assembly: TranspileAssembly]

            [NoContainer]
            public static class Constants
            {
                [Inline]
                public static string Pi => "pi";
            }

            public class Circle
            {
                public string GetPi() => Constants.Pi;
            }
            """
        );

        var output = result["circle.ts"];
        await Assert.That(output).Contains("return \"pi\";");
        await Assert.That(output).DoesNotContain("Constants.");
        await Assert.That(output).DoesNotContain("pi()");
    }

    [Test]
    public async Task Inline_Field_LiteralInitializer_InlinesAtAccessSite()
    {
        var result = TranspileHelper.Transpile(
            """
            using Metano.Annotations;
            [assembly: TranspileAssembly]

            [NoContainer]
            public static class Constants
            {
                [Inline]
                public static readonly int Answer = 42;
            }

            public class Asker
            {
                public int Ask() => Constants.Answer;
            }
            """
        );

        var output = result["asker.ts"];
        await Assert.That(output).Contains("return 42;");
        await Assert.That(output).DoesNotContain("Constants.");
    }

    [Test]
    public async Task Inline_Property_PlainObjectInitializer_InlinesAsLiteral()
    {
        // The DOM-binding shape: catalog entry built from a
        // [PlainObject] record lowers to the object literal at the
        // call site, with no intermediate helper indirection.
        var result = TranspileHelper.Transpile(
            """
            using Metano.Annotations;
            [assembly: TranspileAssembly]

            [PlainObject]
            public sealed record Tag(string TagName);

            [NoContainer]
            public static class HtmlElementType
            {
                [Inline]
                public static Tag Div => new("div");
            }

            public class Runtime
            {
                public Tag Describe() => HtmlElementType.Div;
            }
            """
        );

        var output = result["runtime.ts"];
        await Assert.That(output).Contains("tagName: \"div\"");
        await Assert.That(output).DoesNotContain("HtmlElementType.");
    }

    // ─── Cross-assembly ───────────────────────────────────────

    [Test]
    public async Task Inline_CrossAssembly_ExpandsFromProjectReference()
    {
        // Regression for issue #109: an [Inline] member declared in a
        // referenced assembly carries a SyntaxTree that belongs to
        // the declaring compilation, not ours. Calling
        // GetSemanticModel on that tree used to throw ArgumentException
        // or fall back to the NoContainer member identifier. Source
        // ProjectReferences should now inline through the referenced
        // compilation.
        var result = TranspileHelper.TranspileWithLibrary(
            """
            using Metano.Annotations;

            [NoContainer]
            public static class Constants
            {
                [Inline]
                public static int Answer => 42;
            }
            """,
            """
            [assembly: TranspileAssembly]

            public class Asker
            {
                public int Ask() => Constants.Answer;
            }
            """
        );

        var output = result["asker.ts"];
        await Assert.That(output).Contains("return 42;");
        await Assert.That(output).DoesNotContain("return answer;");
    }

    [Test]
    public async Task Inline_CrossAssembly_DomBindingCatalogEntry_StaysStringLiteral()
    {
        var result = TranspileHelper.TranspileWithLibrary(
            """
            using Metano.Annotations;
            using Metano.Annotations.TypeScript;

            [External, Ignore]
            public static class Js
            {
                [Name("document")]
                public static Document Document => throw null!;
            }

            [External, Name("HTMLElement")]
            public abstract class HtmlElement
            {
                public string Id { get; set; } = "";
            }

            [External, Name("HTMLDivElement")]
            public abstract class HtmlDivElement : HtmlElement;

            [External]
            public abstract class Document
            {
                public HtmlElement CreateElement(string elementName) => throw null!;
            }

            [Transpile, NoContainer]
            public static class DocumentExtensions
            {
                extension(Document document)
                {
                    [Inline(InlineMode.Substitute)]
                    public TElement CreateElement<TElement>(HtmlElementType.Of<TElement> type)
                        where TElement : HtmlElement
                    {
                        return (TElement)document.CreateElement(type.TagName);
                    }
                }
            }

            [Transpile, NoContainer]
            public static class HtmlElementType
            {
                [PlainObject]
                public sealed record Of<T>(string TagName)
                    where T : HtmlElement;

                [Inline]
                public static Of<HtmlDivElement> Div => new("div");
            }
            """,
            """
            [assembly: TranspileAssembly]

            public class Renderer
            {
                public HtmlElement Create() => Js.Document.CreateElement(HtmlElementType.Div);
            }
            """
        );

        var output = result["renderer.ts"];
        await Assert.That(output).Contains("return document.createElement(\"div\");");
        await Assert.That(output).DoesNotContain("document.createElement(div)");
        await Assert.That(output).DoesNotContain("new HtmlElementType.Of");
    }

    // ─── Diagnostics (MS0016) ─────────────────────────────────

    [Test]
    public async Task Inline_OnInstanceField_EmitsMs0016()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using Metano.Annotations;
            [assembly: TranspileAssembly]

            public class Catalog
            {
                [Inline]
                public readonly int X = 42;
            }
            """
        );

        var ms0016 = diagnostics.FirstOrDefault(d => d.Code == DiagnosticCodes.InvalidInline);
        await Assert.That(ms0016).IsNotNull();
        await Assert.That(ms0016!.Message).Contains("static");
    }

    [Test]
    public async Task Inline_OnMutableField_EmitsMs0016()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using Metano.Annotations;
            [assembly: TranspileAssembly]

            public static class Catalog
            {
                [Inline]
                public static int X = 42;
            }
            """
        );

        var ms0016 = diagnostics.FirstOrDefault(d => d.Code == DiagnosticCodes.InvalidInline);
        await Assert.That(ms0016).IsNotNull();
        await Assert.That(ms0016!.Message).Contains("readonly");
    }

    [Test]
    public async Task Inline_OnFieldWithoutInitializer_EmitsMs0016()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using Metano.Annotations;
            [assembly: TranspileAssembly]

            public static class Catalog
            {
                [Inline]
                public static readonly int X;

                static Catalog() { X = 42; }
            }
            """
        );

        var ms0016 = diagnostics.FirstOrDefault(d => d.Code == DiagnosticCodes.InvalidInline);
        await Assert.That(ms0016).IsNotNull();
        await Assert.That(ms0016!.Message).Contains("initializer");
    }

    [Test]
    public async Task Inline_OnBlockBodiedProperty_EmitsMs0016()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using Metano.Annotations;
            [assembly: TranspileAssembly]

            public static class Catalog
            {
                [Inline]
                public static int X
                {
                    get { return 42; }
                }
            }
            """
        );

        var ms0016 = diagnostics.FirstOrDefault(d => d.Code == DiagnosticCodes.InvalidInline);
        await Assert.That(ms0016).IsNotNull();
        await Assert.That(ms0016!.Message).Contains("expression-bodied");
    }

    [Test]
    public async Task Inline_SelfReferentialProperty_EmitsMs0016()
    {
        // A property whose initializer refers to itself would cause
        // unbounded substitution. The validator detects the cycle
        // via DFS and surfaces MS0016 before extraction runs.
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using Metano.Annotations;
            [assembly: TranspileAssembly]

            public static class Loop
            {
                [Inline]
                public static string X => X;
            }
            """
        );

        var ms0016 = diagnostics.FirstOrDefault(d => d.Code == DiagnosticCodes.InvalidInline);
        await Assert.That(ms0016).IsNotNull();
        await Assert.That(ms0016!.Message).Contains("cycle");
    }

    [Test]
    public async Task Inline_MutualRecursion_EmitsMs0016()
    {
        // Two [Inline] members pointing at each other also form a
        // cycle. The DFS flags at least one of the two.
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using Metano.Annotations;
            [assembly: TranspileAssembly]

            public static class Loop
            {
                [Inline]
                public static string A => B;

                [Inline]
                public static string B => A;
            }
            """
        );

        var ms0016 = diagnostics.FirstOrDefault(d => d.Code == DiagnosticCodes.InvalidInline);
        await Assert.That(ms0016).IsNotNull();
        await Assert.That(ms0016!.Message).Contains("cycle");
    }

    [Test]
    public async Task Inline_Property_CascadesThroughAnotherInline()
    {
        var result = TranspileHelper.Transpile(
            """
            using Metano.Annotations;
            [assembly: TranspileAssembly]

            [NoContainer]
            public static class Colors
            {
                [Inline]
                public static string Primary => "#112233";

                [Inline]
                public static string Highlight => Primary;
            }

            public class Theme
            {
                public string Accent() => Colors.Highlight;
            }
            """
        );

        var output = result["theme.ts"];
        await Assert.That(output).Contains("return \"#112233\";");
        await Assert.That(output).DoesNotContain("Colors.");
    }

    [Test]
    public async Task Inline_OnInstanceMethod_EmitsMs0016()
    {
        // Instance methods cannot be inlined: the body's `this`
        // references would have to be rewritten to the call-site
        // receiver, which is outside the supported substitution
        // surface. The validator rejects non-static `[Inline]` methods
        // up front so users get a diagnostic instead of broken output.
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using Metano.Annotations;
            [assembly: TranspileAssembly]

            public class Counter
            {
                public int Count { get; init; }

                [Inline]
                public int Doubled() => Count * 2;
            }
            """
        );

        var ms0016 = diagnostics.FirstOrDefault(d => d.Code == DiagnosticCodes.InvalidInline);
        await Assert.That(ms0016).IsNotNull();
        await Assert.That(ms0016!.Message).Contains("static");
    }

    [Test]
    public async Task Inline_Method_NamedArguments_BindsByParameterName()
    {
        // Caller uses named arguments in reverse order. The expander
        // must resolve each argument to its target parameter rather
        // than substituting positionally — otherwise `a` would receive
        // 1 and `b` would receive 2, swapping the operands.
        var result = TranspileHelper.Transpile(
            """
            using Metano.Annotations;
            [assembly: TranspileAssembly]

            [NoContainer]
            public static class Math2
            {
                [Inline(InlineMode.Substitute)]
                public static int Sub(int a, int b) => a - b;
            }

            public class Calc
            {
                public int Compute() => Math2.Sub(b: 1, a: 10);
            }
            """
        );

        var output = result["calc.ts"];
        await Assert.That(output).Contains("return (10 - 1) | 0;");
    }

    [Test]
    public async Task Inline_Method_OmittedOptional_FillsExplicitDefault()
    {
        // The caller skips the optional `b`. The expander pulls the
        // parameter's explicit default value into the substitution so
        // the inlined body stays self-contained.
        var result = TranspileHelper.Transpile(
            """
            using Metano.Annotations;
            [assembly: TranspileAssembly]

            [NoContainer]
            public static class Math2
            {
                [Inline(InlineMode.Substitute)]
                public static int Bump(int a, int b = 5) => a + b;
            }

            public class Calc
            {
                public int Compute() => Math2.Bump(7);
            }
            """
        );

        var output = result["calc.ts"];
        await Assert.That(output).Contains("return (7 + 5) | 0;");
    }

    // ─── Static-class [Inline] propagation ───────────────────

    [Test]
    public async Task Inline_StaticClass_PropagatesToEveryStaticMember()
    {
        var result = TranspileHelper.Transpile(
            """
            using Metano.Annotations;
            [assembly: TranspileAssembly]

            [NoContainer, Inline(InlineMode.Substitute)]
            public static class Catalog
            {
                public static readonly int Pi = 3;

                public static int Doubled => Pi * 2;

                public static int Add(int a, int b) => a + b;
            }

            public class Calc
            {
                public int Run() => Catalog.Add(Catalog.Pi, Catalog.Doubled);
            }
            """
        );

        var output = result["calc.ts"];
        await Assert.That(output).Contains("return (3 + Math.imul(3, 2)) | 0;");
    }

    [Test]
    public async Task Inline_StaticClass_PerMemberInlineStillWorks()
    {
        var result = TranspileHelper.Transpile(
            """
            using Metano.Annotations;
            [assembly: TranspileAssembly]

            [NoContainer, Inline(InlineMode.Substitute)]
            public static class Catalog
            {
                public static readonly int Pi = 3;
            }

            public class Calc
            {
                public int Run() => Catalog.Pi;
            }
            """
        );

        var output = result["calc.ts"];
        await Assert.That(output).Contains("return 3;");
    }

    [Test]
    public async Task Inline_OnNonStaticClass_EmitsMs0016()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using Metano.Annotations;
            [assembly: TranspileAssembly]

            [Inline]
            public class Catalog
            {
                public int Pi => 3;
            }
            """
        );

        var error = diagnostics.FirstOrDefault(d =>
            d.Code == Metano.Compiler.Diagnostics.DiagnosticCodes.InvalidInline
            && d.Severity == Metano.Compiler.Diagnostics.MetanoDiagnosticSeverity.Error
        );

        await Assert.That(error).IsNotNull();
        await Assert.That(error!.Message).Contains("Catalog");
        await Assert.That(error.Message).Contains("static");
    }

    [Test]
    public async Task Inline_StaticClass_DoesNotPropagateToMutableStaticField()
    {
        var result = TranspileHelper.Transpile(
            """
            using Metano.Annotations;
            [assembly: TranspileAssembly]

            [NoContainer, Inline(InlineMode.Substitute)]
            public static class Counters
            {
                public static int Value = 1;

                public static int Read() => Value;
            }

            public class Calc
            {
                public int Run() => Counters.Read();
            }
            """
        );

        var output = result["calc.ts"];
        await Assert.That(output).Contains("return value;");
        await Assert.That(output).DoesNotContain("return 1;");
    }

    [Test]
    public async Task Inline_StaticClass_NonPropagatableMember_EmitsWarning()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using Metano.Annotations;
            [assembly: TranspileAssembly]

            [NoContainer, Inline(InlineMode.Substitute)]
            public static class Catalog
            {
                public static readonly int Pi = 3;

                public static int Compute()
                {
                    var x = 1;
                    return x + 1;
                }
            }
            """
        );

        var warning = diagnostics.FirstOrDefault(d =>
            d.Code == Metano.Compiler.Diagnostics.DiagnosticCodes.InvalidInline
            && d.Severity == Metano.Compiler.Diagnostics.MetanoDiagnosticSeverity.Warning
        );

        await Assert.That(warning).IsNotNull();
        await Assert.That(warning!.Message).Contains("Compute");
    }

    [Test]
    public async Task Inline_DefaultMethod_LowersAsIifeAtCallSite()
    {
        var result = TranspileHelper.Transpile(
            """
            using Metano.Annotations;
            [assembly: TranspileAssembly]

            public static class MathHelpers
            {
                [Inline]
                public static int Squared(int x) => x * x;
            }

            public class Caller
            {
                public int Run(int n) => MathHelpers.Squared(n + 1);
            }
            """
        );

        if (result.TryGetValue("math-helpers.ts", out var helpers))
            await Assert.That(helpers).DoesNotContain("squared");

        var output = result["caller.ts"];
        await Assert.That(output).Contains("((x: number) => Math.imul(x, x))((n + 1) | 0)");
        await Assert.That(output).DoesNotContain("(n + 1) * (n + 1)");
    }

    [Test]
    public async Task Inline_MethodPassedAsDelegate_MaterializesAsLambda()
    {
        var result = TranspileHelper.Transpile(
            """
            using Metano.Annotations;
            using System;
            [assembly: TranspileAssembly]

            public static class MathHelpers
            {
                [Inline]
                public static int Squared(int x) => x * x;
            }

            public class Caller
            {
                public int Run(int n)
                {
                    Func<int, int> f = MathHelpers.Squared;
                    return f(n);
                }
            }
            """
        );

        var output = result["caller.ts"];
        await Assert.That(output).Contains("(x: number) => Math.imul(x, x)");
        await Assert.That(output).DoesNotContain("MathHelpers.");
    }

    [Test]
    public async Task Inline_DirectRecursion_Invocation_LowersAsNamedFunctionExpressionIife()
    {
        // #194: an [Inline] method that calls itself directly inside its
        // body has no name to use after the declaration is erased. The
        // body must lower as a JavaScript named function expression so the
        // internal name (`inline$factorial`) carries the recursion.
        var result = TranspileHelper.Transpile(
            """
            using Metano.Annotations;
            [assembly: TranspileAssembly]

            public static class MathHelpers
            {
                [Inline]
                public static int Factorial(int n) => n <= 1 ? 1 : n * Factorial(n - 1);
            }

            public class Caller
            {
                public int Run(int n) => MathHelpers.Factorial(n);
            }
            """
        );

        var output = result["caller.ts"];
        await Assert.That(output).Contains("function inline$factorial(");
        await Assert.That(output).Contains("inline$factorial((n - 1) | 0)");
        await Assert.That(output).DoesNotContain("Factorial(");
    }

    [Test]
    public async Task Inline_DirectRecursion_MethodReference_LowersAsNamedFunctionExpression()
    {
        // Method-group reference position: `Func<int, int> f = Factorial`
        // — same emission path, but the named function expression stands
        // alone (no IIFE wrapper).
        var result = TranspileHelper.Transpile(
            """
            using System;
            using Metano.Annotations;
            [assembly: TranspileAssembly]

            public static class MathHelpers
            {
                [Inline]
                public static int Factorial(int n) => n <= 1 ? 1 : n * Factorial(n - 1);
            }

            public class Caller
            {
                public int Run(int n)
                {
                    Func<int, int> f = MathHelpers.Factorial;
                    return f(n);
                }
            }
            """
        );

        var output = result["caller.ts"];
        await Assert.That(output).Contains("function inline$factorial(");
        await Assert.That(output).Contains("inline$factorial((n - 1) | 0)");
        await Assert.That(output).DoesNotContain("MathHelpers.");
    }

    [Test]
    public async Task Inline_NonRecursive_StillEmitsArrow()
    {
        // Regression guard: without direct self-reference, the lowered
        // body keeps the arrow form (#193 step 2/3 unchanged).
        var result = TranspileHelper.Transpile(
            """
            using Metano.Annotations;
            [assembly: TranspileAssembly]

            public static class MathHelpers
            {
                [Inline]
                public static int Squared(int x) => x * x;
            }

            public class Caller
            {
                public int Run(int n) => MathHelpers.Squared(n);
            }
            """
        );

        var output = result["caller.ts"];
        await Assert.That(output).Contains("((x: number) => Math.imul(x, x))");
        await Assert.That(output).DoesNotContain("function inline$");
    }

    [Test]
    public async Task Inline_SubstituteMode_BetaReducesArgsTextually()
    {
        var result = TranspileHelper.Transpile(
            """
            using Metano.Annotations;
            [assembly: TranspileAssembly]

            public static class MathHelpers
            {
                [Inline(InlineMode.Substitute)]
                public static int Squared(int x) => x * x;
            }

            public class Caller
            {
                public int Run(int n) => MathHelpers.Squared(n + 1);
            }
            """
        );

        var output = result["caller.ts"];
        await Assert.That(output).Contains("Math.imul(n + 1, n + 1)");
        await Assert.That(output).DoesNotContain("=> x * x");
    }
}
