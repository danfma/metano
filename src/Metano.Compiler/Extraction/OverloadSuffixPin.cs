using System.Text.RegularExpressions;
using Metano.Annotations;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.Extraction;

/// <summary>
/// The suffix a <c>[Name]</c> pins on one overload of a dispatched group (spec
/// 013 FR-005, OQ-1 (a)): <c>[Name("cents")]</c> replaces the generated suffix
/// with <c>Cents</c> — <c>fromCents</c>, <c>byMoneyCents</c>, <c>addCents</c> —
/// and leaves the dispatcher's name alone. The value must be an ASCII
/// identifier with no <c>$</c>, which the claim pass and the dead end reserve
/// for their markers; an invalid one is not pinned.
/// </summary>
/// <param name="Value">The attribute's value, as written.</param>
/// <param name="Attribute">The <c>[Name]</c> attribute the value comes from.</param>
internal sealed partial record OverloadSuffixPin(string Value, AttributeData Attribute)
{
    /// <summary>Whether <see cref="Value"/> can be a suffix.</summary>
    public bool IsValid => SuffixPattern().IsMatch(Value);

    /// <summary>The pinned suffix, capitalized; <c>null</c> when the value is invalid.</summary>
    public string? Suffix => IsValid ? NamingText.Capitalize(Value) : null;

    /// <summary>
    /// The attribute's name argument, else the attribute; <c>null</c> for an
    /// attribute read from metadata, which its own compilation reported.
    /// </summary>
    public Location? Location =>
        Attribute.ApplicationSyntaxReference?.GetSyntax() is AttributeSyntax syntax
            ? (syntax.ArgumentList?.Arguments.LastOrDefault() ?? (SyntaxNode)syntax).GetLocation()
            : null;

    /// <summary>The <c>[Name]</c> pin <paramref name="overload"/> carries for <paramref name="target"/>.</summary>
    public static OverloadSuffixPin? Of(IMethodSymbol overload, TargetLanguage? target) =>
        overload.FindNameOverrideAttribute(target) is { } attribute
            ? new OverloadSuffixPin(SymbolHelper.NameOverrideValueOf(attribute), attribute)
            : null;

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex SuffixPattern();
}
