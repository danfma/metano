using System.Runtime.CompilerServices;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.Extraction;

/// <summary>
/// Which properties of the compilation take part in an override chain: the
/// scan shared by the constructor-read diagnostic
/// (<see cref="OverridablePropertyConstructionReads"/>) and the per-class storage
/// decision (<see cref="OwnsPerClassStorage"/>). Overrides declared in other
/// packages are not visible (known gap).
/// </summary>
internal static class OverrideChains
{
    private static readonly ConditionalWeakTable<
        Compilation,
        HashSet<IPropertySymbol>
    > OverriddenPropertiesCache = new();

    /// <summary>
    /// Whether <paramref name="property"/> is overridable (<c>virtual</c> or an
    /// unsealed <c>override</c>) and the compilation contains an override of it.
    /// Matched per property, not per subtree.
    /// </summary>
    public static bool IsOverridableAndOverridden(
        IPropertySymbol property,
        Compilation compilation
    ) =>
        IsOverridable(property)
        && OverriddenPropertiesIn(compilation).Contains(property.OriginalDefinition);

    /// <summary>
    /// Whether the auto-property keeps a backing field of its own in every class of
    /// an override chain the compilation declares. C# gives each declaring class a
    /// private backing field, so a base value stays reachable through
    /// <c>base.P</c> while a derived class overrides it; a target that lowers the
    /// auto-property to one field of the property's name would share a single slot
    /// across the hierarchy, and that field would hide the override's accessor.
    /// <para>
    /// Applies to an auto-property that is not static, abstract (it has no storage)
    /// or an indexer, and that is either overridable and overridden in the
    /// compilation, or overrides a non-abstract property the compilation declares
    /// (so a sealed override follows the base it overrides).
    /// </para>
    /// </summary>
    public static bool OwnsPerClassStorage(IPropertySymbol property, Compilation? compilation) =>
        compilation is not null
        && property is { IsStatic: false, IsAbstract: false, IsIndexer: false }
        && IsAutoProperty(property)
        && (
            IsOverridableAndOverridden(property, compilation)
            || OverridesDeclaredBase(property, compilation)
        );

    /// <summary>
    /// The accessor <paramref name="property"/> leaves to the properties it overrides: an
    /// override may declare only <c>get</c> or only <c>set</c> and C# keeps the other
    /// accessor of the nearest overridden property that declares it, however many overrides
    /// up the chain. Only properties the compilation declares count.
    /// </summary>
    public static IrInheritedAccessor InheritedAccessorOf(
        IPropertySymbol property,
        Compilation? compilation
    )
    {
        if (
            compilation is null
            || property is not { IsOverride: true, IsStatic: false, IsIndexer: false }
        )
        {
            return IrInheritedAccessor.None;
        }

        if (
            property.SetMethod is null
            && OverriddenChainDeclares(property, compilation, p => p.SetMethod)
        )
            return IrInheritedAccessor.Setter;

        return
            property.GetMethod is null
            && OverriddenChainDeclares(property, compilation, p => p.GetMethod)
            ? IrInheritedAccessor.Getter
            : IrInheritedAccessor.None;
    }

    private static bool OverriddenChainDeclares(
        IPropertySymbol property,
        Compilation compilation,
        Func<IPropertySymbol, IMethodSymbol?> accessor
    )
    {
        for (
            var overridden = property.OverriddenProperty;
            overridden is not null && IsDeclaredIn(overridden, compilation);
            overridden = overridden.OverriddenProperty
        )
        {
            if (accessor(overridden) is not null)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Whether <paramref name="type"/> or a class the compilation declares among its
    /// ancestors declares an instance method that the target emits as <c>toJSON</c>
    /// (an unrenamed <c>ToJSON</c>, or one renamed to it for <paramref name="target"/>).
    /// </summary>
    public static bool DeclaresToJson(
        INamedTypeSymbol type,
        Compilation? compilation,
        Metano.Annotations.TargetLanguage? target
    )
    {
        for (
            var current = type;
            current is not null && compilation is not null && IsDeclaredIn(current, compilation);
            current = current.BaseType
        )
        {
            if (current.GetMembers().OfType<IMethodSymbol>().Any(m => IsToJson(m, target)))
                return true;
        }

        return false;
    }

    private static bool IsToJson(IMethodSymbol method, Metano.Annotations.TargetLanguage? target) =>
        method is { IsStatic: false, MethodKind: MethodKind.Ordinary }
        && (method.GetNameOverride(target) ?? (method.Name == "ToJSON" ? "toJSON" : method.Name))
            == "toJSON";

    /// <summary>
    /// Whether a class the compilation declares, among the ancestors of
    /// <paramref name="type"/>, emits an auto-property with per-class storage
    /// (<see cref="OwnsPerClassStorage"/>): one that is neither <c>[Ignore]</c>d for
    /// <paramref name="target"/> nor <c>private protected</c>, which a backend omits.
    /// </summary>
    public static bool AncestorOwnsPerClassStorage(
        INamedTypeSymbol type,
        Compilation? compilation,
        Metano.Annotations.TargetLanguage? target
    )
    {
        if (compilation is null)
            return false;

        for (var ancestor = type.BaseType; ancestor is not null; ancestor = ancestor.BaseType)
        {
            if (
                IsDeclaredIn(ancestor, compilation)
                && EmitsPerClassStorage(ancestor, compilation, target)
            )
            {
                return true;
            }
        }

        return false;
    }

    private static bool EmitsPerClassStorage(
        INamedTypeSymbol type,
        Compilation compilation,
        Metano.Annotations.TargetLanguage? target
    ) =>
        type.GetMembers()
            .OfType<IPropertySymbol>()
            .Any(property =>
                OwnsPerClassStorage(property, compilation)
                && property.DeclaredAccessibility is not Accessibility.ProtectedAndInternal
                && !SymbolHelper.HasIgnore(property, target)
            );

    private static bool IsDeclaredIn(ISymbol symbol, Compilation compilation) =>
        SymbolEqualityComparer.Default.Equals(symbol.ContainingAssembly, compilation.Assembly);

    private static bool OverridesDeclaredBase(IPropertySymbol property, Compilation compilation) =>
        property.OverriddenProperty is { IsAbstract: false } overridden
        && IsDeclaredIn(overridden, compilation);

    private static bool IsAutoProperty(IPropertySymbol property) =>
        property.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax()
            is PropertyDeclarationSyntax declaration
        && PrimaryConstructorPropertyAlias.IsAutoProperty(declaration);

    private static bool IsOverridable(IPropertySymbol property) =>
        !property.IsIndexer && (property.IsVirtual || (property.IsOverride && !property.IsSealed));

    private static HashSet<IPropertySymbol> OverriddenPropertiesIn(Compilation compilation) =>
        OverriddenPropertiesCache.GetValue(compilation, ScanOverriddenProperties);

    private static HashSet<IPropertySymbol> ScanOverriddenProperties(Compilation compilation)
    {
        var overridden = new HashSet<IPropertySymbol>(SymbolEqualityComparer.Default);

        foreach (var type in AllTypesIn(compilation.Assembly.GlobalNamespace))
        foreach (var property in type.GetMembers().OfType<IPropertySymbol>())
            for (
                var parent = property.OverriddenProperty;
                parent is not null;
                parent = parent.OverriddenProperty
            )
                overridden.Add(parent.OriginalDefinition);

        return overridden;
    }

    private static IEnumerable<INamedTypeSymbol> AllTypesIn(INamespaceSymbol ns)
    {
        foreach (var type in ns.GetTypeMembers())
        foreach (var nested in WithNestedTypes(type))
            yield return nested;

        foreach (var child in ns.GetNamespaceMembers())
        foreach (var type in AllTypesIn(child))
            yield return type;
    }

    private static IEnumerable<INamedTypeSymbol> WithNestedTypes(INamedTypeSymbol type)
    {
        yield return type;

        foreach (var nested in type.GetTypeMembers())
        foreach (var inner in WithNestedTypes(nested))
            yield return inner;
    }
}
