using System.Runtime.CompilerServices;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.Extraction;

/// <summary>
/// Override-chain facts about properties: the overrides of the compilation (scan used by the
/// constructor-read diagnostic <see cref="OverridablePropertyConstructionReads"/>, which
/// cannot see overrides declared in other packages) and the per-class storage decision
/// (<see cref="OwnsPerClassStorage"/>), which is local to the property and so identical in
/// every package that compiles it.
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
    /// Whether the auto-property keeps a backing field of its own in every class of the
    /// chain it belongs to. C# gives each declaring class a private backing field, so a
    /// base value stays reachable through <c>base.P</c> while a derived class overrides it;
    /// a target that lowers the auto-property to one field of the property's name would
    /// share a single slot across the hierarchy, and that field would hide the override's
    /// accessor.
    /// <para>
    /// Decided from the property alone, so every package that compiles it (the declaring
    /// library and a consumer that overrides it) agrees without seeing the other: a
    /// <c>virtual</c> or <c>override</c> (sealed or not) auto-property that is not static,
    /// abstract (it has no storage) or an indexer.
    /// </para>
    /// </summary>
    public static bool OwnsPerClassStorage(IPropertySymbol property) =>
        property is { IsStatic: false, IsAbstract: false, IsIndexer: false }
        && (property.IsVirtual || property.IsOverride || HidesPerClassStorage(property))
        && IsAutoProperty(property);

    /// <summary>
    /// A non-virtual auto-property (<c>new int P</c>) hiding an inherited one that owns
    /// per-class storage: declared as a plain field it would sit over the base's accessor.
    /// Starts above the declaring class, whose own property is the one being decided.
    /// </summary>
    private static bool HidesPerClassStorage(IPropertySymbol property) =>
        AncestorsOf(property.ContainingType)
            .Any(ancestor =>
                ancestor
                    .GetMembers(property.Name)
                    .OfType<IPropertySymbol>()
                    .Any(hidden => OwnsPerClassStorage(hidden))
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
            overridden is not null && IsEmittedByToolchain(overridden.ContainingType, compilation);
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
    /// Stops at the first ancestor the toolchain does not emit, as an inheritance
    /// chain never re-enters emitted types after leaving them.
    /// </summary>
    public static bool DeclaresToJson(
        INamedTypeSymbol type,
        Compilation? compilation,
        Metano.Annotations.TargetLanguage? target
    ) =>
        compilation is not null
        && TypeAndAncestorsOf(type)
            .TakeWhile(current => IsEmittedByToolchain(current, compilation))
            .Any(current =>
                current.GetMembers().OfType<IMethodSymbol>().Any(m => IsToJson(m, target))
            );

    private static bool IsToJson(IMethodSymbol method, Metano.Annotations.TargetLanguage? target) =>
        method is { IsStatic: false, MethodKind: MethodKind.Ordinary }
        && !method.HasIgnore(target)
        && EmittedNameOf(method, target) == "toJSON";

    private static string EmittedNameOf(
        IMethodSymbol method,
        Metano.Annotations.TargetLanguage? target
    ) => method.GetNameOverride(target) ?? SymbolHelper.ToCamelCase(method.Name);

    /// <summary>
    /// Whether a class the compilation declares, among the ancestors of
    /// <paramref name="type"/>, emits an auto-property with per-class storage
    /// (<see cref="EmitsPerClassStorage(IPropertySymbol, Metano.Annotations.TargetLanguage?)"/>).
    /// </summary>
    public static bool AncestorEmitsPerClassStorage(
        INamedTypeSymbol type,
        Compilation? compilation,
        Metano.Annotations.TargetLanguage? target
    ) =>
        compilation is not null
        && AncestorsOf(type)
            .Any(ancestor =>
                IsEmittedByToolchain(ancestor, compilation)
                && ancestor
                    .GetMembers()
                    .OfType<IPropertySymbol>()
                    .Any(property => EmitsPerClassStorage(property, target))
            );

    /// <summary>
    /// Whether the property's per-class storage reaches the emitted output: it
    /// <see cref="OwnsPerClassStorage">owns storage</see> and is a member a backend
    /// emits — neither <c>private protected</c> nor <c>[Ignore]</c>d for
    /// <paramref name="target"/>. The one predicate both the core and the backends
    /// apply to decide that a backing field is declared.
    /// </summary>
    public static bool EmitsPerClassStorage(
        IPropertySymbol property,
        Metano.Annotations.TargetLanguage? target
    ) =>
        OwnsPerClassStorage(property)
        && property.DeclaredAccessibility is not Accessibility.ProtectedAndInternal
        && !SymbolHelper.HasIgnore(property, target);

    /// <summary>The type's base classes, nearest first, without the type itself.</summary>
    private static IEnumerable<INamedTypeSymbol> AncestorsOf(INamedTypeSymbol type)
    {
        for (var ancestor = type.BaseType; ancestor is not null; ancestor = ancestor.BaseType)
            yield return ancestor;
    }

    /// <summary>The type itself, then its base classes, nearest first.</summary>
    private static IEnumerable<INamedTypeSymbol> TypeAndAncestorsOf(INamedTypeSymbol type)
    {
        yield return type;

        foreach (var ancestor in AncestorsOf(type))
            yield return ancestor;
    }

    /// <summary>
    /// The class's declaration is emitted by the toolchain: the compilation itself or a
    /// transpiled package it references.
    /// </summary>
    private static bool IsEmittedByToolchain(INamedTypeSymbol type, Compilation compilation) =>
        SymbolHelper.IsToolchainDeclared(type, compilation.Assembly);

    /// <summary>
    /// An auto-property: by its syntax when declared in source, and in a referenced
    /// assembly by its metadata accessors.
    /// </summary>
    private static bool IsAutoProperty(IPropertySymbol property) =>
        property.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax()
            is PropertyDeclarationSyntax declaration
            ? PrimaryConstructorPropertyAlias.IsAutoProperty(declaration)
            : IsMetadataAutoProperty(property);

    /// <summary>
    /// An auto-property of a referenced assembly (no syntax): every accessor the
    /// compiler generated (<c>[CompilerGenerated]</c> survives in reference assemblies
    /// too, unlike the private backing field, and rejects a semi-auto <c>field</c>
    /// property that has a body).
    /// </summary>
    private static bool IsMetadataAutoProperty(IPropertySymbol property) =>
        property.DeclaringSyntaxReferences.IsEmpty
        && new[] { property.GetMethod, property.SetMethod }
            .OfType<IMethodSymbol>()
            .All(IsCompilerGenerated);

    private static bool IsCompilerGenerated(IMethodSymbol accessor) =>
        accessor
            .GetAttributes()
            .Any(attribute =>
                attribute.AttributeClass
                    is {
                        Name: "CompilerGeneratedAttribute",
                        ContainingNamespace.Name: "CompilerServices"
                    }
            );

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
