using Metano.Compiler;
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

    public IReadOnlyList<DartSourceFile> LastSourceFiles { get; private set; } = [];

    public TargetOutput Transform(Compilation compilation)
    {
        var transformer = new DartTransformer(compilation);
        var sourceFiles = transformer.TransformAll();
        LastSourceFiles = sourceFiles;

        var printer = new Printer();
        var generated = sourceFiles
            .Select(f => new GeneratedFile(f.FileName, printer.Print(f)))
            .ToList();

        return new TargetOutput(generated, transformer.Diagnostics);
    }
}
