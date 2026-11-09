using Metano.Annotations;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Dart.Bridge;

/// <summary>
/// The shapes the Dart target gives a C# type, where the overload naming of
/// spec 013 depends on them (FR-022, DECIDED-14): a containerless static class
/// lowers to top-level functions, and a class or struct whose shape the target
/// keeps as today's emission (<c>[PlainObject]</c>, branded — DECIDED-10)
/// names no overload members.
/// </summary>
public static class DartTypeShapes
{
    /// <summary>
    /// Whether a static class lowers to top-level Dart functions
    /// (<c>[NoContainer]</c> / <c>[ExportedAsModule]</c>) instead of a class of
    /// static methods.
    /// </summary>
    public static bool LowersToTopLevelFunctions(INamedTypeSymbol type) =>
        type.IsStatic && SymbolHelper.IsContainerless(type);

    /// <summary>
    /// The answer to <see cref="Extraction.IrExtractionContext.EmitsConstructorSelectors"/>:
    /// whether Dart emits <paramref name="type"/> as a class whose dispatched
    /// constructor set becomes one named constructor per overload, so a
    /// <c>new</c> of it calls the overload's named constructor.
    /// </summary>
    public static bool EmitsNamedConstructors(INamedTypeSymbol type) =>
        type.TypeKind is TypeKind.Class or TypeKind.Struct
        && IsTranspiled(type)
        && !LowersToTopLevelFunctions(type)
        && !SymbolHelper.HasIgnore(type, TargetLanguage.Dart)
        && !SymbolHelper.HasPlainObject(type)
        && !SymbolHelper.HasBranded(type)
        && !SymbolHelper.HasExternal(type)
        && !SymbolHelper.HasImport(type);

    /// <summary>
    /// Whether the class emits its dispatched overloads as addressable members
    /// — named constructors and fast paths — rather than today's single
    /// primary member: every class but the keep-today's-emission shapes.
    /// </summary>
    public static bool NamesOverloadMembers(IrClassDeclaration ir) =>
        !ir.Semantics.IsPlainObject && !ir.Semantics.IsBranded;

    private static bool IsTranspiled(INamedTypeSymbol type) =>
        SymbolHelper.IsTranspilable(
            type,
            type.ContainingAssembly.HasTranspileAssembly(),
            type.ContainingAssembly
        );
}
