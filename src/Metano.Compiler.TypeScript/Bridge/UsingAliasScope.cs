namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// A bi-directional alias map for the C# <c>using X = Y;</c> directives in
/// scope of one emitted TypeScript file. <see cref="CanonicalToAlias"/>
/// drives substitution at type-name emission time; <see cref="AliasToCanonical"/>
/// lets the import collector resolve a referenced alias back to the
/// canonical type so the matching <c>{ Original as Alias }</c> import line
/// can be produced.
/// </summary>
public sealed class UsingAliasScope
{
    /// <param name="canonicalToAlias">The alias each canonical type prints as.</param>
    /// <param name="otherAliases">Further aliases of those types
    /// (<c>using Money = System.Decimal; using Cash = System.Decimal;</c>),
    /// alias → canonical: they print nowhere the mapper names the type, but
    /// a value position keeps the source spelling, so the import collector
    /// must still resolve them.</param>
    public UsingAliasScope(
        IReadOnlyDictionary<string, string> canonicalToAlias,
        IReadOnlyDictionary<string, string>? otherAliases = null
    )
    {
        CanonicalToAlias = canonicalToAlias;
        var inverse = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (alias, canonical) in otherAliases ?? new Dictionary<string, string>())
            inverse[alias] = canonical;

        foreach (var (canonical, alias) in canonicalToAlias)
            inverse[alias] = canonical;

        AliasToCanonical = inverse;
    }

    public IReadOnlyDictionary<string, string> CanonicalToAlias { get; }
    public IReadOnlyDictionary<string, string> AliasToCanonical { get; }

    /// <summary>
    /// The aliases of this scope that are not the printed alias of their type.
    /// </summary>
    public IReadOnlyDictionary<string, string> OtherAliases =>
        AliasToCanonical
            .Where(entry => CanonicalToAlias.GetValueOrDefault(entry.Value) != entry.Key)
            .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal);

    public static UsingAliasScope? Create(
        IReadOnlyDictionary<string, string> canonicalToAlias,
        IReadOnlyDictionary<string, string>? otherAliases = null
    ) => canonicalToAlias.Count == 0 ? null : new UsingAliasScope(canonicalToAlias, otherAliases);
}
