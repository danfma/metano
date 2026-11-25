using Metano.Annotations;
using Metano.Compiler;
using Metano.Compiler.Dart;
using Metano.Compiler.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Metano.Tests;

/// <summary>
/// Compiles inline C# and drives the real <see cref="DartTarget"/>
/// end-to-end, returning <c>filename → Dart source</c> plus the
/// target's diagnostics. The Dart counterpart of
/// <see cref="TranspileHelper.Transpile"/>.
/// </summary>
public static class DartTranspileHelper
{
    public static (
        Dictionary<string, string> Files,
        IReadOnlyList<MetanoDiagnostic> Diagnostics
    ) Transpile(string csharpSource)
    {
        var compilation = Compile(csharpSource);

        var ir = new CSharpSourceFrontend().ExtractFromCompilation(
            compilation,
            TargetLanguage.Dart
        );

        // Through the target, so the diagnostics the body printer reports
        // (W9-13 placeholders) are included.
        var output = new DartTarget().Transform(ir, compilation);

        return (output.Files.ToDictionary(f => f.RelativePath, f => f.Content), output.Diagnostics);
    }

    /// <summary>
    /// The diagnostics the frontend raises for a Dart run (the declaration
    /// validators, such as the interim union rejection), which
    /// <see cref="Transpile"/> leaves out because it returns the transformer's.
    /// </summary>
    public static IReadOnlyList<MetanoDiagnostic> FrontendDiagnostics(string csharpSource) =>
        new CSharpSourceFrontend()
            .ExtractFromCompilation(Compile(csharpSource), TargetLanguage.Dart)
            .Diagnostics;

    /// <summary>
    /// <paramref name="diagnostics"/> without the error the Dart body printer
    /// reports for <paramref name="knownGap"/> (a construct it has no Dart
    /// form for yet, W9-13), for a test whose subject is another part of the
    /// same source.
    /// </summary>
    public static List<MetanoDiagnostic> ExceptPrinterGap(
        IEnumerable<MetanoDiagnostic> diagnostics,
        string knownGap
    ) =>
        [
            .. diagnostics.Where(d =>
                !d.Message.StartsWith(knownGap, StringComparison.Ordinal)
                || !d.Message.EndsWith(
                    "is not supported by the Dart target.",
                    StringComparison.Ordinal
                )
            ),
        ];

    private static CSharpCompilation Compile(string csharpSource)
    {
        var source = $"""
            using System;
            using Metano.Annotations;
            {csharpSource}
            """;

        var syntaxTree = CSharpSyntaxTree.ParseText(
            source,
            new CSharpParseOptions(LanguageVersion.Preview)
        );

        // Share the same cached metadata references that TranspileHelper uses —
        // rebuilding the ~200-entry list per test would add hundreds of MB of
        // churn across the suite.
        var compilation = CSharpCompilation.Create(
            "DartTestAssembly",
            [syntaxTree],
            TranspileHelper.BaseReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );

        var errors = compilation
            .GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        if (errors.Count > 0)
            throw new InvalidOperationException(
                "C# compilation failed:\n" + string.Join("\n", errors.Select(e => e.ToString()))
            );

        return compilation;
    }
}
