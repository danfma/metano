using Metano.Annotations;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Extraction;

/// <summary>
/// The dispatcher overloads an emitted base class already declares for a
/// group, as the derived type sees them (spec 013 FR-025, naming.md § Root anchoring).
/// </summary>
internal static partial class MethodOverloadGroups
{
    /// <summary>
    /// The overloads an emitted base class's dispatcher declares for
    /// <paramref name="name"/>, as <paramref name="type"/> sees them; a
    /// <c>new</c> method of the type hides the base overload of its
    /// signature. <c>null</c> when no base dispatches the name.
    /// </summary>
    internal static IReadOnlyList<GroupMember>? InheritedOverloadsOf(
        INamedTypeSymbol type,
        string name,
        IReadOnlyList<GroupMember> declared,
        OverloadGroupingPolicy policy
    )
    {
        // Operators lower to static helpers of their own type, never to an
        // inherited dispatcher.
        if (declared.Any(member => member.IsOperator))
            return null;

        if (DispatcherOverloadsAbove(type, name, policy) is not { } inherited)
            return null;

        return inherited.Where(overload => !IsHiddenBy(declared, overload)).ToList();
    }

    /// <summary>
    /// Whether a <c>new</c> method of <paramref name="declared"/> hides the
    /// inherited <paramref name="overload"/> of its signature. A synthesized
    /// member is overridden, never hidden.
    /// </summary>
    private static bool IsHiddenBy(IReadOnlyList<GroupMember> declared, GroupMember overload) =>
        declared.Any(member => member.Hides(overload));

    /// <summary>
    /// The dispatcher overloads the nearest emitted base declaring
    /// <paramref name="name"/> emits, constructed through
    /// <paramref name="type"/>'s base chain: the overloads every level adds,
    /// base-most first. <c>null</c> when that base's group is not
    /// dispatched.
    /// </summary>
    private static IReadOnlyList<GroupMember>? DispatcherOverloadsAbove(
        INamedTypeSymbol type,
        string name,
        OverloadGroupingPolicy policy
    )
    {
        for (
            var ancestor = type.BaseType;
            IsEmittedBase(ancestor, policy.Target);
            ancestor = ancestor!.BaseType
        )
        {
            var group = Of(ancestor!, policy)
                .FirstOrDefault(candidate =>
                    candidate.Name == name && !candidate.IsSegregatedOverrides
                );

            if (group is null)
                continue;

            var added = group
                .Members.Where(member => member.Anchor?.Kind is not IrOverloadAnchorKind.Override)
                .Select(member => member.ConstructedIn(ancestor!))
                .ToList();

            if (group.InheritedOverloads is null)
                return group.Members.Count >= 2 ? added : null;

            var above = (DispatcherOverloadsAbove(ancestor!, name, policy) ?? []).Where(overload =>
                !added.Any(own => own.HasSameParametersAs(overload))
            );

            return [.. above, .. added];
        }

        return null;
    }

    /// <summary>
    /// A class the target emits, whose dispatched groups a derived class and
    /// a <c>base.M(...)</c> call can reach by fast path: transpiled, not
    /// <c>[External]</c>, not a BCL or special type.
    /// </summary>
    internal static bool IsEmittedBase(INamedTypeSymbol? type, TargetLanguage? target) =>
        type is { TypeKind: TypeKind.Class, SpecialType: SpecialType.None }
        && IrTypeRefMapper.IsTranspilableType(type.OriginalDefinition, target)
        && !SymbolHelper.HasExternal(type.OriginalDefinition);
}
