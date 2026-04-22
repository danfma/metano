using Metano.Annotations;
using Metano.Compiler;
using Metano.Compiler.IR;
using Metano.Dart.AST;
using Metano.Dart.Transformation;
using Microsoft.CodeAnalysis;

namespace Metano.Dart;

/// <summary>
/// <see cref="ITranspilerTarget"/> implementation for the Dart/Flutter backend.
/// </summary>
public sealed class DartTarget : ITranspilerTarget
{
    public string Name => "dart";

    public TargetLanguage Language => TargetLanguage.Dart;

    public IReadOnlyList<DartSourceFile> LastSourceFiles { get; private set; } = [];

    public TargetOutput Transform(IrCompilation ir, Compilation? compilation)
    {
        if (compilation is null)
            throw new NotSupportedException(
                "DartTarget currently requires a Roslyn-backed source frontend; "
                    + "compilation was null. The Roslyn dependency will go away once every "
                    + "Dart per-type extractor also reads its inputs from IrCompilation."
            );

        var transformer = new DartTransformer(ir, compilation);
        var sourceFiles = transformer.TransformAll();
        LastSourceFiles = sourceFiles;

        var printer = new Printer();
        var generated = sourceFiles
            .Select(f => new GeneratedFile(f.FileName, printer.Print(f)))
            .ToList();

        return new TargetOutput(generated, transformer.Diagnostics);
    }
}
