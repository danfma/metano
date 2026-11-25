using Metano.Annotations;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Extraction;

/// <summary>
/// The facts about a C# 14 extension operator
/// (<c>extension(Sig) { public static Sig operator +(Sig a, Sig b) … }</c>,
/// #761, decisions W9-3 / W9-8) that its declaration, its applications and a
/// target's name registry share: the static class that holds the block, the
/// block's receiver type, and whether the operator is lowered at all. A lowered
/// extension operator is a module function of that class's file, called at
/// every application; an operator this rule leaves out reports <c>MS0001</c>
/// at its declaration and at each application.
/// </summary>
public static class ExtensionOperators
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
    /// The receiver type of the extension block that declares
    /// <paramref name="method"/>, as the block declares it (<c>Box&lt;T&gt;</c>
    /// for <c>extension&lt;T&gt;(Box&lt;T&gt;)</c>), which a target qualifies the
    /// operator's function name by.
    /// </summary>
    public static ITypeSymbol ReceiverOf(IMethodSymbol method) =>
        method.OriginalDefinition.ContainingType.ExtensionParameter?.Type
        ?? throw new ArgumentException(
            $"'{method.Name}' is not declared in an extension block.",
            nameof(method)
        );

    /// <summary>
    /// Why the extension operator <paramref name="method"/> of
    /// <paramref name="container"/> is not lowered for
    /// <paramref name="target"/>; <c>null</c> when it is. An operator of a
    /// class the toolchain does not emit, an <c>[Ignore]</c>d operator, an
    /// instance (compound or increment) operator, a conversion, a
    /// <c>true</c> / <c>false</c> operator, and an overload has no module
    /// function: two operators of one metadata name for one receiver type,
    /// unless every one of them carries its own <c>[Name]</c>.
    /// </summary>
    public static string? UnsupportedReasonOf(
        IMethodSymbol method,
        INamedTypeSymbol container,
        TargetLanguage? target
    )
    {
        if (
            !container.IsEmittedExtensionContainer(target)
            || SymbolHelper.HasIgnore(container, target)
        )
            return $"'{container.Name}' is not emitted";

        if (SymbolHelper.HasIgnore(method.OriginalDefinition, target))
            return "the operator is [Ignore]d";

        if (!method.IsStatic)
            return "an instance extension operator is not supported";

        if (method.MethodKind == MethodKind.Conversion)
            return "an extension conversion is not supported";

        if (IsTruthOperator(method))
            return "an extension 'true' / 'false' operator is not supported";

        return IsUnnamedOverload(method, container, target)
            ? $"'{container.Name}' declares more than one extension '{method.Name}' for "
                + $"'{ReceiverOf(method).Name}' without a distinct [Name] on each, and "
                + "overloaded extension operators are not supported"
            : null;
    }

    private static bool IsTruthOperator(IMethodSymbol method) =>
        method.Name
            is WellKnownMemberNames.TrueOperatorName
                or WellKnownMemberNames.FalseOperatorName;

    /// <summary>
    /// Whether <paramref name="method"/> shares its metadata name and receiver
    /// type with another extension operator of <paramref name="container"/>,
    /// and the group's <c>[Name]</c>s do not tell every member apart.
    /// </summary>
    private static bool IsUnnamedOverload(
        IMethodSymbol method,
        INamedTypeSymbol container,
        TargetLanguage? target
    )
    {
        var siblings = SameReceiverOperators(method, container);

        if (siblings.Count < 2)
            return false;

        var pins = siblings.Select(sibling => sibling.GetNameOverride(target)).ToList();
        var distinctPins = pins.Distinct(StringComparer.Ordinal).Count();

        return pins.Contains(null) || distinctPins < pins.Count;
    }

    private static List<IMethodSymbol> SameReceiverOperators(
        IMethodSymbol method,
        INamedTypeSymbol container
    )
    {
        var receiver = ReceiverOf(method).OriginalDefinition;

        return
        [
            .. container
                .GetTypeMembers()
                .Where(block => block.TypeKind == TypeKind.Extension)
                .SelectMany(block => block.GetMembers(method.OriginalDefinition.Name))
                .OfType<IMethodSymbol>()
                .Where(member =>
                    member.MethodKind == MethodKind.UserDefinedOperator
                    && SymbolEqualityComparer.Default.Equals(
                        ReceiverOf(member).OriginalDefinition,
                        receiver
                    )
                ),
        ];
    }
}
