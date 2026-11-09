using Metano.Annotations;
using Metano.Compiler.Diagnostics;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Extraction;

/// <summary>
/// Names the overloads of one dispatched group (spec 013 FR-001), a method
/// group or a type's constructors: captures each parameter's
/// <see cref="IrNamingSpelling"/> from its Roslyn symbol, runs
/// <see cref="OverloadNamePolicy"/> and records the result on each
/// declaration's <c>OverloadName</c>. A <c>[Name]</c> on an overload pins its
/// suffix (FR-005, <see cref="OverloadSuffixPin"/>). A set of overloads the
/// climb cannot separate reports <c>MS0003</c> at its later overload (FR-024).
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
    /// FR-001), each overload pinned by its own <c>[Name]</c> for
    /// <paramref name="target"/> (FR-005).
    /// </summary>
    public static IReadOnlyList<string> SuffixesOf(
        IReadOnlyList<IMethodSymbol> symbols,
        TargetLanguage? target
    ) =>
        SuffixesOf(
            symbols
                .Select(symbol => new PinnedOverload(symbol, OverloadSuffixPin.Of(symbol, target)))
                .ToList()
        );

    /// <summary>
    /// The suffixes of a group of methods, in the group's order (spec 013
    /// FR-001), read from the Roslyn symbols: each parameter's C# name and its
    /// <see cref="IrNamingSpelling"/>, or the suffix its pin carries (FR-005).
    /// A set the climb cannot separate reports <c>MS0003</c> at its later
    /// overload (FR-024); an invalid pin reports <c>MS0003</c> at its
    /// attribute and pins nothing.
    /// </summary>
    public static IReadOnlyList<string> SuffixesOf(IReadOnlyList<PinnedOverload> overloads)
    {
        var symbols = overloads.Select(overload => overload.Symbol).ToList();

        var suffixes = OverloadNamePolicy.Compute(
            overloads.Select(SignatureOf).ToList(),
            KindOf(symbols)
        );

        foreach (var overload in overloads)
            overload.Pin?.ReportIfInvalid();

        foreach (var deadEnd in suffixes.DeadEnds)
            ReportDeadEnd(deadEnd.Select(index => overloads[index]).ToList(), deadEnd, suffixes);

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
        IReadOnlyList<(IMethodSymbol Symbol, IrConstructorDeclaration Declaration)> constructors,
        TargetLanguage? target
    )
    {
        var suffixes = SuffixesOf(
            constructors.Select(constructor => constructor.Symbol).ToList(),
            target
        );

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

    private static OverloadSignature SignatureOf(PinnedOverload overload) =>
        new(
            overload
                .Symbol.Parameters.Select(parameter => new OverloadParameter(
                    parameter.Name,
                    IrNamingSpellingExtractor.Extract(parameter)
                ))
                .ToList(),
            overload.Symbol.MethodKind == MethodKind.Conversion
                ? IrNamingSpellingExtractor.Extract(overload.Symbol.ReturnType)
                : null,
            overload.Pin?.Suffix
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
        IReadOnlyList<PinnedOverload> deadEnd,
        IReadOnlyList<int> indexes,
        OverloadSuffixes suffixes
    )
    {
        var signatures = JoinQuoted(
            deadEnd.Select(overload => OverloadSignatureText.DisplayOf(overload.Symbol))
        );
        var names = JoinQuoted(indexes.Select(index => suffixes.Suffixes[index]));

        ExtractionDiagnostics.Report(
            new MetanoDiagnostic(
                MetanoDiagnosticSeverity.Error,
                DiagnosticCodes.AmbiguousConstruct,
                $"Overloads {signatures} {DeadEndCauseOf(deadEnd)}; they are named with the "
                    + $"suffixes {names} {DeadEndReferenceOf(deadEnd)}",
                deadEnd[^1].Symbol.Locations.FirstOrDefault()
            )
        );
    }

    private static string DeadEndCauseOf(IReadOnlyList<PinnedOverload> deadEnd)
    {
        var pinned = PinnedIn(deadEnd);

        if (pinned.Count == 0)
            return "have the same naming spelling at every qualification level";

        var pinnedSignatures = JoinQuoted(
            pinned.Select(overload => OverloadSignatureText.DisplayOf(overload.Symbol))
        );

        return "have the same suffix at every qualification level, and [Name] pins the suffix "
            + $"of {pinnedSignatures}";
    }

    private static string DeadEndReferenceOf(IReadOnlyList<PinnedOverload> deadEnd) =>
        PinnedIn(deadEnd).Count == 0 ? "(spec 013 FR-024)." : "(spec 013 FR-005, FR-024).";

    private static List<PinnedOverload> PinnedIn(IReadOnlyList<PinnedOverload> deadEnd) =>
        deadEnd.Where(overload => overload.Pin?.Suffix is not null).ToList();

    private static string JoinQuoted(IEnumerable<string> items)
    {
        var quoted = items.Select(item => $"'{item}'").ToList();

        return quoted.Count == 1
            ? quoted[0]
            : string.Join(", ", quoted.Take(quoted.Count - 1)) + $" and {quoted[^1]}";
    }
}

/// <summary>
/// One overload as its group's naming reads it: the symbol whose signature is
/// named and the pin that names it — its own <c>[Name]</c>, or, for an
/// override or interface implementation, its root's (FR-005, FR-025).
/// </summary>
internal sealed record PinnedOverload(IMethodSymbol Symbol, OverloadSuffixPin? Pin);
