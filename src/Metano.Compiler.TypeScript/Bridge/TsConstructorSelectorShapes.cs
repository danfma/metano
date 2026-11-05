using Metano.Annotations;
using Metano.Compiler.Extraction;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// The TypeScript answer to
/// <see cref="IrExtractionContext.EmitsConstructorSelectors"/>: the types
/// this target emits as a class, so a dispatched constructor set gets a
/// selector and, for a concrete type, a factory per overload (spec 013
/// FR-007, FR-008). It mirrors the shape ladder of
/// <c>TypeTransformer.BuildTypeStatements</c>: a transpiled class or struct
/// that none of the shapes lowering to something else claims
/// (<c>[Import]</c>, <c>[Ignore]</c>, <c>[External]</c>, <c>[PlainObject]</c>,
/// branded, <c>[JsTuple]</c>, a JSX renderable; DECIDED-10).
/// </summary>
public static class TsConstructorSelectorShapes
{
    /// <summary>Whether TypeScript emits <paramref name="type"/> as a class.</summary>
    public static bool EmitsSelectors(INamedTypeSymbol type) =>
        type.TypeKind is TypeKind.Class or TypeKind.Struct
        && IsTranspiled(type)
        && !SymbolHelper.HasIgnore(type, TargetLanguage.TypeScript)
        && !SymbolHelper.HasPlainObject(type)
        && !SymbolHelper.HasBranded(type)
        && !SymbolHelper.HasJsTuple(type)
        && !SymbolHelper.HasExternal(type)
        && !SymbolHelper.HasImport(type)
        && !SymbolHelper.IsJsxRenderable(type);

    /// <summary>
    /// Whether code constructs <paramref name="type"/> through its overload
    /// factories in TypeScript.
    /// </summary>
    public static bool ConstructsThroughFactories(INamedTypeSymbol type) =>
        DispatchedConstructorCalls.ConstructsThroughFactories(type, EmitsSelectors);

    /// <summary>
    /// Whether the compilation declaring <paramref name="type"/> transpiles it,
    /// by the rule that compilation applies to its own types
    /// (<see cref="SymbolHelper.IsTranspilable"/>).
    /// </summary>
    private static bool IsTranspiled(INamedTypeSymbol type) =>
        SymbolHelper.IsTranspilable(
            type,
            type.ContainingAssembly.HasTranspileAssembly(),
            type.ContainingAssembly
        );
}
