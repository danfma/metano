using Metano.Compiler.Diagnostics;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Extraction;

/// <summary>
/// The declared overloads of a group that a runtime dispatcher cannot tell from
/// the group's synthesized member (spec 013 FR-042): at the fewest arguments
/// both accept, one of its parameters there has no runtime check (a type
/// parameter, <c>object</c>) or one that accepts the synthesized member's
/// argument (for <c>Equals</c> the owner type, a base or an interface of it; for
/// the copy any object or interface). Such an overload keeps its fast path, so
/// its C# calls stay direct, and its branch follows the synthesized member's.
/// It reports <c>MS0063</c>, or <c>MS0001</c> as an error when callers through
/// an interface member or a base method it implements would reach the
/// synthesized member instead.
/// </summary>
internal static class SynthesizedShadowing
{
    /// <summary>The shadowed members of <paramref name="group"/>, each reported once.</summary>
    public static IReadOnlySet<GroupMember> Report(
        INamedTypeSymbol type,
        MethodOverloadGroup group,
        OverloadGroupingPolicy policy
    )
    {
        if (group.Synthesized is not { } synthesized)
            return new HashSet<GroupMember>();

        var shadowed = group
            .DeclaredMembers.Where(member =>
                member.Anchor?.Kind is not IrOverloadAnchorKind.Override
                && IsShadowed(type, member, synthesized)
            )
            .ToHashSet<GroupMember>();

        foreach (var member in shadowed.Cast<DeclaredMember>())
            ReportShadowed(type, member, synthesized, policy);

        return shadowed;
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

    private static void ReportShadowed(
        INamedTypeSymbol type,
        DeclaredMember member,
        SynthesizedMember synthesized,
        OverloadGroupingPolicy policy
    )
    {
        var method = member.Symbol;
        var shared = policy.NameOf(synthesized.Kind);
        var signature = OverloadSignatureText.DisplayOf(method);

        if (ReachedThroughSharedName(type, method, policy) is { } contract)
        {
            ExtractionDiagnostics.Report(
                new MetanoDiagnostic(
                    MetanoDiagnosticSeverity.Error,
                    DiagnosticCodes.UnsupportedFeature,
                    $"'{type.Name}.{signature}' {contract}, whose callers reach it through "
                        + $"'{shared}', but a runtime dispatcher cannot tell it from the "
                        + $"synthesized '{SynthesizedText(synthesized.Kind)}' of '{type.Name}', "
                        + "which keeps that name; those callers would reach the synthesized "
                        + "member. Give the overload a parameter of a type the dispatcher can "
                        + "tell apart, or rename it (spec 013 FR-042).",
                    method.Locations.FirstOrDefault()
                )
            );

            return;
        }

        if (IsCoveredByReplacement(type, synthesized, policy))
            return;

        ExtractionDiagnostics.Report(
            new MetanoDiagnostic(
                MetanoDiagnosticSeverity.Warning,
                DiagnosticCodes.AmbiguousRuntimeDispatch,
                $"Overload '{signature}' cannot be told apart at runtime from the synthesized "
                    + $"'{SynthesizedText(synthesized.Kind)}' of '{type.Name}', which shares "
                    + $"its name '{shared}': its C# calls reach it directly, but a call "
                    + "through the dispatcher (an interface, object, the runtime collections) "
                    + "reaches the synthesized member (spec 013 FR-042).",
                method.Locations.FirstOrDefault()
            )
        );
    }

    /// <summary>
    /// The contract that makes callers reach <paramref name="method"/> through
    /// the shared dispatcher name: a member of a transpiled interface it
    /// implements, or a method an emitted base class declares outside any
    /// dispatched group. <c>null</c> when it has none.
    /// </summary>
    private static string? ReachedThroughSharedName(
        INamedTypeSymbol type,
        IMethodSymbol method,
        OverloadGroupingPolicy policy
    )
    {
        var implemented = type
            .AllInterfaces.Where(candidate =>
                MethodOverloadGroups.IsTranspiledInterface(candidate, policy.Target)
            )
            .SelectMany(candidate => candidate.GetMembers().OfType<IMethodSymbol>())
            .FirstOrDefault(candidate =>
                SymbolEqualityComparer.Default.Equals(
                    type.FindImplementationForInterfaceMember(candidate)?.OriginalDefinition,
                    method.OriginalDefinition
                )
            );

        if (implemented is not null)
            return $"implements '{implemented.ContainingType.Name}.{implemented.Name}'";

        if (method.OverriddenMethod is not { } overridden)
            return null;

        var isPlainBaseMethod =
            MethodOverloadGroups.IsEmittedBase(overridden.ContainingType, policy.Target)
            && MethodOverloadGroups.GroupOf(overridden.OriginalDefinition, policy)
                is not { IsDispatched: true };

        return isPlainBaseMethod
            ? $"overrides '{overridden.ContainingType.Name}.{overridden.Name}'"
            : null;
    }

    /// <summary>
    /// The type replaces the synthesized <c>Equals</c> itself (a record's
    /// <c>Equals(R)</c>, a struct's <c>override Equals(object)</c>): calls
    /// through the dispatcher reach the user's equality, which owns the
    /// consistency of its overloads.
    /// </summary>
    private static bool IsCoveredByReplacement(
        INamedTypeSymbol type,
        SynthesizedMember synthesized,
        OverloadGroupingPolicy policy
    ) =>
        synthesized.Kind is IrSynthesizedValueMember.Equals
        && ValueEqualityShapes.ReplacingEqualsOf(type, policy.Target) is not null;

    private static string SynthesizedText(IrSynthesizedValueMember kind) =>
        kind switch
        {
            IrSynthesizedValueMember.Equals => "Equals(object)",
            IrSynthesizedValueMember.HashCode => "GetHashCode()",
            _ => "with copy",
        };
}
