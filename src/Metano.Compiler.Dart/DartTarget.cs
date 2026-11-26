using Metano.Annotations;
using Metano.Compiler;
using Metano.Compiler.Dart.AST;
using Metano.Compiler.Dart.Bridge;
using Metano.Compiler.Dart.Transformation;
using Metano.Compiler.Diagnostics;
using Metano.Compiler.Extraction;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Dart;

/// <summary>
/// <see cref="ITranspilerTarget"/> implementation for the Dart/Flutter backend.
/// </summary>
public sealed class DartTarget : ITranspilerTarget
{
    public string Name => "dart";

    public TargetLanguage Language => TargetLanguage.Dart;

    public bool SupportsReifiedTypeParameters => false;

    public OverloadGroupingPolicy GroupingPolicy => DartGroupingPolicy.Instance;

    public IReadOnlyList<DartSourceFile> LastSourceFiles { get; private set; } = [];

    public TargetOutput Transform(
        IrCompilation ir,
        Compilation? compilation,
        string? outputDir = null,
        string? filePrefix = null
    )
    {
        if (compilation is null)
            throw new NotSupportedException(
                "DartTarget currently requires a Roslyn-backed source frontend; "
                    + "compilation was null. The Roslyn dependency will go away once every "
                    + "Dart per-type extractor also reads its inputs from IrCompilation."
            );

        // The Dart target is a prototype that does not render every body yet:
        // a placeholder that drops code is a warning (W9-13a).
        using var policy = DroppedCodeDiagnostics.Use(
            new DroppedCodePolicy(MetanoDiagnosticSeverity.Warning, "Dart")
        );

        var transformer = new DartTransformer(ir, compilation);
        var sourceFiles = transformer.TransformAll();
        LastSourceFiles = sourceFiles;

        var printingDiagnostics = new List<MetanoDiagnostic>();
        var generated = Print(sourceFiles, printingDiagnostics);

        return new TargetOutput(generated, [.. transformer.Diagnostics, .. printingDiagnostics]);
    }

    /// <summary>
    /// Prints the files with a diagnostic sink open: the body printer reports
    /// the placeholders it emits for IR it cannot render (W9-13).
    /// </summary>
    private static List<GeneratedFile> Print(
        IReadOnlyList<DartSourceFile> sourceFiles,
        List<MetanoDiagnostic> diagnostics
    )
    {
        using var sink = ExtractionDiagnostics.Open(diagnostics.Add);

        var printer = new Printer();

        return [.. sourceFiles.Select(f => new GeneratedFile(f.FileName, printer.Print(f)))];
    }
}
