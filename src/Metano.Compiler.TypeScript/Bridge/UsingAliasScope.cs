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
    public IReadOnlyDictionary<string, string> CanonicalToAlias { get; }
    public IReadOnlyDictionary<string, string> AliasToCanonical { get; }

    public UsingAliasScope(IReadOnlyDictionary<string, string> canonicalToAlias)
    {
        CanonicalToAlias = canonicalToAlias;
        var inverse = new Dictionary<string, string>(
            canonicalToAlias.Count,
            StringComparer.Ordinal
        );
        foreach (var (canonical, alias) in canonicalToAlias)
            inverse[alias] = canonical;
        AliasToCanonical = inverse;
    }

    public static UsingAliasScope? Create(IReadOnlyDictionary<string, string> canonicalToAlias) =>
        canonicalToAlias.Count == 0 ? null : new UsingAliasScope(canonicalToAlias);
}
