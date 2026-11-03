using Metano.Annotations;
using Metano.Compiler;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;

namespace Metano.Tests.Caching;

/// <summary>
/// Forwards to the real target and records whether the host took the full
/// cache-hit path (<see cref="ITranspilerTarget.AdoptCachedOutput"/>) instead of
/// <see cref="ITranspilerTarget.Transform"/>.
/// </summary>
internal sealed class CacheAdoptionRecorder(TypeScriptTarget inner) : ITranspilerTarget
{
    public bool AdoptedCachedOutput { get; private set; }

    public string Name => inner.Name;

    public TargetLanguage Language => inner.Language;

    public string ConfigurationFingerprint => inner.ConfigurationFingerprint;

    public TargetOutput Transform(
        IrCompilation ir,
        Compilation? compilation,
        string? outputDir = null,
        string? filePrefix = null
    ) => inner.Transform(ir, compilation, outputDir, filePrefix);

    public void AdoptCachedOutput(
        IrCompilation ir,
        Compilation? compilation,
        IReadOnlyList<GeneratedFile> files,
        IReadOnlyDictionary<string, string> packageDependencies
    )
    {
        AdoptedCachedOutput = true;
        inner.AdoptCachedOutput(ir, compilation, files, packageDependencies);
    }
}
