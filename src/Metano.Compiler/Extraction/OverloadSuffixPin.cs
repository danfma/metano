using System.Text.RegularExpressions;
using Metano.Annotations;
using Metano.Compiler.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.Extraction;

/// <summary>
/// The suffix a <c>[Name]</c> pins on one overload of a dispatched group (spec
/// 013 FR-005, OQ-1 (a)): <c>[Name("cents")]</c> replaces the generated suffix
/// with <c>Cents</c> — <c>fromCents</c>, <c>byMoneyCents</c>, <c>addCents</c> —
/// and leaves the dispatcher's name alone. The value must be an ASCII
/// identifier with no <c>$</c>, which the claim pass and the dead end reserve
/// for their markers; an invalid one pins nothing. The pin's own reports — an
/// invalid value, a pin an anchored overload cannot use, a pin on a
/// constructor that is not overloaded — are made here.
/// </summary>
internal sealed partial class OverloadSuffixPin
{
    private OverloadSuffixPin(IMethodSymbol owner, string value, Location? location)
    {
        Owner = owner;
        Value = value;
        Location = location;
        Suffix = SuffixPattern().IsMatch(value) ? NamingText.Capitalize(value) : null;
    }

    /// <summary>The overload the <c>[Name]</c> is declared on.</summary>
    public IMethodSymbol Owner { get; }

    /// <summary>The attribute's value, as written.</summary>
    public string Value { get; }

    /// <summary>The pinned suffix, capitalized; <c>null</c> when the value is invalid.</summary>
    public string? Suffix { get; }

    /// <summary>
    /// The attribute's name argument, else the attribute; <c>null</c> for an
    /// attribute read from metadata, which its own compilation reported.
    /// </summary>
    public Location? Location { get; }

    /// <summary>
    /// The <c>[Name]</c> pin <paramref name="overload"/> carries for
    /// <paramref name="target"/>; <c>null</c> when it carries none or when its
    /// <c>[Name]</c> does not pin a suffix there (<see cref="Applies"/>).
    /// </summary>
    public static OverloadSuffixPin? Of(IMethodSymbol overload, TargetLanguage? target)
    {
        if (!Applies(overload, target))
            return null;

        if (overload.FindNameOverrideAttribute(target) is not { } attribute)
            return null;

        return SymbolHelper.NameOverrideValueOf(attribute) is { } value
            ? new OverloadSuffixPin(overload, value, LocationOf(attribute))
            : null;
    }

    /// <summary>
    /// Whether a <c>[Name]</c> on <paramref name="overload"/> pins a suffix
    /// for <paramref name="target"/> instead of renaming the whole member:
    /// not for a conversion operator, which has no C# name, so its
    /// <c>[Name]</c> names its group; not for Dart, which names no fast paths
    /// before 013g and renames the member as it always did.
    /// </summary>
    public static bool Applies(IMethodSymbol overload, TargetLanguage? target) =>
        // TODO(#549): the same target capability as the direct-call fast paths.
        target != TargetLanguage.Dart
        && overload.MethodKind != MethodKind.Conversion;

    /// <summary>
    /// An invalid value pins nothing: the overload keeps its generated suffix
    /// and the error fails the build. The message names no suffix, so the
    /// report is the same from every group that sees the overload (a derived
    /// type's visible group included).
    /// </summary>
    public void ReportIfInvalid()
    {
        if (Suffix is not null)
            return;

        Report(
            MetanoDiagnosticSeverity.Error,
            $"[Name(\"{Value}\")] on the overload '{OverloadSignatureText.DisplayOf(Owner)}' is "
                + "not a valid suffix: it must start with an ASCII letter or '_' and contain only "
                + "ASCII letters, digits and '_'; the overload keeps its generated suffix "
                + "(spec 013 FR-005)."
        );
    }

    /// <summary>
    /// The pin of an override or interface implementation, which takes its
    /// root's suffix (FR-025): a pin naming another suffix is ignored and
    /// reported; repeating the root's suffix is silent.
    /// </summary>
    public void ReportIfIgnoredFor(IMethodSymbol root, string rootSuffix)
    {
        if (Suffix == rootSuffix)
            return;

        var rootSignature = OverloadSignatureText.Of(root.OriginalDefinition);

        Report(
            MetanoDiagnosticSeverity.Warning,
            $"[Name(\"{Value}\")] on '{Owner.ContainingType.Name}."
                + $"{OverloadSignatureText.DisplayOf(Owner)}' is ignored: an override or interface "
                + $"implementation takes the suffix '{rootSuffix}' of its root "
                + $"'{root.ContainingType.Name}.{rootSignature}'; put [Name] on the root "
                + "(spec 013 FR-005, FR-025)."
        );
    }

    /// <summary>
    /// The pin of a constructor outside a dispatched set: no factory or
    /// selector is named, so the attribute has no effect.
    /// </summary>
    public void ReportUnusedOnConstructor() =>
        Report(
            MetanoDiagnosticSeverity.Warning,
            $"[Name(\"{Value}\")] on the constructor '{OverloadSignatureText.DisplayOf(Owner)}' "
                + "has no effect: only an overload of a dispatched constructor set gets a factory "
                + "and a selector to name (spec 013 FR-005)."
        );

    private void Report(MetanoDiagnosticSeverity severity, string message)
    {
        if (Location is null)
            return;

        ExtractionDiagnostics.Report(
            new MetanoDiagnostic(severity, DiagnosticCodes.AmbiguousConstruct, message, Location)
        );
    }

    private static Location? LocationOf(AttributeData attribute) =>
        attribute.ApplicationSyntaxReference?.GetSyntax() is AttributeSyntax syntax
            ? (syntax.ArgumentList?.Arguments.LastOrDefault() ?? (SyntaxNode)syntax).GetLocation()
            : null;

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex SuffixPattern();
}
