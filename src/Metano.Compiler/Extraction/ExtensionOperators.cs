using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Extraction;

/// <summary>
/// The facts about a C# 14 extension operator
/// (<c>extension(Sig) { public static Sig operator +(Sig a, Sig b) … }</c>,
/// #761) that its declaration and its applications share: the static class
/// that holds the block, and whether the operator is lowered at all. A lowered
/// extension operator is a module function of that class's file, called at
/// every application; an operator this rule leaves out reports <c>MS0001</c>
/// at its declaration and at each application.
/// </summary>
internal static class ExtensionOperators
{
    /// <summary>
    /// The static class whose extension block declares
    /// <paramref name="method"/>; <c>null</c> for any other method.
    /// </summary>
    public static INamedTypeSymbol? ContainerOf(IMethodSymbol method) =>
        method
            is {
                MethodKind: MethodKind.UserDefinedOperator or MethodKind.Conversion,
                IsImplicitlyDeclared: false,
            }
        && method.IsExtensionBlockMember()
            ? method.ContainingType.ContainingType
            : null;

    /// <summary>
    /// Why the extension operator <paramref name="method"/> is not lowered;
    /// <c>null</c> when it is: a static operator other than <c>true</c> /
    /// <c>false</c> that no other extension operator of its class shares a
    /// metadata name with. An instance (compound or increment) operator,
    /// a conversion, a <c>true</c> / <c>false</c> operator and an overloaded
    /// operator have no module function.
    /// </summary>
    public static string? UnsupportedReasonOf(IMethodSymbol method, INamedTypeSymbol container)
    {
        if (!method.IsStatic)
            return "an instance extension operator is not supported";

        if (method.MethodKind == MethodKind.Conversion)
            return "an extension conversion is not supported";

        if (IsTruthOperator(method))
            return "an extension 'true' / 'false' operator is not supported";

        return IsOverloaded(method, container)
            ? $"'{container.Name}' declares more than one extension '{method.Name}', and "
                + "overloaded extension operators are not supported"
            : null;
    }

    private static bool IsTruthOperator(IMethodSymbol method) =>
        method.Name
            is WellKnownMemberNames.TrueOperatorName
                or WellKnownMemberNames.FalseOperatorName;

    private static bool IsOverloaded(IMethodSymbol method, INamedTypeSymbol container) =>
        container
            .GetTypeMembers()
            .Where(block => block.Name.Length == 0)
            .SelectMany(block => block.GetMembers(method.OriginalDefinition.Name))
            .OfType<IMethodSymbol>()
            .Count(member => member.MethodKind == MethodKind.UserDefinedOperator) > 1;
}
