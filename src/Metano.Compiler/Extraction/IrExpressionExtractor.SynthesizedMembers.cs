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
        if (symbol is null || SynthesizedKindOf(symbol) is not { } kind)
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
            Overload = bound.Overload,
        };
    }

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
        method.IsImplicitlyDeclared && method.ContainingType.IsRecord
        || method.ContainingType.SpecialType is SpecialType.System_ValueType;

    /// <summary>
    /// The synthesized <paramref name="kind"/> member of a type whose claims the
    /// target reads (declared in this compilation, emitted as a class) when it
    /// joined a group; <c>null</c> otherwise.
    /// </summary>
    private IrOverloadRef? SynthesizedRefOf(INamedTypeSymbol type, IrSynthesizedValueMember kind)
    {
        var definition = type.OriginalDefinition;

        if (definition.DeclaringSyntaxReferences.IsEmpty)
            return null;

        if (!IrTypeRefMapper.IsTranspilableType(definition, _target) || definition.HasExternal())
            return null;

        return MethodOverloadGroups.SynthesizedRefOf(definition, kind, _context.GroupingPolicy);
    }
}
