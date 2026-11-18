using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Extraction;

/// <summary>
/// One member of an overload group (spec 013 FR-038): a method the type
/// declares (<see cref="DeclaredMember"/>) or a value-equality member C#
/// synthesizes, described by data (<see cref="SynthesizedMember"/>). The facts
/// naming and dispatch read live here, so only the class extractor tells the
/// two apart.
/// </summary>
/// <param name="Ordinal">Its position among the members the type groups under
/// its C# name (<see cref="IrOverloadRef.Ordinal"/>); a synthesized member
/// comes after every declared one.</param>
/// <param name="Anchor">The root it reuses the names of; <c>null</c> when it
/// is its own root.</param>
internal abstract record GroupMember(int Ordinal, MemberAnchor? Anchor)
{
    /// <summary>Whether the member is static.</summary>
    public abstract bool IsStatic { get; }

    /// <summary>The fewest arguments a call passes it.</summary>
    public abstract int RequiredArity { get; }

    /// <summary>The most arguments a call passes it; <see cref="int.MaxValue"/> with <c>params</c>.</summary>
    public abstract int MaximumArity { get; }

    /// <summary>Whether the member stands for <paramref name="method"/>.</summary>
    public abstract bool StandsFor(IMethodSymbol method);
}

/// <summary>A method the type declares.</summary>
/// <param name="Symbol">The declared method.</param>
internal sealed record DeclaredMember(IMethodSymbol Symbol, int Ordinal, MemberAnchor? Anchor)
    : GroupMember(Ordinal, Anchor)
{
    public override bool IsStatic => Symbol.IsStatic;

    public override int RequiredArity =>
        Symbol.Parameters.Count(parameter => !parameter.IsOptional && !parameter.IsParams);

    public override int MaximumArity =>
        Symbol.Parameters.Any(parameter => parameter.IsParams)
            ? int.MaxValue
            : Symbol.Parameters.Length;

    public override bool StandsFor(IMethodSymbol method) =>
        SymbolEqualityComparer.Default.Equals(Symbol.OriginalDefinition, method.OriginalDefinition);
}

/// <summary>
/// A value-equality member C# synthesizes or inherits (spec 013 FR-038): its
/// shape is fixed by its kind (<c>Equals(object)</c>, <c>GetHashCode()</c>, the
/// copy behind <c>with</c> taking optional overrides) and its suffix is pinned
/// to the target's (DECIDED-34).
/// </summary>
/// <param name="Kind">Which member.</param>
/// <param name="Replacement">The user declaration that supplies its body: a
/// struct's <c>override Equals(object)</c> or an <c>override GetHashCode()</c>,
/// emitted only as this member. <c>null</c> when C# synthesizes it.</param>
internal sealed record SynthesizedMember(
    IrSynthesizedValueMember Kind,
    int Ordinal,
    MemberAnchor? Anchor,
    IMethodSymbol? Replacement = null
) : GroupMember(Ordinal, Anchor)
{
    public override bool IsStatic => false;

    public override int RequiredArity => Kind is IrSynthesizedValueMember.Equals ? 1 : 0;

    public override int MaximumArity => Kind is IrSynthesizedValueMember.HashCode ? 0 : 1;

    public override bool StandsFor(IMethodSymbol method) =>
        Replacement is not null
        && SymbolEqualityComparer.Default.Equals(
            Replacement.OriginalDefinition,
            method.OriginalDefinition
        );
}
