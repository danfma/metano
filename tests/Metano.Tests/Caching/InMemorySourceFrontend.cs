using Metano.Annotations;
using Metano.Compiler;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;

namespace Metano.Tests.Caching;

/// <summary>
/// Serves a prebuilt in-memory compilation through the production C# extraction, so
/// the host runs its real cache and emit path without loading an MSBuild project.
/// </summary>
internal sealed class InMemorySourceFrontend(Compilation compilation) : ISourceFrontend
{
    private readonly CSharpSourceFrontend _extractor = new();

    public string Name => "in-memory";

    public Compilation? LoadedCompilation => compilation;

    public int LoadErrorCount => 0;

    public Task<IrCompilation> ExtractAsync(
        string projectPath,
        CancellationToken ct = default,
        TargetLanguage target = TargetLanguage.TypeScript
    ) => Task.FromResult(_extractor.ExtractFromCompilation(compilation, target));
}
