using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// The format and alignment clauses of interpolation holes and the format of
/// <c>ToString(format)</c> on the numbers (baseline FR-051, #323). TypeScript
/// lowers a clause to one runtime formatter call picked by the value's type
/// (golden <c>interpolation-format-clauses.ts</c>, which type-checks under
/// <c>tsgo --strict --noImplicitOverride</c>); a clause the runtime does not
/// format reports MS0001 and lowers to the throwing placeholder; a hole
/// without a clause emits what it emitted before. Dart honors no clause yet
/// and reports every one.
/// </summary>
public class InterpolationFormatClauseTests
{
    private const string SupportedSource = """
        [Transpile]
        public class Clauses
        {
            public string Integers(int i, uint u, short s, ushort us, sbyte sb, byte b) =>
                $"{i:D2}|{u:X8}|{s:X}|{us,6:N0}|{sb:x}|{b,-4:D3}";

            public string Doubles(double d) =>
                $"{d:F2}|{d,10:N1}|{d:E3}|{d:P0}|{d:G5}|{d:C}|{d:R}|{d:e}";

            public string SixtyFourBits(long l, ulong u) => $"{l:E2}|{u,8:P1}|{l:C0}";

            public string Nullables(int? i, double? d, long? l) => $"{i:D2}|{d,8:F1}|{l:N0}";

            public string Alignments(int i, double d, string s, bool b, string? n) =>
                $"[{i,5}][{d,-8}][{s,4}][{b,6}][{n,3}]";

            public string IgnoredFormats(string s, bool b, char c) => $"{s:X}|{b:D2}|{c,3:N}";

            public string ToStrings(int i, double d, byte b, short s) =>
                i.ToString("D4") + d.ToString("F3", null)
                + b.ToString("X2") + s.ToString("x");
        }
        """;

    private const string Placeholder =
        "throw new Error(\"Unsupported expression: UnsupportedFormat\");";

    private const string Host = """
        using System;

        [Transpile]
        public enum Color { Red }

        [Transpile]
        public sealed class Money : IFormattable
        {
            public string ToString(string? format, IFormatProvider? provider) => "money";
        }

        [Transpile]
        public class Holes
        {
        """;

    [Test]
    public async Task SupportedClauses_CallTheRuntimeFormatterOfTheValuesType()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(SupportedSource);

        await TranspileHelper.AssertGolden("interpolation-format-clauses.ts", files["clauses.ts"]);
        await Assert.That(UnsupportedFeatures(diagnostics)).IsEmpty();
    }

    /// <summary>
    /// A hole without a clause is the plain template-literal hole it was
    /// before (a maybe-null one still formats as <c>""</c>), so a project
    /// without clauses imports no formatter.
    /// </summary>
    [Test]
    public async Task HolesWithoutClauses_AreUnchanged()
    {
        var files = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Plain
            {
                public string Show(int i, double d, bool b, long l, int? n, string s) =>
                    $"{i} {d} {b} {l} {n} {s}";
            }
            """
        );

        await Assert
            .That(files["plain.ts"])
            .Contains("return `${i} ${d} ${b} ${l} ${n ?? \"\"} ${s}`;");

        await Assert.That(files["plain.ts"]).DoesNotContain("metano-runtime");
    }

    [Test]
    [Arguments("$\"{m:F2}\"", "decimal m", "F2", "decimal")]
    [Arguments("$\"{f:F2}\"", "float f", "F2", "float")]
    [Arguments("$\"{t:yyyy-MM-dd}\"", "DateTime t", "yyyy-MM-dd", "DateTime")]
    [Arguments("$\"{t:yyyy-MM-dd}\"", "DateOnly t", "yyyy-MM-dd", "DateOnly")]
    [Arguments("$\"{g:N}\"", "Guid g", "N", "Guid")]
    [Arguments("$\"{c:D}\"", "Color c", "D", "Color")]
    [Arguments("$\"{m:C}\"", "Money m", "C", "Money")]
    [Arguments("$\"{o:D2}\"", "object o", "D2", "object")]
    [Arguments("$\"{d:0.00}\"", "double d", "0.00", "double")]
    [Arguments("$\"{i,8:#,##0}\"", "int i", "#,##0", "int")]
    [Arguments("$\"{i:B8}\"", "int i", "B8", "int")]
    [Arguments("$\"{n:F2,5}\"", "double? n", "F2,5", "double?")]
    public async Task UnsupportedFormat_ReportsAtTheHole(
        string expression,
        string parameter,
        string format,
        string type
    )
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            Body(parameter, expression)
        );

        var report = UnsupportedFeatures(diagnostics).Single();

        await Assert
            .That(report.Message)
            .IsEqualTo(
                $"The format '{format}' is not supported for '{type}' in TypeScript; "
                    + "the hole throws at run time."
            );

        await Assert.That(report.Severity).IsEqualTo(MetanoDiagnosticSeverity.Warning);
        await Assert.That(LocatedSource.TextOf(report)).IsEqualTo(expression[2..^1]);
        await Assert.That(files["holes.ts"]).Contains(Placeholder);
    }

    /// <summary>
    /// A letter .NET rejects for the type is not reported: the runtime throws
    /// the same <c>FormatException</c> .NET throws.
    /// </summary>
    [Test]
    [Arguments("$\"{d:D}\"", "double d", "formatDouble(d, \"D\")")]
    [Arguments("$\"{i:Q}\"", "int i", "formatInteger(i, 32, \"Q\")")]
    public async Task InvalidFormat_IsLeftToTheRuntime(
        string expression,
        string parameter,
        string lowered
    )
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            Body(parameter, expression)
        );

        await Assert.That(UnsupportedFeatures(diagnostics)).IsEmpty();
        await Assert.That(files["holes.ts"]).Contains(lowered);
    }

    [Test]
    [Arguments(
        "d.ToString(\"0.00\")",
        "double d",
        "\"0.00\"",
        "The format '0.00' is not supported for 'double' in TypeScript; the call throws at run time."
    )]
    [Arguments(
        "f.ToString(\"F2\")",
        "float f",
        "\"F2\"",
        "The format 'F2' is not supported for 'float' in TypeScript; the call throws at run time."
    )]
    [Arguments(
        "i.ToString(\"B\")",
        "int i",
        "\"B\"",
        "The format 'B' is not supported for 'int' in TypeScript; the call throws at run time."
    )]
    [Arguments(
        "i.ToString(format)",
        "int i, string format",
        "format",
        "A format that is not a constant is not supported for 'int' in TypeScript; the call throws at run time."
    )]
    [Arguments(
        "d.ToString(format, null)",
        "double d, string format",
        "format",
        "A format that is not a constant is not supported for 'double' in TypeScript; the call throws at run time."
    )]
    public async Task UnsupportedToStringFormat_ReportsAtTheFormat(
        string expression,
        string parameter,
        string argument,
        string message
    )
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            Body(parameter, expression)
        );

        var report = UnsupportedFeatures(diagnostics).Single();

        await Assert.That(report.Message).IsEqualTo(message);
        await Assert.That(LocatedSource.TextOf(report)).IsEqualTo(argument);
        await Assert.That(files["holes.ts"]).Contains(Placeholder);
    }

    [Test]
    [Arguments("$\"{i:D2}\"", "int i")]
    [Arguments("$\"{i,8}\"", "int i")]
    [Arguments("$\"{d,-8:F2}\"", "double d")]
    [Arguments("$\"{l:X}\"", "long l")]
    [Arguments("$\"{s,4}\"", "string s")]
    public async Task Dart_ReportsEveryClause(string expression, string parameter)
    {
        var (files, diagnostics) = DartTranspileHelper.Transpile(Body(parameter, expression));

        var report = UnsupportedFeatures(diagnostics).Single();

        await Assert
            .That(report.Message)
            .IsEqualTo(
                "Format and alignment clauses of an interpolation hole are not supported in "
                    + "Dart; the hole throws at run time."
            );

        await Assert.That(LocatedSource.TextOf(report)).IsEqualTo(expression[2..^1]);

        await Assert
            .That(files["holes.dart"])
            .Contains("'${(throw UnsupportedError('Unsupported expression: UnsupportedFormat'))}'");
    }

    [Test]
    public async Task Dart_ReportsANumericToStringFormat()
    {
        var (files, diagnostics) = DartTranspileHelper.Transpile(
            Body("int i", "i.ToString(\"D2\")")
        );

        var report = UnsupportedFeatures(diagnostics).Single();

        await Assert
            .That(report.Message)
            .IsEqualTo(
                "Numeric format strings are not supported in Dart; the call throws at run time."
            );

        await Assert
            .That(files["holes.dart"])
            .Contains("(throw UnsupportedError('Unsupported expression: UnsupportedFormat'))");
    }

    [Test]
    public async Task Dart_LeavesHolesWithoutClausesUnchanged()
    {
        var (files, diagnostics) = DartTranspileHelper.Transpile(
            Body("int i, string s", "$\"{i} {s:X}\"")
        );

        await Assert.That(UnsupportedFeatures(diagnostics)).IsEmpty();
        await Assert.That(files["holes.dart"]).Contains("'${i} ${s}'");
    }

    private static string Body(string parameters, string expression) =>
        Host
        + $$"""
                public string Run({{parameters}}) => {{expression}};
            }
            """;

    private static IEnumerable<MetanoDiagnostic> UnsupportedFeatures(
        IEnumerable<MetanoDiagnostic> diagnostics
    ) => diagnostics.Where(diagnostic => diagnostic.Code == DiagnosticCodes.UnsupportedFeature);
}
