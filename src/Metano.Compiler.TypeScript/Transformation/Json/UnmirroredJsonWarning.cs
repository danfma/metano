using Metano.Compiler.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.TypeScript.Transformation.Json;

/// <summary>
/// Builds the <c>MS0037</c> (<c>UnmirroredJsonOption</c>) warnings of
/// spec 009 FR-046 and names the symbols and locations they point at.
/// </summary>
internal static class UnmirroredJsonWarning
{
    /// <summary>The warning with <paramref name="message"/> at <paramref name="location"/>.</summary>
    public static MetanoDiagnostic Create(string message, Location? location) =>
        new(
            MetanoDiagnosticSeverity.Warning,
            DiagnosticCodes.UnmirroredJsonOption,
            message,
            location
        );

    /// <summary><c>Type.Member</c>, the type in C#'s short error-message format.</summary>
    public static string MemberName(ISymbol member) =>
        $"{TypeName(member.ContainingType)}.{member.Name}";

    /// <summary>The type in C#'s short error-message format (<c>Page&lt;T&gt;</c>).</summary>
    public static string TypeName(ITypeSymbol type) =>
        type.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat);

    /// <summary>Where <paramref name="attribute"/> is applied, if in source.</summary>
    public static Location? At(AttributeData attribute) =>
        attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation();

    /// <summary>
    /// The named argument <paramref name="name"/> of <paramref name="attribute"/>
    /// in source (<c>DefaultIgnoreCondition = …</c>), else the attribute.
    /// </summary>
    public static Location? At(AttributeData attribute, string name) =>
        attribute.ApplicationSyntaxReference?.GetSyntax() is AttributeSyntax syntax
            ? syntax
                .ArgumentList?.Arguments.FirstOrDefault(argument =>
                    argument.NameEquals?.Name.Identifier.ValueText == name
                )
                ?.GetLocation()
                ?? syntax.GetLocation()
            : null;

    /// <summary>The first source location of <paramref name="symbol"/>.</summary>
    public static Location? At(ISymbol symbol) =>
        symbol.Locations.FirstOrDefault(location => location.IsInSource);
}
