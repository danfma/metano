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

    public IReadOnlyList<DartSourceFile> LastSourceFiles { get; private set; } = [];

    public TargetOutput Transform(IrCompilation ir, Compilation compilation)
    {
        // The Dart prototype still drives discovery off the Roslyn compilation;
        // the IR is accepted to honor the contract and used opportunistically
        // by helpers as they migrate.
        _ = ir;

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
