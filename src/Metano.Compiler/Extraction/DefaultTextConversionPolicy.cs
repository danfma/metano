using Metano.Annotations;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Extraction;

/// <summary>The target and mapping decision used by every default-text consumer.</summary>
internal sealed record DefaultTextConversionPolicy(
    TargetLanguage? Target,
    Func<IMethodSymbol, int, bool>? LowersByDeclarativeMapping,
    DefaultTextSite Site
);
