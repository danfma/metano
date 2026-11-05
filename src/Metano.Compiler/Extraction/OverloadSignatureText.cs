using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Extraction;

/// <summary>
/// An overload as C# declares it — <c>Money(decimal amount)</c>,
/// <c>Opt(string s, params int[] rest)</c> — recorded as the overload's
/// <c>SignatureText</c> (spec 013 D2): a selector's description, the name
/// diagnostics give it, and how a target finds the overload's declaration
/// again. Public because the targets locate the declarations of their
/// diagnostics with it.
/// </summary>
public static class OverloadSignatureText
{
    private static readonly SymbolDisplayFormat TypeFormat =
        SymbolDisplayFormat.MinimallyQualifiedFormat.AddMiscellaneousOptions(
            SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier
        );

    /// <summary>The signature text of <paramref name="method"/>.</summary>
    public static string Of(IMethodSymbol method)
    {
        var parameters = method.Parameters.Select(parameter =>
            ModifierOf(parameter) + $"{parameter.Type.ToDisplayString(TypeFormat)} {parameter.Name}"
        );

        return $"{DeclaredNameOf(method)}({string.Join(", ", parameters)})";
    }

    /// <summary>The method's name, or a constructor's type name.</summary>
    internal static string DeclaredNameOf(IMethodSymbol method) =>
        method.MethodKind == MethodKind.Constructor ? method.ContainingType.Name : method.Name;

    private static string ModifierOf(IParameterSymbol parameter) =>
        parameter switch
        {
            { IsParams: true } => "params ",
            { RefKind: RefKind.Ref } => "ref ",
            { RefKind: RefKind.Out } => "out ",
            { RefKind: RefKind.In } => "in ",
            { RefKind: RefKind.RefReadOnlyParameter } => "ref readonly ",
            _ => "",
        };
}
