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
    public async Task Replace_LowersToTheStringReplaceHelper()
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
        await Assert.That(output).DoesNotContain("s.replace(");
    }

    [Test]
    public async Task ReplaceWithAComparison_PassesTheComparisonToTheHelper()
    {
        var output = TranspileText(
            """
            public static string Folded(string s) =>
                s.Replace("x", "-", System.StringComparison.OrdinalIgnoreCase);
            public static string IgnoringCase(string s, bool fold) => s.Replace("x", "-", fold, null);
            """
        );

        await Assert
            .That(output)
            .Contains(
                "return stringReplace(s, \"x\", \"-\", fold ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);"
            );

        await Assert
            .That(output)
            .Contains("return stringReplace(s, \"x\", \"-\", StringComparison.OrdinalIgnoreCase);");
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

        await Assert.That(output).DoesNotContain("String.isNullOr");
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
            public static int Max() => System.Environment.ProcessorCount;
            public static string Empty() => string.Empty;
            public static System.StringComparison Ordinal() => System.StringComparison.Ordinal;
            """
        );

        var unmapped = diagnostics.Single();

        await Assert.That(unmapped.Code).IsEqualTo(DiagnosticCodes.UnsupportedFeature);
        await Assert.That(unmapped.Message).Contains("'System.Environment.ProcessorCount'");
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
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
            public static class Text
            {
                public static string Twice(string s) => Helpers.Twice(s);
            }

            [Transpile]
            public static class Helpers
            {
                public static string Twice(string s) => s + s;
            }
            """
        );

        await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task WhiteSpaceCheck_NarrowsWithoutLosingLiteralComparisons()
    {
        // The `true` branch narrows to the branded `NullOrWhiteSpace`, so comparing
        // the argument with a literal still type-checks (a `""` predicate would make
        // `s === " "` a TS2367 error).
        var output = TranspileText(
            """
            public static int Classify(string? s)
            {
                if (string.IsNullOrWhiteSpace(s))
                    return s == " " ? 1 : 0;

                return s.Length;
            }
            """
        );

        await Assert.That(output).Contains("if (isNullOrWhiteSpace(s)) {");
        await Assert.That(output).Contains("return s === \" \" ? 1 : 0;");
        await Assert.That(output).Contains("return s.length;");
    }

    [Test]
    public async Task SubstringRemoveInsertAndPad_LowerToHelpersThatReadLengths()
    {
        var output = TranspileText(
            """
            public static string Part(string s, int i, int n) => s.Substring(i, n);
            public static string Tail(string s, int i) => s.Substring(i);
            public static string Cut(string s, int i, int n) => s.Remove(i, n) + s.Remove(i);
            public static string Put(string s, int i) => s.Insert(i, "x");
            public static string Pad(string s, int n) => s.PadLeft(n, '0') + s.PadRight(n);
            public static char[] Chars(string s, int i, int n) => s.ToCharArray(i, n);
            """
        );

        await Assert.That(output).Contains("return stringSubstring(s, i, n);");
        await Assert.That(output).Contains("return stringSubstring(s, i);");
        await Assert.That(output).Contains("return stringRemove(s, i, n) + stringRemove(s, i);");
        await Assert.That(output).Contains("return stringInsert(s, i, \"x\");");

        await Assert
            .That(output)
            .Contains("return stringPadLeft(s, n, \"0\") + stringPadRight(s, n);");

        await Assert.That(output).Contains("return stringToCharArray(s, i, n);");
        await Assert.That(output).DoesNotContain(".substring(");
    }

    [Test]
    public async Task SearchWithStartCountOrComparison_LowersToHelpers()
    {
        var output = TranspileText(
            """
            public static int A(string s, int i) => s.IndexOf("a", i);
            public static int B(string s, int i, int n) => s.IndexOf("a", i, n);
            public static int C(string s) => s.IndexOf("a", StringComparison.OrdinalIgnoreCase);
            public static int D(string s, int i) => s.LastIndexOf("ab", i);
            public static bool E(string s) => s.StartsWith("a", StringComparison.Ordinal);
            public static bool F(string s) => s.Contains("a", StringComparison.OrdinalIgnoreCase);
            public static int G(string s) => s.IndexOf("a") + s.LastIndexOf('a');
            """
        );

        await Assert.That(output).Contains("return stringIndexOf(s, \"a\", i);");
        await Assert.That(output).Contains("return stringIndexOf(s, \"a\", i, n);");

        await Assert
            .That(output)
            .Contains(
                "return stringIndexOf(s, \"a\", 0, undefined, StringComparison.OrdinalIgnoreCase);"
            );

        await Assert.That(output).Contains("return stringLastIndexOf(s, \"ab\", i);");

        await Assert
            .That(output)
            .Contains("return stringStartsWith(s, \"a\", StringComparison.Ordinal);");

        await Assert
            .That(output)
            .Contains("return stringContains(s, \"a\", StringComparison.OrdinalIgnoreCase);");

        await Assert.That(output).Contains("return (s.indexOf(\"a\") + s.lastIndexOf(\"a\")) | 0;");
        await Assert.That(output).Contains("import { StringComparison, ");
    }

    [Test]
    public async Task TrimAndSplitWithCharacters_LowerToHelpers()
    {
        var output = TranspileText(
            """
            public static string A(string s) => s.Trim('a') + s.TrimStart('a', 'b') + s.TrimEnd(new[] { 'a' });
            public static string[] B(string s) => s.Split(',', StringSplitOptions.RemoveEmptyEntries);
            public static string[] C(string s) => s.Split(',', 2);
            public static string[] D(string s) => s.Split(' ', '\t');
            public static string[] E(string s) => s.Split(new[] { "ab" }, StringSplitOptions.None);
            public static string[] F(string s) => s.Split();
            """
        );

        await Assert
            .That(output)
            .Contains(
                "return stringTrim(s, \"a\") + stringTrimStart(s, \"a\", \"b\") + stringTrimEnd(s, ...[\"a\"]);"
            );

        await Assert
            .That(output)
            .Contains(
                "return stringSplit(s, \",\", false, undefined, StringSplitOptions.RemoveEmptyEntries);"
            );

        await Assert.That(output).Contains("return stringSplit(s, \",\", false, 2);");
        await Assert.That(output).Contains("return stringSplitChars(s, \" \", \"\\t\");");

        await Assert
            .That(output)
            .Contains("return stringSplit(s, [\"ab\"], true, undefined, StringSplitOptions.None);");

        await Assert.That(output).Contains("return stringSplitChars(s);");
    }

    [Test]
    public async Task JoinWithRange_LowersToTheRangeHelper()
    {
        var output = TranspileText(
            """
            public static string Part(string[] parts, int i, int n) => string.Join(",", parts, i, n);
            """
        );

        await Assert.That(output).Contains("return stringJoinRange(\",\", parts, i, n);");
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
