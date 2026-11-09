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
/// suffix (FR-005). A set of overloads the climb cannot separate reports
/// <c>MS0003</c> at its later overload (FR-024), and so does an invalid
/// <c>[Name]</c> pin, at its attribute.
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
    /// <see cref="IrNamingSpelling"/>, or the suffix a <c>[Name]</c> for
    /// <paramref name="target"/> pins (FR-005). A set the climb cannot
    /// separate reports <c>MS0003</c> at its later overload (FR-024); an
    /// invalid pin reports <c>MS0003</c> at its attribute and is not pinned.
    /// </summary>
    public static IReadOnlyList<string> SuffixesOf(
        IReadOnlyList<IMethodSymbol> symbols,
        TargetLanguage? target
    )
    {
        var pins = symbols.Select(symbol => OverloadSuffixPin.Of(symbol, target)).ToList();

        var suffixes = OverloadNamePolicy.Compute(
            symbols.Select((symbol, index) => SignatureOf(symbol, pins[index]?.Suffix)).ToList(),
            KindOf(symbols)
        );

        for (var index = 0; index < symbols.Count; index++)
        {
            if (pins[index] is { IsValid: false } invalid)
                ReportInvalidPin(symbols[index], invalid);
        }

        foreach (var deadEnd in suffixes.DeadEnds)
            ReportDeadEnd(deadEnd, symbols, suffixes.Suffixes, pins);

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

    private static OverloadSignature SignatureOf(IMethodSymbol symbol, string? pinnedSuffix) =>
        new(
            symbol
                .Parameters.Select(parameter => new OverloadParameter(
                    parameter.Name,
                    IrNamingSpellingExtractor.Extract(parameter)
                ))
                .ToList(),
            symbol.MethodKind == MethodKind.Conversion
                ? IrNamingSpellingExtractor.Extract(symbol.ReturnType)
                : null,
            pinnedSuffix
        );

    private static OverloadGroupKind KindOf(IReadOnlyList<IMethodSymbol> symbols)
    {
        if (symbols.All(symbol => symbol.MethodKind == MethodKind.UserDefinedOperator))
            return OverloadGroupKind.Operator;

        return symbols.All(symbol => symbol.IsExtensionMethod)
            ? OverloadGroupKind.ExtensionMethod
            : OverloadGroupKind.Method;
    }

    /// <summary>
    /// A <c>[Name]</c> on an override or interface implementation that names
    /// another suffix than its root's: the root's names win (FR-025), so the
    /// pin is ignored and reported (FR-005). Repeating the root's suffix is
    /// silent.
    /// </summary>
    public static void ReportIgnoredPin(
        IMethodSymbol overload,
        IMethodSymbol root,
        string rootSuffix,
        TargetLanguage? target
    )
    {
        if (OverloadSuffixPin.Of(overload, target) is not { Location: { } location } pin)
            return;

        if (pin.Suffix == rootSuffix)
            return;

        var rootSignature = OverloadSignatureText.Of(root.OriginalDefinition);

        ExtractionDiagnostics.Report(
            new MetanoDiagnostic(
                MetanoDiagnosticSeverity.Warning,
                DiagnosticCodes.AmbiguousConstruct,
                $"[Name(\"{pin.Value}\")] on '{overload.ContainingType.Name}."
                    + $"{DisplaySignatureOf(overload)}' is ignored: an override or interface "
                    + $"implementation takes the suffix '{rootSuffix}' of its root "
                    + $"'{root.ContainingType.Name}.{rootSignature}'; put [Name] on the root "
                    + "(spec 013 FR-005, FR-025).",
                location
            )
        );
    }

    /// <summary>
    /// An invalid <c>[Name]</c> on an overload: the overload keeps its
    /// generated suffix, and the error fails the build (FR-005). The message
    /// names no suffix, so the report is the same from every group that
    /// sees the overload (a derived type's visible group included).
    /// </summary>
    private static void ReportInvalidPin(IMethodSymbol overload, OverloadSuffixPin pin)
    {
        if (pin.Location is not { } location)
            return;

        ExtractionDiagnostics.Report(
            new MetanoDiagnostic(
                MetanoDiagnosticSeverity.Error,
                DiagnosticCodes.AmbiguousConstruct,
                $"[Name(\"{pin.Value}\")] on the overload '{DisplaySignatureOf(overload)}' is not a "
                    + "valid suffix: it must start with an ASCII letter or '_' and contain only "
                    + "ASCII letters, digits and '_'; the overload keeps its generated suffix "
                    + "(spec 013 FR-005).",
                location
            )
        );
    }

    private static void ReportDeadEnd(
        IReadOnlyList<int> deadEnd,
        IReadOnlyList<IMethodSymbol> symbols,
        IReadOnlyList<string> suffixes,
        IReadOnlyList<OverloadSuffixPin?> pins
    )
    {
        var signatures = JoinQuoted(deadEnd.Select(index => DisplaySignatureOf(symbols[index])));
        var names = JoinQuoted(deadEnd.Select(index => suffixes[index]));
        var pinned = deadEnd.Where(index => pins[index]?.Suffix is not null).ToList();

        var cause =
            pinned.Count == 0
                ? "have the same naming spelling at every qualification level"
                : "have the same suffix at every qualification level, and [Name] pins the suffix of "
                    + JoinQuoted(pinned.Select(index => DisplaySignatureOf(symbols[index])));

        ExtractionDiagnostics.Report(
            new MetanoDiagnostic(
                MetanoDiagnosticSeverity.Error,
                DiagnosticCodes.AmbiguousConstruct,
                $"Overloads {signatures} {cause}; they are named with the suffixes {names} "
                    + (pinned.Count == 0 ? "(spec 013 FR-024)." : "(spec 013 FR-005, FR-024)."),
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

    private static string JoinQuoted(IEnumerable<string> items)
    {
        var quoted = items.Select(item => $"'{item}'").ToList();

        return quoted.Count == 1
            ? quoted[0]
            : string.Join(", ", quoted.Take(quoted.Count - 1)) + $" and {quoted[^1]}";
    }
}
