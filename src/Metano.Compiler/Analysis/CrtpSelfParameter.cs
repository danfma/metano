using Metano.Annotations;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Analysis;

/// <summary>
/// The self parameter of a CRTP-shaped class (spec 001 FR-113): the one type
/// parameter of <c>B&lt;…, TSelf, …&gt;</c> that has <c>B</c> constructed over its own
/// type parameters among its constraint types (<c>where TSelf : B&lt;TSelf&gt;</c>).
/// </summary>
public static class CrtpSelfParameter
{
    /// <summary>
    /// The self parameter of <paramref name="type"/>'s definition, or <c>null</c> when
    /// none or more than one type parameter is constrained to the class itself.
    /// </summary>
    public static ITypeParameterSymbol? Of(INamedTypeSymbol type)
    {
        var definition = type.OriginalDefinition;

        if (definition.TypeKind is not TypeKind.Class || definition.TypeParameters.IsEmpty)
            return null;

        var selfParameters = definition
            .TypeParameters.Where(parameter =>
                parameter.ConstraintTypes.Any(constraint =>
                    IsSelfConstruction(constraint, definition)
                )
            )
            .ToList();

        return selfParameters is [var self] ? self : null;
    }

    /// <summary>
    /// The type a call to the static <paramref name="callee"/> binds to its class's self
    /// parameter (<c>CardH</c> for <c>Component&lt;CardH&gt;.Use</c>, spelled
    /// <c>CardH.Use</c> or <c>Fancy.Use</c> alike), or <c>null</c> when the callee is no
    /// static method of a CRTP-shaped class.
    /// </summary>
    public static ITypeSymbol? ArgumentOf(IMethodSymbol callee)
    {
        if (
            callee is not { IsStatic: true, MethodKind: MethodKind.Ordinary }
            || Of(callee.ContainingType) is not { } self
        )
            return null;

        return callee.ContainingType.TypeArguments[self.Ordinal];
    }

    /// <summary>
    /// Whether <paramref name="selfArgument"/>, or a class between it and the callee's
    /// declaring class, declares a static member that takes <paramref name="callee"/>'s
    /// name: a call received on the self argument's class would reach that member instead.
    /// </summary>
    public static bool IsHiddenOn(
        ITypeSymbol selfArgument,
        IMethodSymbol callee,
        TargetLanguage target
    )
    {
        var declaring = callee.ContainingType.OriginalDefinition;

        for (
            var current = selfArgument as INamedTypeSymbol;
            current is not null
                && !SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, declaring);
            current = current.BaseType
        )
        {
            if (
                current
                    .GetMembers()
                    .Any(member => member.IsStatic && TakesNameOf(member, callee, target))
            )
                return true;
        }

        return false;
    }

    /// <summary>
    /// Whether <paramref name="member"/> and <paramref name="other"/> are emitted under one
    /// name: each one's <c>[Name]</c> override when it has one, else its C# name, ignoring
    /// case (a target's naming policy may change it).
    /// </summary>
    public static bool TakesNameOf(ISymbol member, ISymbol other, TargetLanguage target) =>
        string.Equals(
            EmittedNameOf(member, target),
            EmittedNameOf(other, target),
            StringComparison.OrdinalIgnoreCase
        );

    /// <summary>
    /// Whether <paramref name="callee"/> is an exported static method of a CRTP-shaped
    /// class that a referenced package declares: its body is not visible, so a call to it
    /// is emitted on the self argument's class from the signature alone (FR-113 item 3).
    /// </summary>
    public static bool IsReferencedCandidate(IMethodSymbol callee, IAssemblySymbol currentAssembly)
    {
        var definition = callee.OriginalDefinition;
        var type = definition.ContainingType;

        return ArgumentOf(callee) is not null
            && !SymbolEqualityComparer.Default.Equals(
                definition.ContainingAssembly,
                currentAssembly
            )
            && definition.DeclaredAccessibility is Accessibility.Public or Accessibility.Protected
            && type.DeclaredAccessibility is Accessibility.Public or Accessibility.Protected
            && SymbolHelper.IsTranspilable(
                type,
                type.ContainingAssembly.HasTranspileAssembly(),
                type.ContainingAssembly
            );
    }

    private static string EmittedNameOf(ISymbol symbol, TargetLanguage target) =>
        symbol.GetNameOverride(target) ?? symbol.Name;

    private static bool IsSelfConstruction(ITypeSymbol constraint, INamedTypeSymbol definition) =>
        constraint is INamedTypeSymbol named
        && SymbolEqualityComparer.Default.Equals(named.OriginalDefinition, definition)
        && named.TypeArguments.SequenceEqual<ITypeSymbol>(
            definition.TypeParameters,
            SymbolEqualityComparer.Default
        );
}
