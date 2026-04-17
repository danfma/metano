using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Extraction;

/// <summary>
/// Maps Roslyn <see cref="Accessibility"/> to <see cref="IrVisibility"/>.
/// </summary>
public static class IrVisibilityMapper
{
    public static IrVisibility Map(Accessibility accessibility) =>
        accessibility switch
        {
            Accessibility.Public => IrVisibility.Public,
            Accessibility.Protected => IrVisibility.Protected,
            Accessibility.Internal => IrVisibility.Internal,
            Accessibility.ProtectedOrInternal => IrVisibility.ProtectedInternal,
            Accessibility.ProtectedAndInternal => IrVisibility.PrivateProtected,
            Accessibility.Private => IrVisibility.Private,
            _ => IrVisibility.Public,
        };
}
