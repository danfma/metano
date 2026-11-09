using Metano.Compiler.Extraction;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// Joins the IR overloads of a dispatcher back to their Roslyn symbols, which
/// the IR does not carry, so a diagnostic can point at the declaration and
/// name the overload.
/// </summary>
internal static class OverloadSymbols
{
    private static readonly SymbolDisplayFormat SignatureFormat =
        SymbolDisplayFormat.CSharpShortErrorMessageFormat;

    internal static string Describe(IMethodSymbol? symbol) =>
        symbol?.ToDisplayString(SignatureFormat) ?? "an overload";

    /// <summary>The quoted, comma-separated signatures of <paramref name="symbols"/>.</summary>
    internal static string DescribeAll(IEnumerable<IMethodSymbol?> symbols) =>
        string.Join(", ", symbols.Select(symbol => $"'{Describe(symbol)}'"));

    /// <summary>
    /// The methods named <paramref name="name"/> declared by <paramref name="type"/>
    /// and everything it inherits or implements, which an inherited dispatcher's
    /// overloads are declared on.
    /// </summary>
    internal static List<IMethodSymbol> NamedInHierarchy(INamedTypeSymbol type, string name) =>
        BaseChain(type)
            .Concat(type.AllInterfaces)
            .SelectMany(declaring => declaring.GetMembers(name).OfType<IMethodSymbol>())
            .ToList();

    private static IEnumerable<INamedTypeSymbol> BaseChain(INamedTypeSymbol type)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            yield return current;
        }
    }

    /// <summary>
    /// The declared overload whose parameters match <paramref name="parameters"/>
    /// by name and <c>params</c> flag, then, among several, by mapped type.
    /// </summary>
    internal static IMethodSymbol? Of(
        IReadOnlyList<IMethodSymbol> symbols,
        IReadOnlyList<IrParameter> parameters
    )
    {
        var byName = symbols
            .Where(symbol => HasParameters(symbol, parameters, MatchesByName))
            .ToList();

        return byName.Count == 1
            ? byName[0]
            : byName.FirstOrDefault(symbol => HasParameters(symbol, parameters, MatchesByType));
    }

    private static bool HasParameters(
        IMethodSymbol symbol,
        IReadOnlyList<IrParameter> parameters,
        Func<IParameterSymbol, IrParameter, bool> matches
    ) =>
        symbol.Parameters.Length == parameters.Count
        && symbol.Parameters.Zip(parameters).All(pair => matches(pair.First, pair.Second));

    private static bool MatchesByName(IParameterSymbol symbol, IrParameter parameter) =>
        symbol.Name == parameter.Name && symbol.IsParams == parameter.IsParams;

    private static bool MatchesByType(IParameterSymbol symbol, IrParameter parameter) =>
        IrTypeRefSignatures.Describe(IrTypeRefMapper.Map(symbol.Type))
        == IrTypeRefSignatures.Describe(parameter.Type);
}
