using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Extraction;

/// <summary>
/// Decides which constructor calls a target may lower to the exact overload
/// Roslyn resolved instead of the runtime dispatcher (spec 013 FR-017,
/// FR-019, DECIDED-11, DECIDED-24): a <c>new T(...)</c> to the overload's
/// factory, a <c>: base(...)</c> to its selector. The overload must belong to
/// a dispatched set (two or more declared constructors) of a type emitted as a
/// class whose IR is available — declared in this compilation or in a project
/// reference, which loads as source. A type read from metadata only (a DLL or
/// a package) keeps the dispatcher call: its claimed names are unknown, and
/// the dispatcher is correct, only slower.
/// </summary>
public static class DispatchedConstructorCalls
{
    /// <summary>
    /// The overload a <c>new</c> of <paramref name="constructor"/> constructs
    /// through its factory; <c>null</c> when the call keeps the dispatcher.
    /// </summary>
    public static IrOverloadRef? ConstructionTarget(
        IMethodSymbol? constructor,
        Metano.Annotations.TargetLanguage? target
    ) =>
        constructor is not null && ConstructsThroughFactories(constructor.ContainingType, target)
            ? OverloadOf(constructor)
            : null;

    /// <summary>
    /// The overload a <c>: base(...)</c> call to <paramref name="constructor"/>
    /// selects; <c>null</c> when the call keeps the dispatcher. An abstract
    /// base qualifies: it has selectors, only no factories (FR-012).
    /// </summary>
    public static IrOverloadRef? BaseCallTarget(
        IMethodSymbol? constructor,
        Metano.Annotations.TargetLanguage? target
    ) =>
        constructor is not null && HasSelectors(constructor.ContainingType, target)
            ? OverloadOf(constructor)
            : null;

    /// <summary>
    /// Whether code constructs <paramref name="type"/> through the factories
    /// of its constructor overloads (FR-007): a concrete type with selectors.
    /// </summary>
    public static bool ConstructsThroughFactories(
        INamedTypeSymbol type,
        Metano.Annotations.TargetLanguage? target
    ) => !type.IsAbstract && HasSelectors(type, target);

    /// <summary>
    /// Whether <paramref name="type"/> is emitted as a class with a selector
    /// per constructor overload (FR-008, FR-013), from IR this compilation can
    /// read.
    /// </summary>
    private static bool HasSelectors(
        INamedTypeSymbol type,
        Metano.Annotations.TargetLanguage? target
    )
    {
        var definition = type.OriginalDefinition;

        return definition.TypeKind is TypeKind.Class or TypeKind.Struct
            && IrConstructorExtractor.IsDispatchedSet(definition)
            && HasSourceIr(definition)
            && IsEmittedAsClass(definition, target);
    }

    /// <summary>
    /// A type with syntax: declared in this compilation or in a project
    /// reference (DECIDED-24).
    /// </summary>
    private static bool HasSourceIr(INamedTypeSymbol type) =>
        !type.DeclaringSyntaxReferences.IsEmpty;

    /// <summary>
    /// A transpiled type that the target emits as a class: none of the shapes
    /// that lower to something else (DECIDED-10) or emit nothing.
    /// </summary>
    private static bool IsEmittedAsClass(
        INamedTypeSymbol type,
        Metano.Annotations.TargetLanguage? target
    ) =>
        IsTranspiled(type)
        && !SymbolHelper.HasIgnore(type, target)
        && !SymbolHelper.HasPlainObject(type)
        && !SymbolHelper.HasBranded(type)
        && !SymbolHelper.HasJsTuple(type)
        && !SymbolHelper.HasJsCallable(type)
        && !SymbolHelper.HasExternal(type)
        && !SymbolHelper.HasImport(type)
        && !SymbolHelper.IsJsxRenderable(type);

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

    private static IrOverloadRef? OverloadOf(IMethodSymbol constructor) =>
        IrConstructorExtractor.DeclarationOrdinalOf(constructor) is { } ordinal
            ? IrOverloadRef.Constructor(IrTypeKeys.Of(constructor.ContainingType), ordinal)
            : null;
}
