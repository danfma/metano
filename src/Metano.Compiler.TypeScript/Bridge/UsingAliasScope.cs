using Microsoft.CodeAnalysis;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// The aliases in scope of one emitted TypeScript file, three layers deep: the
/// auto-synthesized fallbacks, the user's C# <c>using X = Y;</c> directives and the
/// <c>[ImportAlias]</c> pins, each layer over the one before. A <c>using</c> alias
/// names one type, so it applies to that type only (<see cref="UsingAlias"/>, #754);
/// a same-named type of another namespace keeps its own name. The synthesized and
/// pinned layers are keyed by the canonical name. <see cref="AliasToCanonical"/> lets
/// the import collector resolve a referenced alias back to the canonical type so the
/// matching <c>{ Original as Alias }</c> import line can be produced.
/// </summary>
public sealed class UsingAliasScope
{
    private readonly IReadOnlyDictionary<string, string> _synthesized;
    private readonly IReadOnlyDictionary<string, string> _pinned;
    private readonly IReadOnlyDictionary<UsingAliasTarget, string> _usings;
    private readonly IReadOnlyDictionary<string, INamedTypeSymbol> _usingTargets;

    private UsingAliasScope(
        IReadOnlyDictionary<string, string> synthesized,
        IReadOnlyList<UsingAlias> usings,
        IReadOnlyDictionary<string, string> pinned
    )
    {
        _synthesized = synthesized;
        _pinned = pinned;

        var targeted = new Dictionary<UsingAliasTarget, string>();
        var targets = new Dictionary<string, INamedTypeSymbol>(StringComparer.Ordinal);
        var inverse = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (canonical, alias) in synthesized)
            inverse[alias] = canonical;

        foreach (var alias in usings)
        {
            targeted[alias.Target] = alias.Alias;
            targets[alias.Alias] = alias.Symbol;
            inverse[alias.Alias] = alias.Target.Name;
        }

        foreach (var (canonical, alias) in pinned)
            inverse[alias] = canonical;

        _usings = targeted;
        _usingTargets = targets;
        AliasToCanonical = inverse;
    }

    /// <summary>
    /// Every alias of the scope → the canonical name of the type it names, including
    /// a <c>using</c> alias another layer displaces: it prints nowhere the mapper names
    /// the type, but a value position keeps the source spelling, so the import
    /// collector must still resolve it.
    /// </summary>
    public IReadOnlyDictionary<string, string> AliasToCanonical { get; }

    /// <summary>
    /// The alias the type <paramref name="name"/> of <paramref name="namespace"/> prints
    /// as, or <c>null</c> when it prints under its own name: an <c>[ImportAlias]</c> pin
    /// of the name, else a <c>using</c> alias of that very type, else a synthesized
    /// fallback for the name.
    /// </summary>
    public string? AliasOf(string name, string? @namespace)
    {
        if (_pinned.TryGetValue(name, out var pinned))
            return pinned;

        if (_usings.TryGetValue(new UsingAliasTarget(@namespace, name), out var alias))
            return alias;

        return _synthesized.GetValueOrDefault(name);
    }

    /// <summary>
    /// Whether the type <paramref name="name"/> of <paramref name="namespace"/> prints
    /// under a <c>using</c> alias, whose import binds by symbol
    /// (<see cref="UsingTargetOf"/>): a pin of the name displaces it.
    /// </summary>
    public bool PrintsUsingAliasOf(string name, string? @namespace) =>
        !_pinned.ContainsKey(name) && _usings.ContainsKey(new UsingAliasTarget(@namespace, name));

    /// <summary>
    /// The type a <c>using</c> alias names, which binds its import by symbol rather
    /// than by its canonical name (two aliases may name two same-named types);
    /// <c>null</c> for any other name.
    /// </summary>
    public INamedTypeSymbol? UsingTargetOf(string alias) => _usingTargets.GetValueOrDefault(alias);

    public static UsingAliasScope? Create(
        IReadOnlyDictionary<string, string>? synthesized,
        IReadOnlyList<UsingAlias>? usings,
        IReadOnlyDictionary<string, string>? pinned
    )
    {
        var isEmpty =
            synthesized is not { Count: > 0 }
            && usings is not { Count: > 0 }
            && pinned is not { Count: > 0 };

        if (isEmpty)
            return null;

        return new UsingAliasScope(
            synthesized ?? new Dictionary<string, string>(),
            usings ?? [],
            pinned ?? new Dictionary<string, string>()
        );
    }
}

/// <summary>
/// The type a <c>using</c> alias names, as the type mapper sees it: its namespace
/// (<c>null</c> for the global one) and its name.
/// </summary>
public sealed record UsingAliasTarget(string? Namespace, string Name);

/// <summary>A C# <c>using Alias = Target;</c> directive that renames a type.</summary>
public sealed record UsingAlias(string Alias, UsingAliasTarget Target, INamedTypeSymbol Symbol)
{
    /// <summary>The directive's location, for a diagnostic about the alias.</summary>
    public Location? Location { get; init; }
}
