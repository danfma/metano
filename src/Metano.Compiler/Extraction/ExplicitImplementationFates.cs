using Metano.Compiler.Frontend.Roslyn;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Extraction;

/// <summary>
/// Whether the extraction drops an explicit interface implementation: the one
/// predicate the overload groups, the value-equality shape and the class
/// extractor share. The target-agnostic lowering
/// (<see cref="ExplicitImplementationLowering"/>) decides, except that a target
/// naming the synthesized <c>Equals</c> keeps a struct's explicit
/// <c>System.IEquatable&lt;T&gt;.Equals(T)</c> the lowering supersedes only for its
/// name (<see cref="KeepsSupersededEquatable"/>, spec 013 FR-045).
/// </summary>
internal static class ExplicitImplementationFates
{
    /// <summary>Whether the target drops <paramref name="member"/>.</summary>
    public static bool IsDropped(ISymbol member, OverloadGroupingPolicy policy) =>
        ExplicitImplementationLowering.Resolve(member, policy.Target) is { } lowering
        && lowering.Fate is not ExplicitImplementationFate.EmittedUnderInterfaceName
        && !KeepsSupersededEquatable(member, lowering, policy);

    /// <summary>
    /// Whether a struct's (or record struct's) explicit
    /// <c>System.IEquatable&lt;T&gt;.Equals(T)</c> is kept although the lowering
    /// supersedes it: the name it claims is held by <c>System.ValueType.Equals</c>,
    /// or by another <c>Equals</c> overload of the struct, which it joins as one
    /// more overload. Only for a target that names the synthesized <c>Equals</c>,
    /// whose group takes them all; every other target keeps the lowering's fate.
    /// </summary>
    private static bool KeepsSupersededEquatable(
        ISymbol member,
        ExplicitImplementation lowering,
        OverloadGroupingPolicy policy
    ) =>
        policy.NamesSynthesizedEquals
        && member is IMethodSymbol { ContainingType.TypeKind: TypeKind.Struct } method
        && lowering
            is {
                Fate: ExplicitImplementationFate.Superseded,
                ClaimingMember: IMethodSymbol claimant
            }
        && method.ExplicitInterfaceImplementations.Any(implemented =>
            EquatableInterfaces.IsEquatable(implemented.ContainingType)
        )
        && IsNameOnlyClaimant(method, claimant);

    /// <summary>
    /// A claimant that holds only the name: <c>System.ValueType.Equals</c>, or
    /// another <c>Equals</c> overload of the struct with other parameters.
    /// </summary>
    private static bool IsNameOnlyClaimant(IMethodSymbol method, IMethodSymbol claimant) =>
        claimant.ContainingType.SpecialType is SpecialType.System_ValueType
        || (
            SymbolEqualityComparer.Default.Equals(
                claimant.ContainingType.OriginalDefinition,
                method.ContainingType.OriginalDefinition
            )
            && claimant.MethodKind is MethodKind.Ordinary
            && !SymbolHelper.HasSameParameters(claimant, method)
        );
}
