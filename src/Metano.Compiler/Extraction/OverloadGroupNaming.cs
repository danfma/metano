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
    /// <summary>
    /// Records a method group's suffixes on its declarations: each parameter's
    /// spelling, the overload name and the signature text.
    /// </summary>
    /// <param name="overloads">The group's methods and their extracted
    /// declarations.</param>
    /// <param name="suffixOf">Each method's suffix, as
    /// <see cref="MethodOverloadGroups"/> computed it (root-anchored).</param>
    public static IReadOnlyDictionary<IMethodSymbol, IrMethodDeclaration> Apply(
        IReadOnlyList<(IMethodSymbol Symbol, IrMethodDeclaration Declaration)> overloads,
        Func<IMethodSymbol, string> suffixOf
    ) =>
        overloads.ToDictionary(
            overload => overload.Symbol,
            overload =>
                overload.Declaration with
                {
                    Parameters = WithNamingSpellings(
                        overload.Symbol,
                        overload.Declaration.Parameters
                    ),
                    OverloadName = new IrOverloadName(suffixOf(overload.Symbol)),
                    SignatureText = OverloadSignatureText.Of(overload.Symbol),
                },
            (IEqualityComparer<IMethodSymbol>)SymbolEqualityComparer.Default
        );

    /// <summary>
    /// The suffixes of a group of methods, in the group's order (spec 013
    /// FR-001), read from the Roslyn symbols: each parameter's C# name and its
    /// <see cref="IrNamingSpelling"/>. A set the climb cannot separate reports
    /// <c>MS0003</c> at its later overload (FR-024).
    /// </summary>
    public static IReadOnlyList<string> SuffixesOf(IReadOnlyList<IMethodSymbol> symbols)
    {
        var suffixes = OverloadNamePolicy.Compute(
            symbols.Select(SignatureOf).ToList(),
            KindOf(symbols)
        );

        foreach (var deadEnd in suffixes.DeadEnds)
            ReportDeadEnd(deadEnd, symbols, suffixes.Suffixes);

        return suffixes.Suffixes;
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
        var suffixes = SuffixesOf(constructors.Select(constructor => constructor.Symbol).ToList());

        return constructors
            .Select(
                (constructor, index) =>
                    constructor.Declaration with
                    {
                        Parameters = constructor
                            .Declaration.Parameters.Zip(
                                WithNamingSpellings(
                                    constructor.Symbol,
                                    constructor.Declaration.PlainParameters
                                ),
                                (parameter, spelled) => parameter with { Parameter = spelled }
                            )
                            .ToList(),
                        OverloadName = new IrOverloadName(suffixes[index]),
                        SignatureText = OverloadSignatureText.Of(constructor.Symbol),
                        DeclarationOrdinal = index,
                    }
            )
            .ToList();
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

    private static OverloadSignature SignatureOf(IMethodSymbol symbol) =>
        new(
            symbol
                .Parameters.Select(parameter => new OverloadParameter(
                    parameter.Name,
                    IrNamingSpellingExtractor.Extract(parameter)
                ))
                .ToList(),
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

        return $"{OverloadSignatureText.DeclaredNameOf(method)}({string.Join(", ", parameters)})";
    }

    /// <summary>Requires two or more items: a dead end holds at least two overloads.</summary>
    private static string JoinQuoted(IEnumerable<string> items)
    {
        var quoted = items.Select(item => $"'{item}'").ToList();

        return string.Join(", ", quoted.Take(quoted.Count - 1)) + $" and {quoted[^1]}";
    }
}
