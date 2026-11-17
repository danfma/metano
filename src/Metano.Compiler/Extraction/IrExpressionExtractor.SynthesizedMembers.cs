using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Metano.Compiler.Extraction;

/// <summary>
/// Calls bound to a synthesized value-equality member of a type whose member
/// joined a group (spec 013 FR-043, OQ-10): they call its fast path
/// (<c>a.equalsByValue(b)</c>, <c>a.hashCodeByValue()</c>,
/// <c>a.withByValue({ … })</c>, <c>super.equalsByValue(other)</c>), since
/// through the dispatcher a same-type argument and a user overload would
/// compete. On a type whose member joined nothing they keep today's shape.
/// </summary>
public sealed partial class IrExpressionExtractor
{
    /// <summary>
    /// A call to a record's implicit <c>Equals(object)</c> / <c>Equals(R)</c> /
    /// <c>Equals(Base)</c> or <c>GetHashCode()</c>, or a struct's
    /// <c>ValueType.Equals</c> / <c>ValueType.GetHashCode()</c>, on a joined type:
    /// bound to the synthesized member's fast path. <c>null</c> for any other call.
    /// </summary>
    private IrCallExpression? TryExtractSynthesizedMemberCall(
        InvocationExpressionSyntax inv,
        IMethodSymbol? symbol
    )
    {
        if (symbol is null || SynthesizedMemberOf(symbol) is not { } kind)
            return null;

        if (_semantic.GetOperation(inv) is not IInvocationOperation operation)
            return null;

        var owner = IsBaseMemberCall(inv)
            ? symbol.ContainingType
            : operation.Instance?.Type as INamedTypeSymbol;

        if (owner is null || SynthesizedRefOf(owner, kind) is not { } reference)
            return null;

        var bound = ExtractBoundCall(operation, reference);

        if (bound is null)
            return null;

        return new IrCallExpression(
            Extract(inv.Expression),
            bound.Arguments,
            Origin: BuildOrigin(symbol)
        )
        {
            Overload = bound.Overload with { Synthesized = kind },
        };
    }

    /// <summary>
    /// The synthesized member a call to <paramref name="method"/> reaches: one C#
    /// synthesizes (<see cref="SynthesizedKindOf"/>), or one a user declaration
    /// was folded into (an <c>override GetHashCode()</c>), in this package or a
    /// referenced one.
    /// </summary>
    private IrSynthesizedValueMember? SynthesizedMemberOf(IMethodSymbol method) =>
        SynthesizedKindOf(method)
        ?? MethodOverloadGroups.SynthesizedKindOf(method, _context.GroupingPolicy);

    /// <summary>
    /// The synthesized member a call to <paramref name="method"/> reaches: a
    /// record's implicitly declared <c>Equals</c> or <c>GetHashCode</c>, or the
    /// one a struct inherits from <c>System.ValueType</c>.
    /// </summary>
    private static IrSynthesizedValueMember? SynthesizedKindOf(IMethodSymbol method)
    {
        if (method.IsStatic || !IsSynthesizedDeclaration(method))
            return null;

        return method switch
        {
            { Name: nameof(Equals), Parameters.Length: 1 } => IrSynthesizedValueMember.Equals,
            { Name: nameof(GetHashCode), Parameters.Length: 0 } =>
                IrSynthesizedValueMember.HashCode,
            _ => null,
        };
    }

    private static bool IsSynthesizedDeclaration(IMethodSymbol method) =>
        (method.IsImplicitlyDeclared || method.IsCompilerGenerated())
            && method.ContainingType.IsRecord
        || method.ContainingType.SpecialType is SpecialType.System_ValueType;

    /// <summary>
    /// The synthesized <paramref name="kind"/> member of an emitted type
    /// (<see cref="DispatchedMethodCalls.IsEmittedContainingType"/>), declared here
    /// or in a referenced package, when it joined a group; <c>null</c> otherwise.
    /// A referenced package's claims may be unreadable: the member's fast path
    /// keeps its stable name there (contracts/value-equality-members.md § 8).
    /// </summary>
    private IrOverloadRef? SynthesizedRefOf(INamedTypeSymbol type, IrSynthesizedValueMember kind)
    {
        var definition = type.OriginalDefinition;

        if (!DispatchedMethodCalls.IsEmittedContainingType(definition, _target))
            return null;

        return MethodOverloadGroups.SynthesizedRefOf(definition, kind, _context.GroupingPolicy);
    }

    /// <summary>
    /// <paramref name="overload"/>, marked with the synthesized member it binds
    /// when <paramref name="method"/> is a declaration folded into one (an
    /// <c>override GetHashCode()</c>), so a target names its stable fast path.
    /// </summary>
    private IrSelectedOverload MarkSynthesized(IrSelectedOverload overload, IMethodSymbol method) =>
        MethodOverloadGroups.SynthesizedKindOf(method, _context.GroupingPolicy) is { } kind
            ? overload with
            {
                Synthesized = kind,
            }
            : overload;
}
