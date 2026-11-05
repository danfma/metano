using Metano.Compiler.Diagnostics;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Extraction;

/// <summary>
/// Names the overloads of one dispatched group (spec 013 FR-001), a method
/// group or a type's constructors: captures each parameter's
/// <see cref="IrNamingSpelling"/> from its Roslyn symbol, runs
/// <see cref="OverloadNamePolicy"/> and records the result on each
/// declaration's <c>OverloadName</c>. A set of overloads the climb cannot
/// separate reports <c>MS0003</c> at its later overload (FR-024).
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

        var named = Name(
            symbols,
            overloads.Select(overload => overload.Declaration.Parameters).ToList(),
            KindOf(symbols)
        );

        return symbols
            .Select(
                (symbol, index) =>
                    (
                        Symbol: symbol,
                        Declaration: overloads[index].Declaration with
                        {
                            Parameters = named.Parameters[index],
                            OverloadName = new IrOverloadName(named.Suffixes[index]),
                            SignatureText = OverloadSignatureText.Of(symbol),
                        }
                    )
            )
            .ToDictionary(
                pair => pair.Symbol,
                pair => pair.Declaration,
                (IEqualityComparer<IMethodSymbol>)SymbolEqualityComparer.Default
            );
    }

    /// <summary>
    /// Names the declared constructors of a type, a dispatched set of two or
    /// more (spec 013 FR-007, FR-008).
    /// </summary>
    /// <param name="constructors">The constructors and their extracted
    /// declarations, in declaration order.</param>
    /// <returns>The declarations in the same order, each carrying its
    /// parameters' spellings, its overload name, its signature text and its
    /// declaration ordinal.</returns>
    public static IReadOnlyList<IrConstructorDeclaration> ApplyToConstructors(
        IReadOnlyList<(IMethodSymbol Symbol, IrConstructorDeclaration Declaration)> constructors
    )
    {
        var symbols = constructors.Select(constructor => constructor.Symbol).ToList();

        var named = Name(
            symbols,
            constructors.Select(constructor => constructor.Declaration.PlainParameters).ToList(),
            OverloadGroupKind.Method
        );

        return constructors
            .Select(
                (constructor, index) =>
                    constructor.Declaration with
                    {
                        Parameters = constructor
                            .Declaration.Parameters.Zip(
                                named.Parameters[index],
                                (parameter, spelled) => parameter with { Parameter = spelled }
                            )
                            .ToList(),
                        OverloadName = new IrOverloadName(named.Suffixes[index]),
                        SignatureText = OverloadSignatureText.Of(constructor.Symbol),
                        DeclarationOrdinal = index,
                    }
            )
            .ToList();
    }

    /// <summary>
    /// Spells every overload's parameters and computes the group's suffixes,
    /// reporting each dead end.
    /// </summary>
    private static NamedGroup Name(
        IReadOnlyList<IMethodSymbol> symbols,
        IReadOnlyList<IReadOnlyList<IrParameter>> parameters,
        OverloadGroupKind kind
    )
    {
        var spelled = symbols
            .Select((symbol, index) => WithNamingSpellings(symbol, parameters[index]))
            .ToList();

        var suffixes = OverloadNamePolicy.Compute(
            spelled.Select((overload, index) => SignatureOf(symbols[index], overload)).ToList(),
            kind
        );

        foreach (var deadEnd in suffixes.DeadEnds)
            ReportDeadEnd(deadEnd, symbols, suffixes.Suffixes);

        return new NamedGroup(spelled, suffixes.Suffixes);
    }

    private static IReadOnlyList<IrParameter> WithNamingSpellings(
        IMethodSymbol symbol,
        IReadOnlyList<IrParameter> parameters
    )
    {
        if (symbol.Parameters.Length != parameters.Count)
            throw new InvalidOperationException(
                $"'{symbol.Name}' was extracted with {parameters.Count} of its "
                    + $"{symbol.Parameters.Length} parameters."
            );

        return symbol
            .Parameters.Zip(
                parameters,
                (parameterSymbol, parameter) =>
                    parameter with
                    {
                        NamingSpelling = IrNamingSpellingExtractor.Extract(parameterSymbol),
                    }
            )
            .ToList();
    }

    private static OverloadSignature SignatureOf(
        IMethodSymbol symbol,
        IReadOnlyList<IrParameter> parameters
    ) =>
        new(
            parameters,
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

        return $"{DeclaredNameOf(method)}({string.Join(", ", parameters)})";
    }

    /// <summary>The method's name, or a constructor's type name.</summary>
    private static string DeclaredNameOf(IMethodSymbol method) =>
        method.MethodKind == MethodKind.Constructor ? method.ContainingType.Name : method.Name;

    /// <summary>Requires two or more items: a dead end holds at least two overloads.</summary>
    private static string JoinQuoted(IEnumerable<string> items)
    {
        var quoted = items.Select(item => $"'{item}'").ToList();

        return string.Join(", ", quoted.Take(quoted.Count - 1)) + $" and {quoted[^1]}";
    }

    /// <summary>The spelled parameters and the suffix of each overload of a group.</summary>
    private sealed record NamedGroup(
        IReadOnlyList<IReadOnlyList<IrParameter>> Parameters,
        IReadOnlyList<string> Suffixes
    );
}
