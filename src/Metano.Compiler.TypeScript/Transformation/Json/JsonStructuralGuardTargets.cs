using Microsoft.CodeAnalysis;

namespace Metano.Compiler.TypeScript.Transformation.Json;

/// <summary>
/// The <c>[PlainObject]</c> types whose <c>isT</c> guard a polymorphic JSON
/// hierarchy needs (spec 009 FR-019 rule 1), whether or not they declare
/// <c>[GenerateGuard]</c>: every <c>[PlainObject]</c> type a
/// <c>[JsonDerivedType]</c> of this compilation registers, and every concrete
/// <c>[PlainObject]</c> type that is itself polymorphic (its guard is the
/// rule 5 base test, or its self-registration's test).
/// <para>
/// The set is read from the attributes, not from the contract closure of a
/// context (DECIDED-23 a): a registered type always lives in its base's
/// assembly (the base names it with <c>typeof</c>, and it derives from the
/// base), so a library emits the guards a consumer's context imports without
/// knowing that context. Computed once per compilation, so a variant's file
/// and every context agree.
/// </para>
/// </summary>
internal static class JsonStructuralGuardTargets
{
    public static IReadOnlySet<INamedTypeSymbol> Collect(
        IEnumerable<INamedTypeSymbol> transpilableTypes,
        bool assemblyWideTranspile,
        IAssemblySymbol? currentAssembly
    )
    {
        bool IsTranspiled(INamedTypeSymbol type) =>
            SymbolHelper.IsTranspilable(type, assemblyWideTranspile, currentAssembly);

        IEnumerable<INamedTypeSymbol> WithNestedTypes(INamedTypeSymbol type) =>
            type.GetTypeMembers()
                .Where(nested => !nested.IsImplicitlyDeclared && IsTranspiled(nested))
                .SelectMany(WithNestedTypes)
                .Prepend(type);

        return transpilableTypes
            .SelectMany(WithNestedTypes)
            .SelectMany(GuardedTypesOf)
            .Where(type => IsInAssembly(type, currentAssembly) && IsTranspiled(type))
            .Select(type => type.OriginalDefinition)
            .ToHashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
    }

    /// <summary>
    /// The <c>[PlainObject]</c> types the polymorphism of
    /// <paramref name="baseType"/> tests with a guard: its non-generic
    /// registrations and, for a concrete base, the base itself.
    /// </summary>
    private static IEnumerable<INamedTypeSymbol> GuardedTypesOf(INamedTypeSymbol baseType)
    {
        if (JsonPolymorphismReader.TryRead(baseType) is not { } polymorphism)
            return [];

        var registered = polymorphism
            .Registrations.Select(registration => registration.Type)
            .OfType<INamedTypeSymbol>();

        var candidates = IsConcreteObjectType(baseType) ? registered.Append(baseType) : registered;

        // A generic type has no guard the context could reference: its
        // registrations are MS0032, and its base spec is a construction.
        return candidates.Where(type => !type.IsGenericType && SymbolHelper.HasPlainObject(type));
    }

    private static bool IsConcreteObjectType(INamedTypeSymbol type) =>
        type.TypeKind is TypeKind.Class or TypeKind.Struct && !type.IsAbstract;

    private static bool IsInAssembly(INamedTypeSymbol type, IAssemblySymbol? assembly) =>
        assembly is null
        || SymbolEqualityComparer.Default.Equals(type.ContainingAssembly, assembly);
}
