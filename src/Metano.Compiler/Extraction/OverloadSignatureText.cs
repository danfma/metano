using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Extraction;

/// <summary>
/// An overload as C# declares it — <c>Money(decimal amount)</c>,
/// <c>Opt(string s, params int[] rest)</c> — recorded as the overload's
/// <c>SignatureText</c> (spec 013 D2): a selector's description, the name
/// diagnostics give it, and how a target finds the overload's declaration
/// again.
/// </summary>
public static class OverloadSignatureText
{
    private static readonly SymbolDisplayFormat TypeFormat =
        SymbolDisplayFormat.MinimallyQualifiedFormat.AddMiscellaneousOptions(
            SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier
        );

    public static string Of(IMethodSymbol method)
    {
        var parameters = method.Parameters.Select(parameter =>
            (parameter.IsParams ? "params " : "")
            + $"{parameter.Type.ToDisplayString(TypeFormat)} {parameter.Name}"
        );

        var name =
            method.MethodKind == MethodKind.Constructor ? method.ContainingType.Name : method.Name;

        return $"{name}({string.Join(", ", parameters)})";
    }
}
