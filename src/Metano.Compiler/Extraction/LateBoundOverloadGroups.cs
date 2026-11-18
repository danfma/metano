using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Extraction;

/// <summary>
/// Decides whether a call or <c>new</c> that a <c>dynamic</c> argument left
/// with several candidates (spec 011 FR-030) can go through a runtime
/// dispatcher: the candidates must be exactly the overloads one dispatcher
/// branches over for the call's argument count, compared by symbol identity,
/// so the dispatcher, which tests the argument count first, chooses among the
/// same set the runtime binder does. Any other candidate set (a BCL method
/// group, a group the dispatcher holds more or fewer overloads of, a type with
/// no dispatcher) does not, and the caller reports <c>MS0053</c>.
/// </summary>
internal static class LateBoundOverloadGroups
{
    /// <summary>
    /// The candidate whose group's dispatcher branches over exactly
    /// <paramref name="candidates"/> for <paramref name="argumentCount"/>
    /// arguments: the group the overload registry
    /// (<see cref="MethodOverloadGroups"/>) gives it, inherited overloads
    /// included, so a derived class's added overloads resolve to its own
    /// dispatcher. <c>null</c> when no such group exists.
    /// </summary>
    public static IMethodSymbol? DispatcherOwnerOf(
        IReadOnlyList<IMethodSymbol> candidates,
        int argumentCount,
        IrExtractionContext context
    )
    {
        if (candidates.Count < 2)
            return null;

        var definitions = candidates
            .Select(candidate => candidate.OriginalDefinition)
            .ToHashSet(SymbolEqualityComparer.Default);

        var methods = definitions.OfType<IMethodSymbol>().ToList();

        if (!methods.All(candidate => IsDispatchedCall(candidate, context)))
            return null;

        return methods.FirstOrDefault(candidate =>
            BranchesOverExactly(candidate, definitions, argumentCount, context)
        );
    }

    /// <summary>
    /// Whether <paramref name="candidates"/> are every constructor taking
    /// <paramref name="argumentCount"/> arguments of one type that emits a
    /// constructor dispatcher: a dispatched set (two or more
    /// declared constructors) of a class built from source, not a
    /// <c>[PlainObject]</c> or <c>[JsTuple]</c> (an object or array literal)
    /// and without an <c>[ObjectArgs]</c> constructor.
    /// </summary>
    public static bool IsDispatchedConstructorSet(
        IReadOnlyList<IMethodSymbol> candidates,
        int argumentCount
    )
    {
        if (candidates.Count < 2)
            return false;

        var type = candidates[0].ContainingType.OriginalDefinition;

        if (!EmitsConstructorDispatcher(type))
            return false;

        var declared = IrConstructorExtractor
            .AllExplicitConstructors(type)
            .Where(constructor => Accepts(constructor, argumentCount))
            .ToHashSet(SymbolEqualityComparer.Default);

        return declared.SetEquals(candidates.Select(candidate => candidate.OriginalDefinition));
    }

    private static bool EmitsConstructorDispatcher(INamedTypeSymbol type) =>
        IrConstructorExtractor.IsDispatchedSet(type)
        && !type.DeclaringSyntaxReferences.IsEmpty
        && !type.HasPlainObject()
        && !type.HasJsTuple()
        && !IrConstructorExtractor.AllExplicitConstructors(type).Any(SymbolHelper.HasObjectArgs);

    /// <summary>
    /// Whether a call to <paramref name="method"/> could keep its group's
    /// dispatcher: a member of a dispatched group whose IR this compilation
    /// reads (<see cref="DispatchedMethodCalls.RefOf"/>).
    /// </summary>
    private static bool IsDispatchedCall(IMethodSymbol method, IrExtractionContext context) =>
        method.MethodKind == MethodKind.Ordinary
        && DispatchedMethodCalls.RefOf(method, context) is not null;

    private static bool BranchesOverExactly(
        IMethodSymbol candidate,
        HashSet<ISymbol?> definitions,
        int argumentCount,
        IrExtractionContext context
    )
    {
        if (
            MethodOverloadGroups.GroupOf(candidate, context.GroupingPolicy)
            is not { IsDispatched: true, IsSegregatedOverrides: false } group
        )
            return false;

        var branches = group
            .Members.Concat(group.InheritedOverloads ?? [])
            .Where(member =>
                member.RequiredArity <= argumentCount && argumentCount <= member.MaximumArity
            )
            .Select(member => member.Declaration?.OriginalDefinition)
            .ToList();

        return branches.All(branch => branch is not null) && definitions.SetEquals(branches);
    }

    private static bool Accepts(IMethodSymbol method, int argumentCount)
    {
        var required = method.Parameters.Count(parameter =>
            !parameter.IsOptional && !parameter.IsParams
        );

        var maximum = method.Parameters.Any(parameter => parameter.IsParams)
            ? int.MaxValue
            : method.Parameters.Length;

        return required <= argumentCount && argumentCount <= maximum;
    }
}
