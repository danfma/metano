using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// <c>System.String</c> members whose JavaScript namesake has different semantics
/// (<c>src/Metano/Runtime/Strings.cs</c>, #324): <c>Replace</c> replaces every
/// occurrence literally, and the <c>IsNullOrEmpty</c> / <c>IsNullOrWhiteSpace</c>
/// statics lower to null-safe runtime helpers. A static member of a BCL type with no
/// mapping reports MS0001 instead of emitting a reference nothing declares.
/// </summary>
public class StringMemberMappingTests
{
    [Test]
    public async Task Replace_LowersToTheReplaceAllHelper()
    {
        var output = TranspileText(
            """
            public static string Chars(string s) => s.Replace('a', 'b');
            public static string Strings(string s) => s.Replace("a.", "$&");
            public static string Remove(string s) => s.Replace("x", null);
            public static string Grouped(string? s) => (s ?? "").Replace("a", "b");
            """
        );

        await Assert.That(output).Contains("return stringReplace(s, \"a\", \"b\");");
        await Assert.That(output).Contains("return stringReplace(s, \"a.\", \"$&\");");
        await Assert.That(output).Contains("return stringReplace(s, \"x\", null);");
        await Assert.That(output).Contains("return stringReplace(s ?? \"\", \"a\", \"b\");");
        await Assert.That(output).Contains("import { stringReplace } from \"metano-runtime\";");
        await Assert.That(output).DoesNotContain(".replace(");
    }

    [Test]
    public async Task NullOrEmptyChecks_LowerToNullSafeHelpers()
    {
        var output = TranspileText(
            """
            public static bool Empty(string? s) => string.IsNullOrEmpty(s);
            public static bool Blank(string? s) => string.IsNullOrWhiteSpace(s);
            public static int Length(string? s) => string.IsNullOrEmpty(s) ? -1 : s.Length;
            public static bool OfCall(string? s) => string.IsNullOrWhiteSpace(s?.Trim());
            """
        );

        await Assert.That(output).Contains("return isNullOrEmpty(s);");
        await Assert.That(output).Contains("return isNullOrWhiteSpace(s);");
        await Assert.That(output).Contains("return isNullOrEmpty(s) ? -1 : s.length;");
        await Assert.That(output).Contains("return isNullOrWhiteSpace(s?.trim() ?? null);");
        await Assert
            .That(output)
            .Contains("import { isNullOrEmpty, isNullOrWhiteSpace } from \"metano-runtime\";");
        await Assert.That(output).DoesNotContain("String.");
    }

    [Test]
    public async Task MappedStringStatics_ReportNoDiagnostic()
    {
        var diagnostics = TranspileDiagnostics(
            """
            public static bool Blank(string? s) => string.IsNullOrWhiteSpace(s);
            public static string Joined(string[] parts) => string.Join(",", parts);
            """
        );

        await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task UnmappedBclStatic_ReportsUnsupportedFeature()
    {
        var diagnostics = TranspileDiagnostics(
            """
            public static int Ordinal(string a, string b) => string.CompareOrdinal(a, b);
            """
        );

        var unmapped = diagnostics.Single();
        await Assert.That(unmapped.Code).IsEqualTo(DiagnosticCodes.UnsupportedFeature);
        await Assert.That(unmapped.Severity).IsEqualTo(MetanoDiagnosticSeverity.Warning);
        await Assert.That(unmapped.Message).Contains("'string.CompareOrdinal'");
    }

    [Test]
    public async Task UnmappedBclStaticRead_ReportsUnsupportedFeature()
    {
        var diagnostics = TranspileDiagnostics(
            """
            public static int Max() => int.MaxValue;
            public static string Empty() => string.Empty;
            public static System.StringComparison Ordinal() => System.StringComparison.Ordinal;
            """
        );

        var unmapped = diagnostics.Single();
        await Assert.That(unmapped.Code).IsEqualTo(DiagnosticCodes.UnsupportedFeature);
        await Assert.That(unmapped.Message).Contains("'int.MaxValue'");
        await Assert.That(unmapped.Message).Contains("the read is emitted by name");
    }

    [Test]
    public async Task StaticOfBclExportedType_ReportsNoDiagnostic()
    {
        // `[ExportFromBcl]` binds `decimal` to decimal.js's `Decimal`, whose statics
        // resolve by name.
        var diagnostics = TranspileDiagnostics(
            """
            public static decimal Larger(decimal a, decimal b) => decimal.Max(a, b);
            """
        );

        await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task StaticsOfHandWrittenBindings_ReportNoDiagnostic()
    {
        // An untranspiled library that declares JS globals: `[External]` / `[Name]`
        // members and `[NoContainer]` / `[External]` types bind by hand.
        var (_, diagnostics) = TranspileHelper.TranspileWithLibraryAndDiagnostics(
            """
            using Metano.Annotations;
            using Metano.Annotations.TypeScript;

            namespace Bindings;

            public static class Globals
            {
                [External, Name("innerWidth")]
                public static int InnerWidth => 0;

                [Name("alert")]
                public static void Alert(string message) { }
            }

            [NoContainer]
            public static class Flat
            {
                public static int Version => 1;
            }

            [External]
            public static class Console2
            {
                public static void Log(string message) { }
            }
            """,
            """
            using Bindings;

            [Transpile]
            public static class Uses
            {
                public static int Width() => Globals.InnerWidth + Flat.Version;
                public static void Say(string text)
                {
                    Globals.Alert(text);
                    Console2.Log(text);
                }
            }
            """
        );

        await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task StaticOfTranspiledType_ReportsNoDiagnostic()
    {
        var diagnostics = TranspileDiagnostics(
            """
            public static string Twice(string s) => Helpers.Twice(s);
            }

            [Transpile]
            public static class Helpers
            {
                public static string Twice(string s) => s + s;
            """
        );

        await Assert.That(diagnostics).IsEmpty();
    }

    private static string TranspileText(string members) =>
        TranspileHelper.Transpile(Source(members))["text.ts"];

    private static IReadOnlyList<MetanoDiagnostic> TranspileDiagnostics(string members) =>
        TranspileHelper.TranspileWithDiagnostics(Source(members)).Diagnostics;

    private static string Source(string members) =>
        $$"""
            [Transpile]
            public static class Text
            {
            {{members}}
            }
            """;
}
