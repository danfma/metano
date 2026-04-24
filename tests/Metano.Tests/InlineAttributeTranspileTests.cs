using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// Tests for <c>[Inline]</c> from <c>Metano.Annotations</c>. The
/// attribute expands a member access into the member's initializer
/// (or expression-bodied getter) at every call site, so the
/// declaration itself never materializes in the generated output.
/// Combines with <c>[Erasable]</c> on the container and
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

            [Erasable]
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

            [Erasable]
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

            [Erasable]
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
    public async Task Inline_Property_CascadesThroughAnotherInline()
    {
        var result = TranspileHelper.Transpile(
            """
            using Metano.Annotations;
            [assembly: TranspileAssembly]

            [Erasable]
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
}
