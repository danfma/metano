using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// <c>Js.Code</c> inline TypeScript (spec 011 FR-031 – FR-034): constant text is
/// emitted verbatim, holes are transpiled and spliced as operands, the expression
/// form is <c>(text) as T</c> / <c>(text) as any</c>, the statement form ends with
/// <c>;</c>, holes pull their imports, and the diagnostics MS0051, MS0052 and MS0056.
/// </summary>
public class InlineCodeTranspileTests
{
    [Test]
    public async Task ExpressionPosition_WrapsTheTextAndSplicesHolesAsOperands()
    {
        var result = TranspileHelper.Transpile(
            """"
            using System;
            using Metano.Interop;

            [Transpile]
            public class Clock
            {
                private const string Selector = "#app";

                public double Now() => Js.Code<double>("performance.now()");

                public object Config() => Js.Code("(globalThis as any).__config");

                public string Sum(double a, double b) => Js.Code<string>($"{a + b}.toFixed(2)");

                public string One() => Js.Code<string>($"{1}.toString()");

                public int Applied() => Js.Code<int>($"{(Func<int, int>)(x => x + 1)}(2)");

                public string? Found() => Js.Code<string?>($"document.querySelector({Selector})?.id");

                public int Braces(int a) => Js.Code<int>($$"""{ value: {{a}} }.value""");

                public int Escaped(int a) => Js.Code<int>($"[{{ v: {a} }}][0].v");

                public string Member(Clock other) => Js.Code<string>($"{other.One()}.trim()");
            }
            """"
        );

        await TranspileHelper.AssertGolden("inline-code-expression.ts", result["clock.ts"]);
    }

    [Test]
    public async Task StatementPosition_EmitsStatementTextEndedBySemicolon()
    {
        var result = TranspileHelper.Transpile(
            """
            using System;
            using Metano.Interop;

            [Transpile]
            public class Effects
            {
                public void Log(string message)
                {
                    Js.Code($"console.log({message})");
                    Js.Code("const a = 1; console.log(a);");
                    Js.Code("if ((globalThis as any).x) { console.log(1) }");
                    Js.Code("console.log(2) // a trailing comment");
                    Js.Code<int>("void 0");
                }

                public void Arrow() => Js.Code("console.log('arrow')");

                public Action Callback() => () => Js.Code("console.log('callback')");
            }
            """
        );

        await TranspileHelper.AssertGolden("inline-code-statement.ts", result["effects.ts"]);
    }

    [Test]
    public async Task Holes_PullTheImportsOfWhatTheyReference()
    {
        var result = TranspileHelper.Transpile(
            """
            using System;
            using Metano.Annotations.TypeScript;
            using Metano.Interop;

            [NoContainer]
            public static class Inferno
            {
                [Import(name: "createElement", from: "inferno-create-element"), Name("createElement"), External]
                public static object CreateElement(string tag) => throw new NotSupportedException();
            }

            [Transpile]
            public record Point(int X, int Y);

            [Transpile]
            public class View
            {
                public object Div() => Js.Code($"{Inferno.CreateElement}('div')");

                public string Text() => Js.Code<string>($"JSON.stringify({new Point(1, 2)})");

                public object Imported() => Js.Code("createElement('span')");

                public Func<string, object> Group() => Inferno.CreateElement;
            }
            """
        );

        await TranspileHelper.AssertGolden("inline-code-holes-import.ts", result["view.ts"]);
    }

    [Test]
    public async Task OnlyHolesPullImports_TheTextStaysOpaque()
    {
        var result = TranspileHelper.Transpile(
            """
            using System;
            using Metano.Annotations.TypeScript;
            using Metano.Interop;

            [NoContainer]
            public static class Inferno
            {
                [Import(name: "createElement", from: "inferno-create-element"), Name("createElement"), External]
                public static object CreateElement(string tag) => throw new NotSupportedException();
            }

            [Transpile]
            public class ByHole
            {
                public object Div() => Js.Code($"{Inferno.CreateElement}('div')");
            }

            [Transpile]
            public class ByText
            {
                public object Span() => Js.Code("createElement('span')");
            }
            """
        );

        await Assert
            .That(result["by-hole.ts"])
            .Contains("import { createElement } from \"inferno-create-element\";");

        await Assert.That(result["by-text.ts"]).DoesNotContain("import");
    }

    [Test]
    public async Task Holes_AreRewrittenLikeAnyOtherBodyExpression()
    {
        var result = TranspileHelper.Transpile(
            """
            using Metano.Interop;

            [Transpile]
            public class Greeter(string name)
            {
                public string Greet() => Js.Code<string>($"'hi ' + {name}");
            }

            [Transpile, PlainObject]
            public record Size(int Width, int Height)
            {
                public int Area() => Js.Code<int>($"{Width} * {this.Height}");
            }
            """
        );

        await Assert.That(result["greeter.ts"]).Contains("('hi ' + this._name) as string");
        await Assert.That(result["size.ts"]).DoesNotContain("this.");
    }

    [Test]
    public async Task HoleInsideAJsStringLiteral_WarnsMs0056AndStillEmits()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using Metano.Interop;

            [Transpile]
            public class Greeter
            {
                public string Single(string name) => Js.Code<string>($"'hello {name}'");

                public string Double(string name) => Js.Code<string>($"\"hello {name}\"");

                public string Template(string name) => Js.Code<string>($"`hello {name}`");

                public string Substitution(string name) => Js.Code<string>($"`hello ${{{name}}}`");

                public string Comment(string name) => Js.Code<string>($"/* 'x' */ {name}");
            }
            """
        );

        var warnings = diagnostics
            .Where(d => d.Code == DiagnosticCodes.InlineCodeHoleInStringLiteral)
            .ToList();

        await Assert.That(warnings.Count).IsEqualTo(3);

        await Assert
            .That(warnings.All(d => d.Severity == MetanoDiagnosticSeverity.Warning))
            .IsTrue();

        await Assert
            .That(warnings.Select(d => d.Location!.GetLineSpan().StartLinePosition.Line))
            .IsEquivalentTo([8, 10, 12]);

        await TranspileHelper.AssertGolden(
            "inline-code-string-literal-hole.ts",
            files["greeter.ts"]
        );
    }

    [Test]
    [Arguments("Js.Code(text)", "public string F(string text) => {0};")]
    [Arguments("Js.Code(\"a\" + $\"{x}\")", "public string F(int x) => {0};")]
    [Arguments("Js.Code(null!)", "public string F() => {0};")]
    [Arguments("Js.Code(d)", "public string F(dynamic d) => {0};")]
    public async Task NonConstantArgument_ReportsMs0052(string call, string member)
    {
        var diagnostics = InlineCodeErrors(member.Replace("{0}", call));

        await Assert.That(diagnostics.Count).IsEqualTo(1);
        await Assert.That(diagnostics[0].Severity).IsEqualTo(MetanoDiagnosticSeverity.Error);
        await Assert.That(LocatedText(diagnostics[0])).IsEqualTo(call["Js.Code(".Length..^1]);
    }

    [Test]
    [Arguments("{x,4}")]
    [Arguments("{x:F2}")]
    [Arguments("{await Task.FromResult(x)}")]
    [Arguments("{Parse(out var y)}")]
    public async Task InvalidHole_ReportsMs0052AtTheHole(string hole)
    {
        var diagnostics = InlineCodeErrors(
            "public async Task<string> F(int x) => Js.Code<string>($\"f("
                + hole
                + ")\");\n"
                + "private static int Parse(out int value) { value = 1; return value; }"
        );

        await Assert.That(diagnostics.Count).IsEqualTo(1);
        await Assert.That(LocatedText(diagnostics[0])).IsEqualTo(hole);
    }

    [Test]
    public async Task JsCodeMethodGroup_ReportsMs0052()
    {
        var diagnostics = InlineCodeErrors("public Func<string, dynamic> F() => Js.Code;");

        await Assert.That(diagnostics.Count).IsEqualTo(1);
        await Assert.That(LocatedText(diagnostics[0])).IsEqualTo("Js.Code");
    }

    [Test]
    public async Task JsCodeInADartEmission_ReportsMs0051Once()
    {
        var diagnostics = DartFrontendDiagnostics(
            """
            using static Metano.Interop.Js;

            [Transpile]
            public class Clock
            {
                public double Now() => Metano.Interop.Js.Code<double>("performance.now()");

                public void Log() => Code("console.log(1)");
            }
            """
        );

        var errors = diagnostics
            .Where(d => d.Code == DiagnosticCodes.TargetSpecificInterop)
            .ToList();

        await Assert.That(errors.Count).IsEqualTo(2);
        await Assert.That(errors.All(d => d.Severity == MetanoDiagnosticSeverity.Error)).IsTrue();

        await Assert
            .That(errors.Select(LocatedText))
            .IsEquivalentTo(["Metano.Interop.Js.Code<double>", "Code"]);
    }

    [Test]
    public async Task JsCodeInAMemberIgnoredForDart_ReportsNothing()
    {
        const string source = """
            using Metano.Interop;

            [Transpile]
            public class Clock
            {
                [Ignore(TargetLanguage.Dart)]
                public double Now() => Js.Code<double>("performance.now()");

                public int Ticks() => 1;
            }
            """;
        var (files, transformerDiagnostics) = DartTranspileHelper.Transpile(source);
        var diagnostics = transformerDiagnostics.Concat(DartFrontendDiagnostics(source));

        await Assert
            .That(
                diagnostics.Where(d =>
                    d.Code
                        is DiagnosticCodes.TargetSpecificInterop
                            or DiagnosticCodes.UnsupportedFeature
                )
            )
            .IsEmpty();

        await Assert.That(files["clock.dart"]).DoesNotContain("now");
    }

    [Test]
    public async Task JsCodeReachingTheDartPrinter_ReportsMs0001AndThrows()
    {
        var (files, diagnostics) = DartTranspileHelper.Transpile(
            """
            using Metano.Interop;

            [Transpile]
            public class Clock
            {
                public double Now() => Js.Code<double>("performance.now()");

                public void Log() { Js.Code("console.log(1)"); }
            }
            """
        );

        await Assert
            .That(diagnostics.Count(d => d.Code == DiagnosticCodes.UnsupportedFeature))
            .IsEqualTo(2);

        await Assert.That(files["clock.dart"]).Contains("Unsupported expression: InlineCode");
    }

    [Test]
    public async Task EscapeHatchesInDart_AreCasts()
    {
        var (files, diagnostics) = DartTranspileHelper.Transpile(
            """
            using Metano.Interop;

            [Transpile]
            public class Shelf
            {
                public string Name(object? a, object b) => (a ?? b).UnsafeCast<string>();

                public object Raw(object a) => a.AsDynamic();
            }
            """
        );

        await Assert.That(diagnostics).IsEmpty();
        await Assert.That(files["shelf.dart"]).Contains("(a ?? b) as String");
        await Assert.That(files["shelf.dart"]).Contains("a as dynamic");
    }

    [Test]
    public async Task JsCodeInATypeScriptEmission_ReportsNoMs0051()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using Metano.Interop;

            [Transpile]
            public class Clock
            {
                public double Now() => Js.Code<double>("performance.now()");
            }
            """
        );

        await Assert
            .That(diagnostics.Where(d => d.Code == DiagnosticCodes.TargetSpecificInterop))
            .IsEmpty();
    }

    /// <summary>The frontend diagnostics of a Dart emission, where MS0051 is reported.</summary>
    private static IReadOnlyList<MetanoDiagnostic> DartFrontendDiagnostics(string source)
    {
        var tree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(
            "using Metano.Annotations;\n" + source,
            new Microsoft.CodeAnalysis.CSharp.CSharpParseOptions(
                Microsoft.CodeAnalysis.CSharp.LanguageVersion.Preview
            )
        );

        var compilation = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create(
            "DartTestAssembly",
            [tree],
            TranspileHelper.BaseReferences,
            new Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions(
                Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary
            )
        );

        return new Metano.Compiler.CSharpSourceFrontend()
            .ExtractFromCompilation(compilation, Metano.Annotations.TargetLanguage.Dart)
            .Diagnostics;
    }

    private static List<MetanoDiagnostic> InlineCodeErrors(string member)
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            "using System;\nusing System.Threading.Tasks;\nusing Metano.Interop;\n\n"
                + "[Transpile]\npublic class Probe\n{\n"
                + member
                + "\n}\n"
        );

        return [.. diagnostics.Where(d => d.Code == DiagnosticCodes.InvalidInlineCode)];
    }

    private static string LocatedText(MetanoDiagnostic diagnostic)
    {
        var location = diagnostic.Location!;

        return location
            .SourceTree!.ToString()
            .Substring(location.SourceSpan.Start, location.SourceSpan.Length);
    }
}
