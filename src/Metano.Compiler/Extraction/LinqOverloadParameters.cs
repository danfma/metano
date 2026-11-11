using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Extraction;

/// <summary>
/// The identity of a .NET LINQ overload (#648): the names of its parameters
/// after the source, in declaration order (<c>(seed, func)</c>), compared
/// ordinally as a sequence. <c>default</c> is not a valid value.
/// </summary>
public readonly record struct LinqOverloadParameters
{
    private readonly ImmutableArray<string> _names;

    private LinqOverloadParameters(ImmutableArray<string> names) => _names = names;

    /// <summary>The parameter names, in declaration order.</summary>
    public IReadOnlyList<string> Names =>
        _names.IsDefault
            ? throw new InvalidOperationException(
                "default(LinqOverloadParameters) names no overload."
            )
            : _names;

    /// <summary>Whether the overload takes a comparer.</summary>
    public bool PassesComparer => Names.Contains(ComparerParameter.Name, StringComparer.Ordinal);

    /// <summary>The same overload without its comparer parameter.</summary>
    public LinqOverloadParameters WithoutComparer() =>
        new([.. Names.Where(name => name != ComparerParameter.Name)]);

    public bool Equals(LinqOverloadParameters other) =>
        Names.SequenceEqual(other.Names, StringComparer.Ordinal);

    public override int GetHashCode()
    {
        var hash = new HashCode();

        foreach (var name in Names)
            hash.Add(name, StringComparer.Ordinal);

        return hash.ToHashCode();
    }

    /// <summary>The overload as messages name it: <c>Aggregate(seed, func)</c>.</summary>
    public string ToString(string methodName) => $"{methodName}({string.Join(", ", Names)})";

    public override string ToString() => ToString(string.Empty);

    /// <summary>
    /// The overload taking <paramref name="names"/>, none for an overload with
    /// no parameter after the source.
    /// </summary>
    public static LinqOverloadParameters Named(params string[] names)
    {
        if (names.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("A LINQ overload parameter needs a name.", nameof(names));

        return new([.. names]);
    }

    /// <summary>The overload <paramref name="method"/> binds to.</summary>
    public static LinqOverloadParameters From(IMethodSymbol method) =>
        new([.. (method.ReducedFrom ?? method).Parameters.Skip(1).Select(p => p.Name)]);
}
