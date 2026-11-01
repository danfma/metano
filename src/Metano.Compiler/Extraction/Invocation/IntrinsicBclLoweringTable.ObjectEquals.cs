using Metano.Compiler.Analysis;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.Extraction.Invocation;

/// <summary>
/// <c>object.Equals(a, b)</c> with a C# tuple operand →
/// <c>tuple.Equals(other)</c>. The general lowering cannot tell a boxed tuple
/// from any other object at runtime (both are arrays in TypeScript, and a C#
/// array compares by reference), so it compares the tuple by reference. When
/// an operand is statically a (non-nullable) tuple, the call is retargeted to
/// that tuple's own <c>Equals(object)</c>, which each backend maps to its
/// element-wise comparison. The two calls agree: a tuple is never
/// <c>null</c>, and <c>object.Equals(a, b)</c> is <c>a.Equals(b)</c> once
/// <c>a</c> is not <c>null</c>.
/// </summary>
public sealed partial class IntrinsicBclLoweringTable
{
    private void SeedObjectEquals(Compilation compilation)
    {
        var staticEquals = compilation
            .GetSpecialType(SpecialType.System_Object)
            .GetMembers(nameof(object.Equals))
            .OfType<IMethodSymbol>()
            .Single(m => m.IsStatic && m.Parameters.Length == 2);

        _entries[staticEquals] = BuildTupleEqualsCall;
    }

    /// <summary>
    /// The receiver is the first tuple operand. When only the second operand
    /// is a tuple, the operands swap, so the first one must have no side
    /// effects (it would otherwise run after the second); the entry declines
    /// then, keeping the general lowering.
    /// </summary>
    private IrExpression? BuildTupleEqualsCall(InvocationRewriteContext ctx)
    {
        var arguments = ctx.Invocation.ArgumentList.Arguments;

        if (arguments.Count != 2 || arguments.Any(a => a.NameColon is not null))
            return null;

        var left = arguments[0].Expression;
        var right = arguments[1].Expression;

        if (TupleEqualsOf(left, ctx.SemanticModel) is { } leftEquals)
            return TupleEqualsCall(ctx, leftEquals, left, ctx.Recurse(right));

        if (TupleEqualsOf(right, ctx.SemanticModel) is not { } rightEquals)
            return null;

        var other = ctx.Recurse(left);

        return IrEvaluationTraits.IsSideEffectFree(other)
            ? TupleEqualsCall(ctx, rightEquals, right, other)
            : null;
    }

    private IrCallExpression TupleEqualsCall(
        InvocationRewriteContext ctx,
        IMethodSymbol tupleEquals,
        ExpressionSyntax tuple,
        IrExpression other
    ) =>
        new(
            new IrMemberAccess(ctx.Recurse(tuple), tupleEquals.Name),
            [new IrArgument(other)],
            Origin: _helpers.BuildOrigin(tupleEquals)
        );

    /// <summary>
    /// The <c>Equals(object)</c> of <paramref name="operand"/>'s tuple type,
    /// before the conversion to <c>object</c>; <c>null</c> when the operand is
    /// not a tuple, or is a nullable one (which may be <c>null</c>).
    /// </summary>
    private static IMethodSymbol? TupleEqualsOf(ExpressionSyntax operand, SemanticModel model) =>
        model.GetTypeInfo(operand).Type is INamedTypeSymbol { IsTupleType: true } tuple
            ? tuple
                .GetMembers(nameof(object.Equals))
                .OfType<IMethodSymbol>()
                .FirstOrDefault(m =>
                    m
                        is {
                            IsStatic: false,
                            Parameters: [{ Type.SpecialType: SpecialType.System_Object }]
                        }
                )
            : null;
}
