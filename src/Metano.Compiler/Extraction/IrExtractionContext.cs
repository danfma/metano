using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Extraction;

/// <summary>
/// Everything an IR extractor needs beyond its syntax and semantic model:
/// the cross-package type-origin resolver, the compilation (null in
/// shape-only mode), the language target, and the target's local-naming
/// decision the local-name disambiguator maps bindings through (#398). One
/// immutable object threads through every extractor — type, member, module
/// function, statement, expression — replacing the
/// <c>originResolver / compilation / target / localNaming</c> parameter
/// clump, so a new extraction input reaches every nested extractor by
/// construction instead of by threading one more optional parameter.
/// </summary>
public sealed record IrExtractionContext(
    IrTypeOriginResolver? OriginResolver = null,
    Compilation? Compilation = null,
    Metano.Annotations.TargetLanguage? Target = null,
    LocalNaming? LocalNaming = null
)
{
    /// <summary>Shape-only extraction: no resolver, compilation or target.</summary>
    public static IrExtractionContext Empty { get; } = new();
}
