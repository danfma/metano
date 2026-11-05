using Metano.Compiler.Diagnostics;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Extraction;

/// <summary>
/// Names the overloads of one dispatched method group (spec 013 FR-001):
/// captures each parameter's <see cref="IrNamingSpelling"/> from its Roslyn
/// symbol, runs <see cref="OverloadNamePolicy"/> and records the result on each
/// <see cref="IrMethodDeclaration.OverloadName"/>. A set of overloads the climb
/// cannot separate reports <c>MS0003</c> at its later overload (FR-024).
/// </summary>
internal static class OverloadGroupNaming
{
    /// <param name="overloads">The group's methods and their extracted
    /// declarations, in declaration order.</param>
    /// <returns>Each method's declaration carrying its parameters' spellings and
    /// its overload name.</returns>
    public static IReadOnlyDictionary<IMethodSymbol, IrMethodDeclaration> Apply(
        IReadOnlyList<(IMethodSymbol Symbol, IrMethodDeclaration Declaration)> overloads
    )
    {
        var symbols = overloads.Select(overload => overload.Symbol).ToList();

        var spelled = overloads
            .Select(overload => WithNamingSpellings(overload.Symbol, overload.Declaration))
            .ToList();

        var suffixes = OverloadNamePolicy.Compute(
            spelled
                .Select((declaration, index) => SignatureOf(symbols[index], declaration))
                .ToList(),
            KindOf(symbols)
        );

        foreach (var deadEnd in suffixes.DeadEnds)
            ReportDeadEnd(deadEnd, symbols, suffixes.Suffixes);

        return symbols
            .Select(
                (symbol, index) =>
                    (
                        Symbol: symbol,
                        Declaration: spelled[index] with
                        {
                            OverloadName = new IrOverloadName(suffixes.Suffixes[index]),
                        }
                    )
            )
            .ToDictionary(
                named => named.Symbol,
                named => named.Declaration,
                (IEqualityComparer<IMethodSymbol>)SymbolEqualityComparer.Default
            );
    }

    private static IrMethodDeclaration WithNamingSpellings(
        IMethodSymbol symbol,
        IrMethodDeclaration declaration
    )
    {
        if (symbol.Parameters.Length != declaration.Parameters.Count)
            throw new InvalidOperationException(
                $"'{symbol.Name}' was extracted with {declaration.Parameters.Count} of its "
                    + $"{symbol.Parameters.Length} parameters."
            );

        return declaration with
        {
            Parameters = symbol
                .Parameters.Zip(
                    declaration.Parameters,
                    (parameterSymbol, parameter) =>
                        parameter with
                        {
                            NamingSpelling = IrNamingSpellingExtractor.Extract(parameterSymbol),
                        }
                )
                .ToList(),
        };
    }

    private static OverloadSignature SignatureOf(
        IMethodSymbol symbol,
        IrMethodDeclaration declaration
    ) =>
        new(
            declaration.Parameters,
            symbol.MethodKind == MethodKind.Conversion
                ? IrNamingSpellingExtractor.Extract(symbol.ReturnType)
                : null
        );

    private static OverloadGroupKind KindOf(IReadOnlyList<IMethodSymbol> symbols)
    {
        if (symbols.All(symbol => symbol.MethodKind == MethodKind.UserDefinedOperator))
            return OverloadGroupKind.Operator;

        return symbols.All(symbol => symbol.IsExtensionMethod)
            ? OverloadGroupKind.ExtensionMethod
            : OverloadGroupKind.Method;
    }

    private static void ReportDeadEnd(
        IReadOnlyList<int> deadEnd,
        IReadOnlyList<IMethodSymbol> symbols,
        IReadOnlyList<string> suffixes
    )
    {
        var signatures = JoinQuoted(deadEnd.Select(index => DisplaySignatureOf(symbols[index])));
        var names = JoinQuoted(deadEnd.Select(index => suffixes[index]));

        ExtractionDiagnostics.Report(
            new MetanoDiagnostic(
                MetanoDiagnosticSeverity.Error,
                DiagnosticCodes.AmbiguousConstruct,
                $"Overloads {signatures} have the same naming spelling at every qualification "
                    + $"level; they are named with the suffixes {names} (spec 013 FR-024).",
                symbols[deadEnd[^1]].Locations.FirstOrDefault()
            )
        );
    }

    private static string DisplaySignatureOf(IMethodSymbol method)
    {
        var parameters = method.Parameters.Select(parameter =>
            $"{parameter.Type.ToDisplayString()} {parameter.Name}"
        );

        return $"{method.Name}({string.Join(", ", parameters)})";
    }

    /// <summary>Requires two or more items: a dead end holds at least two overloads.</summary>
    private static string JoinQuoted(IEnumerable<string> items)
    {
        var quoted = items.Select(item => $"'{item}'").ToList();

        return string.Join(", ", quoted.Take(quoted.Count - 1)) + $" and {quoted[^1]}";
    }
}
