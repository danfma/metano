using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Frontend.Roslyn;

/// <summary>
/// One rule over the type and member references that
/// <see cref="TranspiledReferenceWalker"/> finds in emitted code. Each rule reports
/// its own diagnostic and keeps its own record of the locations it reported.
/// </summary>
internal interface ITranspiledReferenceRule
{
    /// <summary>A named type referenced at <paramref name="location"/>.</summary>
    void CheckType(INamedTypeSymbol type, Location location);

    /// <summary>A method, property, field or event referenced at <paramref name="location"/>.</summary>
    void CheckMember(ISymbol member, Location location);
}
