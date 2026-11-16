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

        [Transpile, StringEnum]
        public enum Mode { Fast }

        [Transpile]
        public sealed class Plain { public override string ToString() => "plain"; }

        [Transpile]
        public record struct Pair(int A);

        [Transpile]
        public sealed class Money : IFormattable
        {
            public string ToString(string? format, IFormatProvider? provider) => "money";

            public string ToString(string format) => "money:" + format;
        }

        [Transpile]
        public class Holes
        {
            private int _count;

            private int Next() => ++_count;

            private string Pick() => _count > 1 ? "D2" : "X";

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
                public string Show(int i, long l, int? n, string s) => $"{i} {l} {n} {s}";
            }
            """
        );

        await Assert.That(files["plain.ts"]).Contains("return `${i} ${l} ${n ?? \"\"} ${s}`;");

        await Assert.That(files["plain.ts"]).DoesNotContain("metano-runtime");
    }

    /// <summary>
    /// A hole over a <c>bool</c>, <c>double</c> or <c>float</c> without a clause prints .NET's
    /// default text through the runtime function (baseline FR-014, #308); the others keep the
    /// native conversion.
    /// </summary>
    [Test]
    public async Task HolesOverBoolAndFloatingPoint_PrintDotNetText()
    {
        var files = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Plain
            {
                public string Show(double d, bool b, float f) => $"{d} {b} {f}";
            }
            """
        );

        await Assert
            .That(files["plain.ts"])
            .Contains("return `${formatDouble(d)} ${formatBoolean(b)} ${formatSingle(f)}`;");
    }

    [Test]
    [Arguments("$\"{m:F2}\"", "decimal m", "F2", "decimal")]
    [Arguments("$\"{f:F2}\"", "float f", "F2", "float")]
    [Arguments("$\"{t:yyyy-MM-dd}\"", "DateTime t", "yyyy-MM-dd", "DateTime")]
    [Arguments("$\"{t:yyyy-mm-dd}\"", "DateOnly t", "yyyy-mm-dd", "DateOnly")]
    [Arguments("$\"{g:N}\"", "Guid? g", "N", "Guid?")]
    [Arguments("$\"{c:X}\"", "Color c", "X", "Color")]
    [Arguments("$\"{h:G}\"", "Half h", "G", "Half")]
    [Arguments("$\"{v:D2}\"", "IComparable v", "D2", "IComparable")]
    [Arguments("$\"{v:D2}\"", "IComparable? v", "D2", "IComparable")]
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
        "f.ToString(format)",
        "float f, string format",
        "format",
        "Format strings are not supported for 'float' in TypeScript; the call throws at run time."
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

    /// <summary>
    /// A format known only at run time reaches the formatter, as
    /// <c>formatInt64</c> does for <c>long</c>: it throws
    /// <c>FormatException</c> or <c>NotSupportedException</c> for a format it
    /// does not implement and formats a <c>null</c> one as <c>G</c>. The
    /// receiver and the format are each evaluated once, in C# order.
    /// </summary>
    [Test]
    [Arguments("i.ToString(format)", "int i, string format", "formatInteger(i, 32, format)")]
    [Arguments("b.ToString(format)", "byte b, string? format", "formatInteger(b, 8, format)")]
    [Arguments("s.ToString(format)", "short s, string format", "formatInteger(s, 16, format)")]
    [Arguments("d.ToString(format, null)", "double d, string format", "formatDouble(d, format)")]
    [Arguments(
        "Next().ToString(Pick())",
        "int unused",
        "formatInteger(this.next(), 32, this.pick())"
    )]
    public async Task RunTimeFormat_CallsTheFormatter(
        string expression,
        string parameter,
        string lowered
    )
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            Body(parameter, expression)
        );

        await Assert.That(UnsupportedFeatures(diagnostics)).IsEmpty();
        await Assert.That(files["holes.ts"]).Contains($"return {lowered};");
    }

    [Test]
    public async Task Dart_ReportsARunTimeFormat()
    {
        var (_, diagnostics) = DartTranspileHelper.Transpile(
            Body("int i, string format", "i.ToString(format)")
        );

        await Assert
            .That(UnsupportedFeatures(diagnostics).Single().Message)
            .IsEqualTo("Format strings are not supported in Dart; the call throws at run time.");
    }

    /// <summary>
    /// C# passes a format only to an <c>IFormattable</c>: on any other static
    /// type (a sealed class, a record struct) the format is ignored and the
    /// hole prints <c>ToString()</c>, as on main; an alignment still pads.
    /// </summary>
    [Test]
    [Arguments("$\"{p:X}\"", "Plain p", "return `${p}`;")]
    [Arguments("$\"{p:D2}\"", "Pair p", "return `${p}`;")]
    [Arguments("$\"{p,6:X}\"", "Plain p", "return `${alignValue(p, 6)}`;")]
    public async Task FormatOnANonFormattableType_IsIgnored(
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

    /// <summary>
    /// A format the target prints as the clause-free text for every value
    /// (<c>IFormatStringCapability.PrintsAsDefaultText</c>) keeps the
    /// clause-free hole, as on main; the call becomes the same hole.
    /// </summary>
    [Test]
    [Arguments("$\"{c:D}\"", "Color c", "return `${c}`;")]
    [Arguments("$\"{m:G}\"", "Mode m", "return `${m}`;")]
    [Arguments("$\"{d:O}\"", "DateOnly d", "return `${d}`;")]
    [Arguments("$\"{d:yyyy-MM-dd}\"", "DateOnly d", "return `${d}`;")]
    [Arguments("d.ToString(\"yyyy-MM-dd\")", "DateOnly d", "return `${d}`;")]
    [Arguments("$\"{g:D}\"", "Guid g", "return `${g}`;")]
    [Arguments("$\"{g:D}\"", "Guid? g", "return `${g ?? \"\"}`;")]
    [Arguments("$\"{b:R}\"", "System.Numerics.BigInteger b", "return `${b}`;")]
    [Arguments("$\"[{c,4:D}]\"", "Color c", "return `[${alignValue(c, 4)}]`;")]
    [Arguments("c.ToString(\"D\")", "Color c", "return `${c}`;")]
    [Arguments("d.ToString(\"O\", null)", "DateOnly d", "return `${d}`;")]
    public async Task DefaultTextFormat_KeepsTheClauseFreeHole(
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

    /// <summary>
    /// A hole is <c>value.ToString(format)</c>: on a type whose
    /// <c>ToString(string)</c> a declarative mapping lowers (<c>Guid</c>), the
    /// hole lowers through the same mapping as the call.
    /// </summary>
    [Test]
    [Arguments("$\"{g:N}\"", "g.ToString(\"N\")")]
    [Arguments("$\"{g:B}\"", "g.ToString(\"B\")")]
    [Arguments("$\"{g:p}\"", "g.ToString(\"p\")")]
    public async Task HoleOnAMappedToString_LowersThroughTheMapping(string hole, string call)
    {
        var holeOutput = TranspileHelper.Transpile(Body("Guid g", hole))["holes.ts"];
        var callOutput = TranspileHelper.Transpile(Body("Guid g", call))["holes.ts"];

        await Assert
            .That(ReturnedExpressionOf(holeOutput))
            .IsEqualTo($"`${{{ReturnedExpressionOf(callOutput)}}}`");
    }

    /// <summary>
    /// <c>ToString(format)</c> of a BCL type that no mapping and no formatter
    /// lowers reports at the format, instead of keeping a call that throws
    /// or prints the wrong text; <c>nint</c> calls <c>formatInt64</c> as
    /// <c>long</c> does; a user type's own <c>ToString(string)</c> is untouched.
    /// </summary>
    [Test]
    [Arguments("m.ToString(\"F2\")", "decimal m", "\"F2\"", "decimal")]
    [Arguments("t.ToString(\"O\")", "DateTime t", "\"O\"", "DateTime")]
    [Arguments("c.ToString(\"G\")", "Color c", "\"G\"", "Color")]
    [Arguments("b.ToString(\"X\")", "System.Numerics.BigInteger b", "\"X\"", "BigInteger")]
    [Arguments("s.ToString(\"c\")", "TimeSpan s", "\"c\"", "TimeSpan")]
    public async Task BclToStringWithoutLowering_ReportsAtTheFormat(
        string expression,
        string parameter,
        string argument,
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
                $"The format '{argument[1..^1]}' is not supported for '{type}' in TypeScript; "
                    + "the call throws at run time."
            );

        await Assert.That(LocatedSource.TextOf(report)).IsEqualTo(argument);
        await Assert.That(files["holes.ts"]).Contains(Placeholder);
    }

    [Test]
    public async Task FormattableTypeParameterToString_Reports()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            Host
                + """
                    public string Run<T>(T v) where T : IFormattable => v.ToString("D2", null);
                }
                """
        );

        await Assert
            .That(UnsupportedFeatures(diagnostics).Single().Message)
            .IsEqualTo(
                "The format 'D2' is not supported for 'T' in TypeScript; the call throws at run time."
            );
    }

    [Test]
    [Arguments("n.ToString(\"D2\")", "nint n", "return formatInt64(n, \"D2\");")]
    [Arguments("n.ToString(format)", "nuint n, string format", "return formatInt64(n, format);")]
    [Arguments("m.ToString(\"X\")", "Money m", "return m.toStringFormat(\"X\");")]
    public async Task LoweredToStringWithFormat_IsNotReported(
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

    /// <summary>
    /// A hole and the call take one decision order (formatter, mapping,
    /// default text, report), so for every default-text fact and every mapped
    /// format of the TypeScript target they print the same text: the hole is
    /// the call's expression in a template literal, or both are the same
    /// clause-free hole.
    /// </summary>
    [Test]
    [Arguments("Guid g", "g", "D")]
    [Arguments("Guid g", "g", "d")]
    [Arguments("Guid g", "g", "N")]
    [Arguments("Guid g", "g", "n")]
    [Arguments("Guid g", "g", "B")]
    [Arguments("Guid g", "g", "b")]
    [Arguments("Guid g", "g", "P")]
    [Arguments("Guid g", "g", "p")]
    [Arguments("Color c", "c", "D")]
    [Arguments("Color c", "c", "d")]
    [Arguments("Mode m", "m", "G")]
    [Arguments("Mode m", "m", "g")]
    [Arguments("Mode m", "m", "F")]
    [Arguments("Mode m", "m", "f")]
    [Arguments("DateOnly d", "d", "O")]
    [Arguments("DateOnly d", "d", "o")]
    [Arguments("DateOnly d", "d", "yyyy-MM-dd")]
    [Arguments("System.Numerics.BigInteger b", "b", "G")]
    [Arguments("System.Numerics.BigInteger b", "b", "r")]
    [Arguments("System.Numerics.BigInteger b", "b", "D")]
    [Arguments("System.Numerics.BigInteger b", "b", "d")]
    public async Task HoleAndCall_PrintTheSameText(string parameter, string value, string format)
    {
        var (holeFiles, holeDiagnostics) = TranspileHelper.TranspileWithDiagnostics(
            Body(parameter, $"$\"{{{value}:{format}}}\"")
        );

        var (callFiles, callDiagnostics) = TranspileHelper.TranspileWithDiagnostics(
            Body(parameter, $"{value}.ToString(\"{format}\")")
        );

        var hole = ReturnedExpressionOf(holeFiles["holes.ts"]);
        var call = ReturnedExpressionOf(callFiles["holes.ts"]);

        await Assert.That(UnsupportedFeatures(holeDiagnostics)).IsEmpty();
        await Assert.That(UnsupportedFeatures(callDiagnostics)).IsEmpty();
        await Assert.That(hole == call || hole == $"`${{{call}}}`").IsTrue();
    }

    /// <summary>
    /// <c>BigInteger</c>'s <c>D</c> with a precision pads, which the
    /// clause-free text does not: it keeps reporting.
    /// </summary>
    [Test]
    public async Task BigIntegerPaddedDecimal_Reports()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            Body("System.Numerics.BigInteger b", "$\"{b:D3}\"")
        );

        await Assert.That(UnsupportedFeatures(diagnostics).Count()).IsEqualTo(1);
    }

    /// <summary>
    /// A <c>DateOnly</c> prints the ISO text only under <c>O</c> / <c>o</c>
    /// and the ISO pattern; <c>d</c> (the culture's short date, its default
    /// format) and <c>D</c> are not that text, so they keep reporting (#677:
    /// the default format must not be folded to upper case to say so).
    /// </summary>
    [Test]
    [Arguments("$\"{d:d}\"")]
    [Arguments("$\"{d:D}\"")]
    [Arguments("d.ToString(\"d\")")]
    public async Task DateOnlyShortAndLongDate_Report(string expression)
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            Body("DateOnly d", expression)
        );

        await Assert.That(UnsupportedFeatures(diagnostics).Count()).IsEqualTo(1);
    }

    /// <summary>
    /// A static <c>ToString</c> with a leading <c>string</c> parameter
    /// (<c>Convert.ToString(string)</c>) is not a format call: it keeps the
    /// unmapped-call report it had before.
    /// </summary>
    [Test]
    public async Task StaticToString_IsNotAFormatCall()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            Body("string s", "Convert.ToString(s)")
        );

        var report = UnsupportedFeatures(diagnostics).Single();

        await Assert.That(report.Message).Contains("has no declarative mapping");
        await Assert.That(report.Message).DoesNotContain("The format");
    }

    /// <summary>
    /// A conditional-access call names the receiver's type, not the type that
    /// declares <c>ToString</c>.
    /// </summary>
    [Test]
    public async Task ConditionalAccessToString_NamesTheReceiverType()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            Body("Color? c", "c?.ToString(\"G\") ?? \"\"")
        );

        await Assert
            .That(UnsupportedFeatures(diagnostics).Single().Message)
            .IsEqualTo(
                "The format 'G' is not supported for 'Color' in TypeScript; the call throws at run time."
            );
    }

    /// <summary>
    /// A transpiled library referenced as a compiled image (the build's
    /// <c>&lt;Reference HintPath&gt;</c> shape) keeps its own
    /// <c>ToString(string)</c> and <c>IFormattable.ToString</c>: the calls are
    /// its methods, emitted with the type's import, never governed by the
    /// BCL format rule, whatever the library's assembly name.
    /// </summary>
    [Test]
    [Arguments("TestLibrary")]
    [Arguments("System.Acme")]
    public async Task TranspiledLibraryToString_IsNotGoverned(string libraryAssemblyName)
    {
        var files = TranspileHelper.TranspileWithLibraryImage(
            """
            using System;
            using Metano.Annotations;

            [assembly: TranspileAssembly]
            [assembly: EmitPackage("shop-lib")]

            namespace Shop;

            public sealed class Price
            {
                public string ToString(string format) => format;
            }

            public sealed class Fmt : IFormattable
            {
                public string ToString(string? format, IFormatProvider? provider) => "fmt";
            }
            """,
            """
            [Transpile]
            public class Till
            {
                public string Run(Shop.Price p, Shop.Fmt f) => p.ToString("X") + f.ToString("Y", null);
            }
            """,
            libraryAssemblyName: libraryAssemblyName
        );

        var till = files["till.ts"];

        await Assert.That(till).Contains("from \"shop-lib");
        await Assert.That(till).Contains("Price");
        await Assert.That(till).Contains("Fmt");
        await Assert.That(till).DoesNotContain("Unsupported expression");
    }

    /// <summary>
    /// The project's own <c>ToString(string format)</c> is never governed,
    /// even when its assembly name starts with <c>System.</c>: an assembly
    /// compiled from source is not base class library.
    /// </summary>
    [Test]
    public async Task ProjectNamedLikeTheBcl_KeepsItsOwnToString()
    {
        var directory = Directory
            .CreateTempSubdirectory("metano-323-")
            .CreateSubdirectory("System.MyApp");

        try
        {
            File.WriteAllText(
                Path.Combine(directory.FullName, "Price.cs"),
                """
                using Metano.Annotations;

                [Transpile]
                public sealed class Price
                {
                    public string ToString(string format) => format;

                    public string Label() => ToString("X") + this.ToString("Y");
                }
                """
            );

            var (files, diagnostics) = TranspileHelper.TranspileSourceDirectory(directory.FullName);

            await Assert.That(UnsupportedFeatures(diagnostics)).IsEmpty();
            await Assert.That(files["price.ts"]).DoesNotContain("Unsupported expression");
        }
        finally
        {
            directory.Parent!.Delete(recursive: true);
        }
    }

    /// <summary>
    /// A <c>?.</c> call takes the same steps as the plain one (mapping,
    /// default text) on the bound receiver, and an empty or <c>null</c>
    /// format is the type's own default format, as in .NET.
    /// </summary>
    [Test]
    [Arguments("Guid? g", "g?.ToString(\"N\")", "return g == null ? null : g.replace(/-/g, \"\");")]
    [Arguments("Guid? g", "g?.ToString(\"D\")", "return g == null ? null : g;")]
    [Arguments(
        "System.Numerics.BigInteger? b",
        "b?.ToString(\"D\")",
        "return b == null ? null : `${b}`;"
    )]
    [Arguments("DateOnly? d", "d?.ToString(\"O\")", "return d == null ? null : `${d}`;")]
    [Arguments("DateOnly? d", "d?.ToString(\"yyyy-MM-dd\")", "return d == null ? null : `${d}`;")]
    [Arguments("Mode? m", "m?.ToString(\"G\")", "return m == null ? null : `${m}`;")]
    [Arguments("Mode? m", "m?.ToString(\"F\")", "return m == null ? null : `${m}`;")]
    [Arguments("Color? c", "c?.ToString(\"D\")", "return c == null ? null : `${c}`;")]
    [Arguments("Mode m", "m.ToString(\"\")", "return `${m}`;")]
    [Arguments("Mode m", "m.ToString((string?)null)", "return `${m}`;")]
    [Arguments("Guid g", "g.ToString(\"\")", "return g;")]
    [Arguments("System.Numerics.BigInteger b", "b.ToString(\"\")", "return `${b}`;")]
    public async Task ConditionalOrEmptyFormat_PrintsLikeThePlainCall(
        string parameter,
        string expression,
        string lowered
    )
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            Host
                + $$"""
                    public string? Run({{parameter}}) => {{expression}};
                }
                """
        );

        await Assert.That(UnsupportedFeatures(diagnostics)).IsEmpty();
        await Assert.That(files["holes.ts"]).Contains(lowered);
    }

    /// <summary>
    /// A <c>?.</c> call anywhere in a conditional chain (followed by
    /// <c>.Length</c>, <c>?.Length</c>, a method, or behind another
    /// <c>?.</c>) reads the bound receiver: a default-text or mapped format
    /// lowers as the plain call does, and a format no formatter takes reports
    /// at compile time, naming the receiver's type.
    /// </summary>
    [Test]
    [Arguments("Mode? m", "m?.ToString(\"G\").Length", null)]
    [Arguments("Mode? m", "m?.ToString(\"\").Length", null)]
    [Arguments("Mode? m", "m?.ToString(\"G\").ToUpper()", null)]
    [Arguments("Mode? m", "m?.ToString(\"G\")?.Length", null)]
    [Arguments("Hold h", "h?.NMode?.ToString(\"G\")?.ToLower()", null)]
    [Arguments("Guid? g", "g?.ToString(\"N\").Length", null)]
    [Arguments("Guid? g", "g?.ToString(\"N\")?.Length", null)]
    [Arguments(
        "int? n",
        "n?.ToString(\"0.00\")?.Length",
        "The format '0.00' is not supported for 'int'"
    )]
    [Arguments("int? n", "n?.ToString(\"B\").Length", "The format 'B' is not supported for 'int'")]
    [Arguments(
        "double? d",
        "d?.ToString(\"0.00\")?.Length",
        "The format '0.00' is not supported for 'double'"
    )]
    public async Task ConditionalChainToString_ReadsTheBoundReceiver(
        string parameter,
        string expression,
        string? report
    )
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            Host
                + $$"""
                    public object? Run({{parameter}}) => {{expression}};
                }

                [Transpile]
                public class Hold
                {
                    public Mode? NMode { get; set; }
                }
                """
        );

        var reports = UnsupportedFeatures(diagnostics).Select(d => d.Message).ToList();

        if (report is null)
        {
            await Assert.That(reports).IsEmpty();
            await Assert.That(files["holes.ts"]).DoesNotContain("Unsupported expression");

            return;
        }

        await Assert.That(reports.Single()).StartsWith(report);
    }

    /// <summary>
    /// An empty format is the type's default, so where that default is not
    /// the clause-free text (an enum's <c>G</c> holds the name, a
    /// <c>DateOnly</c>'s short date) it still reports.
    /// </summary>
    [Test]
    [Arguments("Color c", "c.ToString(\"\")", "Color")]
    [Arguments("DateOnly d", "d.ToString(\"\")", "DateOnly")]
    public async Task EmptyFormatWithoutDefaultText_Reports(
        string parameter,
        string expression,
        string type
    )
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            Body(parameter, expression)
        );

        await Assert
            .That(UnsupportedFeatures(diagnostics).Single().Message)
            .IsEqualTo(
                $"The format '' is not supported for '{type}' in TypeScript; the call throws at run time."
            );
    }

    /// <summary>
    /// A type from a referenced library (a compiled image, not a BCL
    /// assembly) keeps its own <c>ToString(string)</c>: the call is emitted
    /// as written, never governed by the BCL format rule.
    /// </summary>
    [Test]
    public async Task LibraryTypeToString_IsNotGoverned()
    {
        var files = TranspileHelper.TranspileWithLibraryImage(
            """
            namespace Vendor;

            public sealed class Ticket
            {
                public string ToString(string format) => format;
            }
            """,
            """
            [Transpile]
            public class Reader
            {
                public string Run(Vendor.Ticket t) => t.ToString("X");
            }
            """
        );

        await Assert.That(files["reader.ts"]).Contains("return t.toString(\"X\");");
        await Assert.That(files["reader.ts"]).DoesNotContain("Unsupported expression");
    }

    /// <summary>
    /// The 64-bit integers' formats are the capability's answer (MS0066 for
    /// an unsupported constant one, TODO(#662)), not a core exception: no
    /// MS0001 in TypeScript; Dart, which declares no capability, reports.
    /// </summary>
    [Test]
    [Arguments("l.ToString(\"B\")", "long l", 1)]
    [Arguments("u.ToString(\"X\")", "ulong u", 0)]
    [Arguments("l.ToString(format)", "long l, string format", 0)]
    [Arguments("$\"{l:#,##0}\"", "long l", 1)]
    [Arguments("$\"{u,8:N0}\"", "ulong u", 0)]
    public async Task SixtyFourBitFormats_KeepTheirDiagnostics(
        string expression,
        string parameter,
        int int64FormatReports
    )
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            Body(parameter, expression)
        );

        var (_, dartDiagnostics) = DartTranspileHelper.Transpile(Body(parameter, expression));

        await Assert.That(UnsupportedFeatures(diagnostics)).IsEmpty();

        await Assert
            .That(diagnostics.Count(d => d.Code == DiagnosticCodes.UnsupportedInt64Format))
            .IsEqualTo(int64FormatReports);

        await Assert.That(UnsupportedFeatures(dartDiagnostics).Count()).IsEqualTo(1);
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
            .IsEqualTo("Format strings are not supported in Dart; the call throws at run time.");

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

    private static string ReturnedExpressionOf(string file)
    {
        var start = file.LastIndexOf("return ", StringComparison.Ordinal) + "return ".Length;

        return file[start..file.IndexOf(';', start)];
    }

    private static IEnumerable<MetanoDiagnostic> UnsupportedFeatures(
        IEnumerable<MetanoDiagnostic> diagnostics
    ) => diagnostics.Where(diagnostic => diagnostic.Code == DiagnosticCodes.UnsupportedFeature);
}
