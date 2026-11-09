using Metano.Annotations;
using Metano.Compiler.Extraction;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Dart.Bridge;

/// <summary>
/// The shapes the Dart target gives a C# type, where the overload naming of
/// spec 013 depends on them (FR-022, DECIDED-14): a containerless static class
/// lowers to top-level functions, and a class or struct whose shape the target
/// keeps as today's emission (<c>[PlainObject]</c>, branded — DECIDED-10)
/// names no overload members.
/// </summary>
internal static class DartTypeShapes
{
    /// <summary>
    /// Whether a static class lowers to top-level Dart functions
    /// (<c>[NoContainer]</c> / <c>[ExportedAsModule]</c>) instead of a class of
    /// static methods.
    /// </summary>
    public static bool LowersToTopLevelFunctions(INamedTypeSymbol type) =>
        type.IsStatic && SymbolHelper.IsContainerless(type);

    /// <summary>
    /// How Dart emits the overload groups of <paramref name="type"/>, decided
    /// once per symbol and read by the core port
    /// (<see cref="EmitsNamedConstructors"/>), the claim pass and the class
    /// bridge alike, so a declaration and its call sites never disagree.
    /// </summary>
    public static DartOverloadShape ShapeOf(INamedTypeSymbol type) =>
        new(
            NamesMembers: NamesOverloadMembers(type),
            PinsSuffixes: OverloadEmittedName.PinsOverloadSuffixes(type, TargetLanguage.Dart)
        );

    /// <summary>
    /// The answer to <see cref="Extraction.IrExtractionContext.EmitsConstructorSelectors"/>:
    /// whether Dart emits <paramref name="type"/> as a class whose dispatched
    /// constructor set becomes one named constructor per overload, so a
    /// <c>new</c> of it calls the overload's named constructor.
    /// </summary>
    public static bool EmitsNamedConstructors(INamedTypeSymbol type) =>
        SymbolHelper.IsTranspiledByItsAssembly(type)
        && !SymbolHelper.HasIgnore(type, TargetLanguage.Dart)
        && ShapeOf(type).NamesMembers;

    /// <summary>
    /// Whether the class emits its dispatched overloads as addressable members
    /// — named constructors and fast paths — rather than today's single
    /// primary member: every class or struct Dart emits as a class, but the
    /// keep-today's-emission shapes (<c>[PlainObject]</c>, branded, DECIDED-10)
    /// and a containerless static class, which lowers to top-level functions.
    /// </summary>
    private static bool NamesOverloadMembers(INamedTypeSymbol type) =>
        type.TypeKind is TypeKind.Class or TypeKind.Struct
        && !LowersToTopLevelFunctions(type)
        && !SymbolHelper.HasPlainObject(type)
        && !SymbolHelper.HasBranded(type);
}

/// <summary>
/// How Dart emits a class's overload groups (<see cref="DartTypeShapes.ShapeOf"/>).
/// </summary>
/// <param name="NamesMembers">Named constructors and fast paths; otherwise the
/// primary member alone, with <c>MS0001</c>.</param>
/// <param name="PinsSuffixes">Whether a <c>[Name]</c> on an overload pins a
/// suffix (the core's rule, which call sites follow) rather than renaming the
/// member; a kept primary is then declared under its C# name.</param>
internal readonly record struct DartOverloadShape(bool NamesMembers, bool PinsSuffixes)
{
    /// <summary>A type with no overload groups to name.</summary>
    public static DartOverloadShape None { get; } = new(NamesMembers: false, PinsSuffixes: false);
}
