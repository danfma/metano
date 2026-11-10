using Microsoft.CodeAnalysis;

namespace Metano.Compiler;

/// <summary>
/// Target-agnostic Roslyn-side predicates and formatters used across the
/// pipeline. Keeps the "is this a Dictionary-shaped BCL type?" / version
/// formatting logic in one place so the IR extractor and the per-target
/// transformers don't drift on the answer.
/// </summary>
public static class RoslynTypeQueries
{
    private static readonly string[] DictionaryTypeNames =
    [
        "System.Collections.Generic.Dictionary",
        "System.Collections.Generic.IDictionary",
        "System.Collections.Generic.IReadOnlyDictionary",
        "System.Collections.Concurrent.ConcurrentDictionary",
        "System.Collections.Immutable.ImmutableDictionary",
        "System.Collections.Immutable.IImmutableDictionary",
    ];

    private static readonly string[] SetTypeNames =
    [
        "System.Collections.Generic.HashSet",
        "System.Collections.Generic.ISet",
        "System.Collections.Generic.SortedSet",
        "System.Collections.Immutable.ImmutableHashSet",
    ];

    /// <summary>
    /// Formats an assembly's version as an npm-compatible specifier.
    /// Assemblies that don't declare a version (Roslyn defaults to
    /// <c>0.0.0.0</c>) get <c>workspace:*</c>, which is the right call for
    /// sibling projects in a Bun monorepo. Anything with a real version
    /// becomes <c>^Major.Minor.Patch</c>.
    /// </summary>
    public static string FormatAssemblyVersion(IAssemblySymbol assembly)
    {
        var v = assembly.Identity.Version;
        if (v.Major == 0 && v.Minor == 0 && v.Build <= 0)
            return "workspace:*";
        var build = v.Build > 0 ? v.Build : 0;
        return $"^{v.Major}.{v.Minor}.{build}";
    }

    /// <summary>Whether <paramref name="type"/> is <c>Nullable&lt;T&gt;</c> (<c>int?</c>, <c>DateOnly?</c>).</summary>
    public static bool IsNullableValueType(this ITypeSymbol? type) =>
        type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T };

    /// <summary>
    /// <c>T</c> for <c>Nullable&lt;T&gt;</c>, <paramref name="type"/> itself
    /// otherwise. The non-nullable view of <see cref="UnderlyingValueType"/>:
    /// a <c>Nullable&lt;T&gt;</c> always carries its argument, so a non-null
    /// input never yields null and the assertion below cannot fire.
    /// </summary>
    public static ITypeSymbol UnwrapNullableValueType(this ITypeSymbol type) =>
        type.UnderlyingValueType()!;

    /// <summary><c>T</c> for <c>Nullable&lt;T&gt;</c>, the type itself otherwise.</summary>
    public static ITypeSymbol? UnderlyingValueType(this ITypeSymbol? type) =>
        type
            is INamedTypeSymbol
            {
                OriginalDefinition.SpecialType: SpecialType.System_Nullable_T,
                TypeArguments: [var underlying],
            }
            ? underlying
            : type;

    /// <summary>Whether <paramref name="type"/> is <c>System.Numerics.BigInteger</c>.</summary>
    public static bool IsBigInteger(this ITypeSymbol? type) =>
        type?.ToDisplayString() == "System.Numerics.BigInteger";

    /// <summary>
    /// True for the BCL dictionary types (and their nested types, such as
    /// <c>Dictionary&lt;K,V&gt;.KeyCollection</c>, which callers check first). A
    /// name matches only up to a generic boundary (<c>&lt;</c> or <c>`</c>), so
    /// <c>ImmutableDictionary</c> does not also match a type that merely starts with
    /// the same characters.
    /// </summary>
    public static bool IsDictionaryLike(this INamedTypeSymbol type)
    {
        var fullName = type.ToDisplayString();

        return DictionaryTypeNames.Any(name => HasGenericNamePrefix(fullName, name));
    }

    private static bool HasGenericNamePrefix(string fullName, string name) =>
        fullName.StartsWith(name, StringComparison.Ordinal)
        && (fullName.Length == name.Length || fullName[name.Length] is '<' or '`');

    /// <summary>
    /// True for <c>Dictionary&lt;K,V&gt;</c>, <c>IDictionary&lt;K,V&gt;</c> and
    /// <c>IReadOnlyDictionary&lt;K,V&gt;</c> themselves (not their nested
    /// <c>KeyCollection</c> / <c>ValueCollection</c> types, nor types that
    /// derive from or implement them): the dictionary types the
    /// declarative BCL mappings cover.
    /// </summary>
    public static bool IsBclDictionaryDefinition(this INamedTypeSymbol type) =>
        type.ContainingType is null
        && type.ContainingNamespace?.ToDisplayString() == "System.Collections.Generic"
        && (type.MetadataName is "Dictionary`2" or "IDictionary`2" or "IReadOnlyDictionary`2");

    /// <summary>
    /// The element type of <c>Dictionary&lt;K,V&gt;.KeyCollection</c>
    /// (<c>K</c>) or <c>Dictionary&lt;K,V&gt;.ValueCollection</c>
    /// (<c>V</c>); <c>null</c> for any other type.
    /// </summary>
    public static ITypeSymbol? DictionaryViewElementType(this INamedTypeSymbol type)
    {
        if (type.ContainingType is not { } dictionary || !dictionary.IsBclDictionaryDefinition())
            return null;

        return type.Name switch
        {
            "KeyCollection" => dictionary.TypeArguments[0],
            "ValueCollection" => dictionary.TypeArguments[1],
            _ => null,
        };
    }

    /// <summary>
    /// True for the BCL set types (and their nested types). A name matches
    /// only up to a generic boundary, as in <see cref="IsDictionaryLike"/>.
    /// </summary>
    public static bool IsSetLike(this INamedTypeSymbol type)
    {
        var fullName = type.ToDisplayString();

        return SetTypeNames.Any(name => HasGenericNamePrefix(fullName, name));
    }

    /// <summary>
    /// True for <c>SortedDictionary&lt;K,V&gt;</c> and <c>SortedList&lt;K,V&gt;</c>
    /// themselves: dictionaries the type mapper does not map to <c>Map</c>,
    /// but whose collection expressions construct one.
    /// </summary>
    public static bool IsBclSortedDictionaryDefinition(this INamedTypeSymbol type) =>
        type.ContainingType is null
        && type.ContainingNamespace?.ToDisplayString() == "System.Collections.Generic"
        && type.MetadataName is "SortedDictionary`2" or "SortedList`2";

    /// <summary>
    /// True for BCL types that map to a TypeScript <c>Array</c> at the
    /// IR level. <c>IEnumerable&lt;T&gt;</c> is intentionally excluded
    /// — it represents a lazy sequence and lowers to
    /// <c>IrIterableTypeRef</c> (TS <c>Iterable&lt;T&gt;</c>) instead;
    /// see the dedicated branch in <see cref="Metano.Compiler.Extraction.IrTypeRefMapper"/>.
    /// </summary>
    public static bool IsCollectionLike(this INamedTypeSymbol type)
    {
        var fullName = type.ToDisplayString();
        return fullName.StartsWith("System.Collections.Generic.List")
            || fullName.StartsWith("System.Collections.Generic.IList")
            || fullName.StartsWith("System.Collections.Generic.IReadOnlyList")
            || fullName.StartsWith("System.Collections.Generic.ICollection")
            || fullName.StartsWith("System.Collections.Immutable.ImmutableList")
            || fullName.StartsWith("System.Collections.Immutable.ImmutableArray")
            || fullName.StartsWith("System.Collections.Generic.Queue")
            || fullName.StartsWith("System.Collections.Generic.Stack");
    }
}
