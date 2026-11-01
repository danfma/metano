using Metano.Annotations;
using Metano.Compiler;
using Metano.Compiler.Dart.Transformation;
using Metano.Compiler.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Metano.Tests;

/// <summary>
/// Compiles inline C# and drives the real <see cref="DartTransformer"/>
/// end-to-end, returning <c>filename → Dart source</c> plus the
/// transformer diagnostics. The Dart counterpart of
/// <see cref="TranspileHelper.Transpile"/>.
/// </summary>
public static class DartTranspileHelper
{
    public static (
        Dictionary<string, string> Files,
        IReadOnlyList<MetanoDiagnostic> Diagnostics
    ) Transpile(string csharpSource)
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

        var ir = new CSharpSourceFrontend().ExtractFromCompilation(
            compilation,
            TargetLanguage.Dart
        );
        var transformer = new DartTransformer(ir, compilation);
        var files = transformer.TransformAll();
        var printer = new Metano.Compiler.Dart.Printer();
        var result = new Dictionary<string, string>();
        foreach (var file in files)
            result[file.FileName] = printer.Print(file);
        return (result, transformer.Diagnostics);
    }
}
