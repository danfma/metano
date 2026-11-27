using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Analysis;

/// <summary>
/// The type arguments the frontend reported (MS0001, spec 011 W5-9 R4) as passed to a
/// hidden reified parameter without a runtime descriptor, by call site, so a later phase
/// that reaches the same argument does not report it again.
/// </summary>
public sealed class UndescribableArgumentReports
{
    public static readonly UndescribableArgumentReports None = new();

    private readonly Dictionary<Location, HashSet<ITypeSymbol>> _reported = [];

    /// <summary>Whether <paramref name="type"/> was reported at <paramref name="location"/>.</summary>
    public bool Contains(ITypeSymbol type, Location location) =>
        _reported.TryGetValue(location, out var types) && types.Contains(type);

    internal void Add(ITypeSymbol type, Location location)
    {
        if (!_reported.TryGetValue(location, out var types))
        {
            types = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
            _reported[location] = types;
        }

        types.Add(type);
    }
}
