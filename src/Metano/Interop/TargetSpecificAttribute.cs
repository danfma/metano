using Metano.Annotations;

namespace Metano.Interop;

/// <summary>
/// Binds an interop type or member of this package to the one target language it
/// exists for (spec 011 FR-033). The transpiler reports <c>MS0051</c> at every
/// reference that an emission for another target reaches, unless the containing
/// member or type is <c>[Ignore]</c>d for that target. Internal: the compiler reads
/// it by its full metadata name, and user code has no use for it.
/// </summary>
[AttributeUsage(
    AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Method,
    Inherited = false
)]
internal sealed class TargetSpecificAttribute(TargetLanguage language) : Attribute
{
    /// <summary>The only target language the annotated symbol exists for.</summary>
    public TargetLanguage Language { get; } = language;
}
