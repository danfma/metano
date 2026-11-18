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
        if (declared.OfType<DeclaredMember>().Any(member => IsOperator(member.Symbol)))
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
        overload is DeclaredMember inherited
        && declared
            .OfType<DeclaredMember>()
            .Any(member =>
                !member.Symbol.IsOverride
                && SymbolHelper.HasSameParameters(member.Symbol, inherited.Symbol)
            );

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
                .Select(member => ConstructedIn(ancestor!, member))
                .ToList();

            if (group.InheritedOverloads is null)
                return group.Members.Count >= 2 ? added : null;

            var above = (DispatcherOverloadsAbove(ancestor!, name, policy) ?? []).Where(overload =>
                !added.Any(own => HaveSameParameters(own, overload))
            );

            return [.. above, .. added];
        }

        return null;
    }

    private static bool HaveSameParameters(GroupMember member, GroupMember other) =>
        (member, other) switch
        {
            (DeclaredMember declared, DeclaredMember otherDeclared) =>
                SymbolHelper.HasSameParameters(declared.Symbol, otherDeclared.Symbol),
            (SynthesizedMember synthesized, SynthesizedMember otherSynthesized) => synthesized.Kind
                == otherSynthesized.Kind,
            _ => false,
        };

    /// <summary>
    /// A class the target emits, whose dispatched groups a derived class and
    /// a <c>base.M(...)</c> call can reach by fast path: transpiled, not
    /// <c>[External]</c>, not a BCL or special type.
    /// </summary>
    internal static bool IsEmittedBase(INamedTypeSymbol? type, TargetLanguage? target) =>
        type is { TypeKind: TypeKind.Class, SpecialType: SpecialType.None }
        && IrTypeRefMapper.IsTranspilableType(type.OriginalDefinition, target)
        && !SymbolHelper.HasExternal(type.OriginalDefinition);

    /// <summary>
    /// <paramref name="member"/> as the constructed <paramref name="type"/>
    /// declares it: a declared method with its type arguments substituted.
    /// </summary>
    private static GroupMember ConstructedIn(INamedTypeSymbol type, GroupMember member) =>
        member is DeclaredMember declared
            ? declared with
            {
                Symbol = ConstructedIn(type, declared.Symbol),
            }
            : member;

    /// <summary>The member of a constructed type that <paramref name="definition"/> declares.</summary>
    private static IMethodSymbol ConstructedIn(INamedTypeSymbol type, IMethodSymbol definition) =>
        type.GetMembers(definition.Name)
            .OfType<IMethodSymbol>()
            .First(member =>
                SymbolEqualityComparer.Default.Equals(
                    member.OriginalDefinition,
                    definition.OriginalDefinition
                )
            );
}
