using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.Extraction;

/// <summary>
/// Whether a property accessor is written with a body (<c>get =&gt; …</c>,
/// <c>set { … }</c>) rather than as an auto accessor (<c>get;</c>).
/// </summary>
internal static class AccessorBodies
{
    /// <summary>
    /// A source declaration of <paramref name="accessor"/> has a body. Syntax-based,
    /// so an accessor of a referenced-assembly property reports <c>false</c>.
    /// </summary>
    public static bool HasBody(IMethodSymbol accessor) =>
        accessor.DeclaringSyntaxReferences.Any(reference =>
            reference.GetSyntax() is AccessorDeclarationSyntax syntax && HasBody(syntax)
        );

    /// <summary><paramref name="accessor"/> has a block or expression body.</summary>
    public static bool HasBody(AccessorDeclarationSyntax accessor) =>
        accessor.Body is not null || accessor.ExpressionBody is not null;
}
