using Metano.Annotations;
using Metano.Compiler.Diagnostics;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Extraction;

/// <summary>
/// The one rule that names a user-defined conversion operator (baseline
/// FR-078, #600, spec 013 contracts/naming.md step 3): <c>To</c> + the target
/// type's spelling for a conversion out of its declaring type, <c>From</c> +
/// the source type's spelling for one into it, after <c>Implicit</c> or
/// <c>Checked</c> for those kinds (<see cref="IrConversionName.PrefixOf"/>);
/// a <c>[Name]</c> names the conversion whole. The
/// conversions of one type whose candidates collide climb with
/// <see cref="OverloadSpellingClimb"/>, and a set the climb cannot separate
/// is a dead end (<c>MS0003</c>). A pure function of the symbol, so the
/// declaration and every use, a metadata-only reference included, compute the
/// same name.
/// </summary>
internal static class ConversionNaming
{
    /// <summary>
    /// The name candidate of <paramref name="method"/> for
    /// <paramref name="target"/>; <c>null</c> when it is not a conversion
    /// operator.
    /// </summary>
    public static IrConversionName? Of(IMethodSymbol method, TargetLanguage? target) =>
        method.MethodKind == MethodKind.Conversion
            ? ConversionSet.Of(method.ContainingType, target).NameAt(method)
            : null;

    /// <summary>
    /// Reports <c>MS0003</c> at <paramref name="method"/> when the climb could
    /// not separate it from an earlier conversion of its type; the first
    /// conversion of a dead-end set is not reported, so the error appears once
    /// per later declaration.
    /// </summary>
    public static void ReportIfDeadEnd(IMethodSymbol method, TargetLanguage? target)
    {
        if (method.MethodKind != MethodKind.Conversion)
            return;

        var set = ConversionSet.Of(method.ContainingType, target);

        if (set.EarlierCollisionOf(method) is not { } first)
            return;

        ExtractionDiagnostics.Report(
            new MetanoDiagnostic(
                MetanoDiagnosticSeverity.Error,
                DiagnosticCodes.AmbiguousConstruct,
                $"Conversion operators '{DisplayOf(first)}' and '{DisplayOf(method)}' spell "
                    + $"the same name '{set.SpellingAt(method)}'; give one of them another "
                    + "[Name] (baseline FR-078).",
                method.Locations.FirstOrDefault()
            )
        );
    }

    private static string DisplayOf(IMethodSymbol conversion) =>
        conversion.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat);

    private static ConversionDraft DraftOf(IMethodSymbol conversion, TargetLanguage? target)
    {
        var direction = conversion.Parameters[0].Type.IsDeclaringTypeOf(conversion)
            ? IrConversionDirection.To
            : IrConversionDirection.From;

        var kind = KindOf(conversion);
        var prefix = IrConversionName.PrefixOf(kind, direction);
        var pin = PinOf(conversion, target);

        IReadOnlyList<IrNamingComponent> components = pin is not null
            ? [new IrNamingComponent(NamingText.Capitalize(pin), [])]
            : [new IrNamingComponent(prefix, []), .. ConvertedSpelling(conversion, direction)];

        return new ConversionDraft(direction, kind, prefix, pin, components);
    }

    private static IrConversionKind KindOf(IMethodSymbol conversion) =>
        conversion.Name switch
        {
            WellKnownMemberNames.ImplicitConversionName => IrConversionKind.Implicit,
            WellKnownMemberNames.CheckedExplicitConversionName => IrConversionKind.Checked,
            _ => IrConversionKind.Explicit,
        };

    private static IReadOnlyList<IrNamingComponent> ConvertedSpelling(
        IMethodSymbol conversion,
        IrConversionDirection direction
    )
    {
        var converted =
            direction is IrConversionDirection.To
                ? conversion.ReturnType
                : conversion.Parameters[0].Type;

        return IrNamingSpellingExtractor.Extract(converted).Components;
    }

    private static string? PinOf(IMethodSymbol conversion, TargetLanguage? target) =>
        conversion.FindNameOverrideAttribute(target) is { } attribute
            ? SymbolHelper.NameOverrideValueOf(attribute)
            : null;

    private sealed record ConversionDraft(
        IrConversionDirection Direction,
        IrConversionKind Kind,
        string Prefix,
        string? Pin,
        IReadOnlyList<IrNamingComponent> Components
    )
    {
        public IrConversionName NameFrom(string suffix) =>
            new(Direction, Kind, Pin is null ? suffix[Prefix.Length..] : "", Pin);
    }

    /// <summary>
    /// The conversion operators one type declares, in declaration order, with
    /// their drafts and the suffixes the climb gave them.
    /// </summary>
    private sealed class ConversionSet
    {
        private readonly IReadOnlyList<IMethodSymbol> _conversions;
        private readonly IReadOnlyList<ConversionDraft> _drafts;
        private readonly OverloadSuffixes _suffixes;

        private ConversionSet(
            IReadOnlyList<IMethodSymbol> conversions,
            IReadOnlyList<ConversionDraft> drafts
        )
        {
            _conversions = conversions;
            _drafts = drafts;

            _suffixes = OverloadSpellingClimb.Run(
                drafts.Select(draft => draft.Components).ToList()
            );
        }

        public static ConversionSet Of(INamedTypeSymbol type, TargetLanguage? target)
        {
            var conversions = type
                .OriginalDefinition.GetMembers()
                .OfType<IMethodSymbol>()
                .Where(member => member.MethodKind == MethodKind.Conversion)
                .ToList();

            return new ConversionSet(
                conversions,
                conversions.Select(conversion => DraftOf(conversion, target)).ToList()
            );
        }

        public IrConversionName NameAt(IMethodSymbol method)
        {
            var index = IndexOf(method);

            return _drafts[index].NameFrom(_suffixes.Suffixes[index]);
        }

        /// <summary>
        /// The candidate the climb could not separate, without its positional
        /// marker.
        /// </summary>
        public string SpellingAt(IMethodSymbol method)
        {
            var suffix = _suffixes.Suffixes[IndexOf(method)];
            var marker = suffix.IndexOf('$', StringComparison.Ordinal);

            return marker < 0 ? suffix : suffix[..marker];
        }

        /// <summary>
        /// The first conversion of the dead-end set <paramref name="method"/>
        /// belongs to, when it is not that first one; <c>null</c> otherwise.
        /// </summary>
        public IMethodSymbol? EarlierCollisionOf(IMethodSymbol method)
        {
            var index = IndexOf(method);
            var deadEnd = _suffixes.DeadEnds.FirstOrDefault(set => set.Skip(1).Contains(index));

            return deadEnd is null ? null : _conversions[deadEnd[0]];
        }

        private int IndexOf(IMethodSymbol method)
        {
            for (var index = 0; index < _conversions.Count; index++)
            {
                if (
                    SymbolEqualityComparer.Default.Equals(
                        _conversions[index],
                        method.OriginalDefinition
                    )
                )
                    return index;
            }

            throw new InvalidOperationException(
                $"'{method.ContainingType.Name}' does not declare the conversion '{method}'."
            );
        }
    }
}
