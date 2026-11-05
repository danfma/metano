using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Extraction;

/// <summary>
/// Decides which constructor calls are lowered to the exact overload Roslyn
/// resolved instead of the runtime dispatcher (spec 013 FR-017, FR-019,
/// DECIDED-11, DECIDED-24): a <c>new T(...)</c> to the overload's factory, a
/// <c>: base(...)</c> to its selector. The core holds the target-neutral
/// facts — the overload belongs to a dispatched set (two or more declared
/// constructors) whose IR is available, declared in this compilation or in a
/// project reference, which loads as source; a type read from metadata only
/// (a DLL or a package) keeps the dispatcher call, since its claimed names are
/// unknown and the dispatcher is correct, only slower. Which shapes a target
/// emits with selectors and factories is the target's call, asked through
/// <see cref="IrExtractionContext.EmitsConstructorSelectors"/>.
/// </summary>
public static class DispatchedConstructorCalls
{
    /// <summary>
    /// The overload a <c>new</c> of <paramref name="constructor"/> constructs
    /// through its factory; <c>null</c> when the call keeps the dispatcher.
    /// </summary>
    public static IrOverloadRef? ConstructionTarget(
        IMethodSymbol? constructor,
        IrExtractionContext context
    ) =>
        constructor is not null
        && ConstructsThroughFactories(constructor.ContainingType, context.EmitsConstructorSelectors)
            ? OverloadOf(constructor)
            : null;

    /// <summary>
    /// The overload a <c>: base(...)</c> call to <paramref name="constructor"/>
    /// selects; <c>null</c> when the call keeps the dispatcher. An abstract
    /// base qualifies: it has selectors, only no factories (FR-012).
    /// </summary>
    public static IrOverloadRef? BaseCallTarget(
        IMethodSymbol? constructor,
        IrExtractionContext context
    ) =>
        constructor is not null
        && HasSelectors(constructor.ContainingType, context.EmitsConstructorSelectors)
            ? OverloadOf(constructor)
            : null;

    /// <summary>
    /// Whether code constructs <paramref name="type"/> through the factories
    /// of its constructor overloads (FR-007): a concrete type with selectors.
    /// </summary>
    public static bool ConstructsThroughFactories(
        INamedTypeSymbol type,
        Func<INamedTypeSymbol, bool>? emitsSelectors
    ) => !type.IsAbstract && HasSelectors(type, emitsSelectors);

    /// <summary>
    /// Whether <paramref name="type"/> has a selector per constructor
    /// overload: a dispatched set, from IR this compilation can read, of a
    /// shape the target emits with selectors.
    /// </summary>
    public static bool HasSelectors(
        INamedTypeSymbol type,
        Func<INamedTypeSymbol, bool>? emitsSelectors
    )
    {
        var definition = type.OriginalDefinition;

        return emitsSelectors is not null
            && IrConstructorExtractor.IsDispatchedSet(definition)
            && HasSourceIr(definition)
            && emitsSelectors(definition);
    }

    /// <summary>
    /// A type with syntax: declared in this compilation or in a project
    /// reference (DECIDED-24).
    /// </summary>
    private static bool HasSourceIr(INamedTypeSymbol type) =>
        !type.DeclaringSyntaxReferences.IsEmpty;

    private static IrOverloadRef? OverloadOf(IMethodSymbol constructor) =>
        IrConstructorExtractor.DeclarationOrdinalOf(constructor) is { } ordinal
            ? IrOverloadRef.Constructor(IrTypeKeys.Of(constructor.ContainingType), ordinal)
            : null;
}
