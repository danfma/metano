using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// Tests for <c>[External]</c> from
/// <c>Metano.Annotations.TypeScript</c>. The attribute marks a static
/// class as a stub for runtime globals: the class emits no file,
/// static member access flattens to a bare identifier so
/// <c>Js.Document</c> in C# becomes <c>document</c> in TypeScript.
/// Covers the emission flatten, the MS0012 validation surface, and
/// the no-file contract.
/// </summary>
public class ExternalAttributeTranspileTests
{
    // ─── Emission flatten ────────────────────────────────────

    [Test]
    public async Task External_StaticProperty_AccessFlattensToIdentifier()
    {
        // `Js.Document` on the C# side should lower to `document` (no
        // enclosing type qualifier) on the TS side because `Js` is an
        // `[External]` static class acting as a stub for runtime
        // globals. The `[Name("document")]` on the property drives the
        // emitted identifier.
        var result = TranspileHelper.TranspileWithLibrary(
            """
            using Metano.Annotations.TypeScript;

            [NoEmit]
            public abstract class Document {}

            [External]
            public static class Js
            {
                [Name("document")]
                public static Document Document => throw null!;
            }
            """,
            """
            [assembly: TranspileAssembly]

            public class Renderer
            {
                public Document Target => Js.Document;
            }
            """
        );

        var output = result["renderer.ts"];
        await Assert.That(output).Contains("return document;");
        await Assert.That(output).DoesNotContain("Js.document");
    }

    [Test]
    public async Task External_StaticProperty_ChainedAccessFlattens()
    {
        // Chained call after the flattened access: `Js.Document.Body`
        // lowers to `document.body` (no `Js.` at the root). The member
        // after the first flatten keeps the normal property-access
        // emission so the full chain stays legible.
        var result = TranspileHelper.TranspileWithLibrary(
            """
            using Metano.Annotations.TypeScript;

            [NoEmit, Name("HTMLElement")]
            public abstract class HtmlElement {}

            [NoEmit]
            public abstract class Document
            {
                public HtmlElement Body => throw null!;
            }

            [External]
            public static class Js
            {
                [Name("document")]
                public static Document Document => throw null!;
            }
            """,
            """
            [assembly: TranspileAssembly]

            public class Renderer
            {
                public HtmlElement Root => Js.Document.Body;
            }
            """
        );

        var output = result["renderer.ts"];
        await Assert.That(output).Contains("return document.body;");
        await Assert.That(output).DoesNotContain("Js.");
    }

    [Test]
    public async Task External_StaticMethod_CallFlattens()
    {
        // Static method call on an `[External]` class with a
        // `[Name("parseInt")]` override lowers to a bare call
        // `parseInt("5")` — no `Js.` qualifier.
        var result = TranspileHelper.TranspileWithLibrary(
            """
            using Metano.Annotations.TypeScript;

            [External]
            public static class Js
            {
                [Name("parseInt")]
                public static int ParseInt(string value) => throw null!;
            }
            """,
            """
            [assembly: TranspileAssembly]

            public class Parser
            {
                public int Parse(string s) => Js.ParseInt(s);
            }
            """
        );

        var output = result["parser.ts"];
        await Assert.That(output).Contains("return parseInt(s);");
        await Assert.That(output).DoesNotContain("Js.parseInt");
    }

    [Test]
    public async Task External_Class_EmitsNoFile()
    {
        // `[External]` classes are stubs — no .ts file emits for them
        // even when the class lives in a transpilable assembly.
        var result = TranspileHelper.Transpile(
            """
            using Metano.Annotations.TypeScript;
            [assembly: TranspileAssembly]

            [External]
            public static class Js
            {
                [Name("document")]
                public static object Document => throw null!;
            }

            public class Placeholder {}
            """
        );

        await Assert.That(result).DoesNotContainKey("js.ts");
        await Assert.That(result).ContainsKey("placeholder.ts");
    }

    // ─── Diagnostics (MS0012) ────────────────────────────────

    [Test]
    public async Task External_OnNonStaticClass_EmitsMs0012()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using Metano.Annotations.TypeScript;

            [External]
            public class NotStatic {}
            """
        );

        var ms0012 = diagnostics.FirstOrDefault(d => d.Code == DiagnosticCodes.InvalidExternal);
        await Assert.That(ms0012).IsNotNull();
        await Assert.That(ms0012!.Message).Contains("static");
    }

    [Test]
    public async Task External_WithTranspile_EmitsMs0012()
    {
        // The two attributes are semantically incompatible — one asks
        // for no emission, the other asks for full emission. MS0012
        // surfaces the conflict at extraction time.
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using Metano.Annotations.TypeScript;

            [Transpile, External]
            public static class Mixed
            {
                [Name("x")] public static int Value => 0;
            }
            """
        );

        var ms0012 = diagnostics.FirstOrDefault(d => d.Code == DiagnosticCodes.InvalidExternal);
        await Assert.That(ms0012).IsNotNull();
        await Assert.That(ms0012!.Message).Contains("Transpile");
    }

    [Test]
    public async Task External_OnValidStaticClass_EmitsNoDiagnostic()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using Metano.Annotations.TypeScript;

            [External]
            public static class Js
            {
                [Name("document")]
                public static object Document => throw null!;
            }
            """
        );

        await Assert
            .That(diagnostics.Any(d => d.Code == DiagnosticCodes.InvalidExternal))
            .IsFalse();
    }

    // ─── Doesn't affect non-External static classes ─────────

    [Test]
    public async Task NonExternal_StaticClass_AccessStillUsesPrefix()
    {
        // Baseline: `[ExportedAsModule]` (without `[External]`) emits
        // a module file and a static access through the class reference
        // stays qualified with the class name. The flatten only kicks
        // in when `[External]` is explicit.
        var result = TranspileHelper.Transpile(
            """
            [assembly: TranspileAssembly]

            [Transpile, ExportedAsModule]
            public static class Helpers
            {
                public static int Zero() => 0;
            }

            public class Consumer
            {
                public int Call() => Helpers.Zero();
            }
            """
        );

        var output = result["consumer.ts"];
        // `Helpers.Zero()` lowers to `Helpers.zero()` — the prefix
        // survives because no flatten happens without [External].
        await Assert.That(output).Contains("Helpers.zero()");
    }
}
