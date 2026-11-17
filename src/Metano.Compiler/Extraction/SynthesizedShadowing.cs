using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Extraction;

/// <summary>
/// The declared overloads of a group that a runtime dispatcher cannot tell from
/// the group's synthesized member (spec 013 FR-042): at the fewest arguments
/// both accept, one of its parameters there has no runtime check (a type
/// parameter, <c>object</c>) or one that accepts the synthesized member's
/// argument (for <c>Equals</c> the owner type, a base or an interface of it; for
/// the copy any interface), and at no argument there is nothing to check. Such
/// an overload keeps its fast path, so its C# calls stay direct, and its branch
/// follows the synthesized member's (<see cref="SynthesizedShadowingReporter"/>
/// reports it). Telling overloads apart at run time is the TypeScript
/// dispatcher's capability; it moves into the grouping policy when a second
/// target supplies synthesized member names.
/// </summary>
internal static class SynthesizedShadowing
{
    /// <summary>The declared members of <paramref name="group"/> its synthesized member shadows.</summary>
    public static IReadOnlySet<DeclaredMember> ShadowedBy(
        INamedTypeSymbol type,
        MethodOverloadGroup group
    )
    {
        if (group.Synthesized is not { } synthesized)
            return new HashSet<DeclaredMember>();

        return group
            .DeclaredMembers.Where(member =>
                member.Anchor?.Kind is not IrOverloadAnchorKind.Override
                && IsShadowed(type, member, synthesized)
            )
            .ToHashSet();
    }

    private static bool IsShadowed(
        INamedTypeSymbol type,
        DeclaredMember member,
        SynthesizedMember synthesized
    )
    {
        var fewest = Math.Max(member.RequiredArity, synthesized.RequiredArity);
        var most = Math.Min(member.MaximumArity, synthesized.MaximumArity);

        if (fewest > most)
            return false;

        // At no argument there is no parameter to check.
        if (fewest == 0)
            return true;

        return member
            .Symbol.Parameters.Take(fewest)
            .Any(parameter =>
                AcceptsSynthesizedArgument(type, ElementTypeOf(parameter), synthesized.Kind)
            );
    }

    private static ITypeSymbol ElementTypeOf(IParameterSymbol parameter) =>
        parameter is { IsParams: true, Type: IArrayTypeSymbol array }
            ? array.ElementType
            : parameter.Type;

    /// <summary>
    /// Whether a parameter of <paramref name="parameterType"/> has no runtime
    /// check, or one that accepts what the synthesized member receives.
    /// </summary>
    private static bool AcceptsSynthesizedArgument(
        INamedTypeSymbol type,
        ITypeSymbol parameterType,
        IrSynthesizedValueMember kind
    )
    {
        var tested = UnwrapNullable(parameterType);

        if (tested is ITypeParameterSymbol or IDynamicTypeSymbol)
            return true;

        if (tested.SpecialType is SpecialType.System_Object or SpecialType.System_ValueType)
            return true;

        return kind switch
        {
            IrSynthesizedValueMember.Equals => IsTypeOrSupertype(type, tested),
            _ => tested.TypeKind is TypeKind.Interface,
        };
    }

    private static ITypeSymbol UnwrapNullable(ITypeSymbol type) =>
        type
            is INamedTypeSymbol
            {
                OriginalDefinition.SpecialType: SpecialType.System_Nullable_T,
                TypeArguments: [var inner],
            }
            ? inner
            : type;

    private static bool IsTypeOrSupertype(INamedTypeSymbol type, ITypeSymbol candidate)
    {
        for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            if (IsSameDefinition(current, candidate))
                return true;
        }

        return type.AllInterfaces.Any(implemented => IsSameDefinition(implemented, candidate));
    }

    private static bool IsSameDefinition(ITypeSymbol type, ITypeSymbol other) =>
        SymbolEqualityComparer.Default.Equals(type.OriginalDefinition, other.OriginalDefinition);
}
