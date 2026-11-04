using Metano.Compiler.IR;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// What a dispatcher's runtime type checks resolve against (see
/// <see cref="IrTypeCheckBuilder.Generate"/>).
/// </summary>
/// <param name="TypeParameters">The type parameters in scope, innermost first;
/// a type parameter reference resolves against them, and an unresolved one
/// matches any value.</param>
/// <param name="TypeOverrides">The <c>[ExportFromBcl]</c> type overrides the
/// checked types were mapped with: a type that lowers to an exported class
/// (<c>decimal</c> → <c>Decimal</c>) is checked with <c>instanceof</c> that
/// class; <c>null</c> keeps the default mapping.</param>
internal sealed record TypeCheckScope(
    IReadOnlyList<IrTypeParameter> TypeParameters,
    IrToTsTypeOverrides? TypeOverrides
)
{
    /// <summary>No type parameter and no override.</summary>
    public static TypeCheckScope Empty { get; } = new([], null);

    /// <summary>
    /// The scope of a method: its own type parameters, then its class's, so a
    /// method type parameter shadows a class one of the same name.
    /// </summary>
    public static TypeCheckScope ForMethod(
        IrMethodDeclaration method,
        IReadOnlyList<IrTypeParameter> classTypeParameters,
        IrToTsTypeOverrides? typeOverrides
    ) => new([.. method.TypeParameters ?? [], .. classTypeParameters], typeOverrides);
}
