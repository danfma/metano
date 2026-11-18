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
    /// The reached set a descriptor read the checks render is asserted against
    /// (the file group's <see cref="IrToTsBridgeContext.RuntimeTypes"/>);
    /// <see cref="Analysis.RuntimeTypeReachabilityResult.Unchecked"/> for a scope
    /// that renders no output (an ambiguity analysis).
    /// </summary>
    public Analysis.RuntimeTypeReachabilityResult RuntimeTypes { get; init; } =
        Analysis.RuntimeTypeReachabilityResult.Empty;

    /// <summary>
    /// The scope of a method: its own type parameters, then its class's, so a
    /// method type parameter shadows a class one of the same name.
    /// </summary>
    public static TypeCheckScope ForMethod(
        IrMethodDeclaration method,
        IReadOnlyList<IrTypeParameter> classTypeParameters,
        IrToTsTypeOverrides? typeOverrides
    ) => new([.. method.TypeParameters ?? [], .. classTypeParameters], typeOverrides);

    /// <summary>
    /// The scope of <paramref name="method"/> inside this class scope, keeping
    /// its overrides and reached set.
    /// </summary>
    public TypeCheckScope Within(IrMethodDeclaration method) =>
        ForMethod(method, TypeParameters, TypeOverrides) with
        {
            RuntimeTypes = RuntimeTypes,
        };
}
