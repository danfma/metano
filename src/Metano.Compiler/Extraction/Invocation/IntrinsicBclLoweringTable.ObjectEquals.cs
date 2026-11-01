using Metano.Compiler.Analysis;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.Extraction.Invocation;

/// <summary>
/// <c>object.Equals(a, b)</c> with a C# tuple operand →
/// <c>tuple.Equals(other)</c>. The general lowering cannot tell a boxed tuple
/// from any other object at runtime (in TypeScript both are arrays, and a C#
/// array compares by reference), so it compares the tuple by reference. When
/// an operand is statically a (non-nullable) tuple, the call is retargeted to
/// that tuple's own <c>Equals(object)</c>, so the backend's
/// <c>ValueTuple.Equals</c> mapping applies (TypeScript's element-wise
/// <c>tupleEquals</c>; Dart maps neither call yet). The two calls agree: a
/// tuple is never <c>null</c>, and <c>object.Equals(a, b)</c> is
/// <c>a.Equals(b)</c> once <c>a</c> is not <c>null</c>.
/// <para>
/// A tuple statically typed <c>object</c> (boxed) still compares by
/// reference: only the static type tells the tuple apart.
/// </para>
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
    /// effects (it would otherwise run after the second); when it has some,
    /// the call keeps <c>object.Equals</c>'s own origin, the general lowering,
    /// which compares the tuple by reference. Each operand is extracted once,
    /// in source order, and only after the entry has claimed the call, so no
    /// diagnostic is reported twice.
    /// </summary>
    private IrExpression? BuildTupleEqualsCall(InvocationRewriteContext ctx)
    {
        var arguments = ctx.Invocation.ArgumentList.Arguments;

        if (!IsPositionalPair(arguments))
            return null;

        var leftEquals = TupleEqualsOf(arguments[0].Expression, ctx.SemanticModel);
        var rightEquals = TupleEqualsOf(arguments[1].Expression, ctx.SemanticModel);

        if (leftEquals is null && rightEquals is null)
            return null;

        var left = ctx.ExtractArgument(arguments[0]);
        var right = ctx.ExtractArgument(arguments[1]);

        if (leftEquals is not null)
            return TupleEqualsInvocation(leftEquals, left.Value, right);

        return IrEvaluationTraits.IsSideEffectFree(left.Value)
            ? TupleEqualsInvocation(rightEquals!, right.Value, left)
            : new IrCallExpression(
                ctx.Recurse(ctx.Invocation.Expression),
                [left, right],
                Origin: _helpers.BuildOrigin(ctx.Symbol!)
            );
    }

    private IrCallExpression TupleEqualsInvocation(
        IMethodSymbol tupleEquals,
        IrExpression receiverTuple,
        IrArgument other
    ) =>
        new(
            new IrMemberAccess(receiverTuple, tupleEquals.Name),
            [other],
            Origin: _helpers.BuildOrigin(tupleEquals)
        );

    /// <summary>
    /// Two positional arguments: a named one may not follow the operand order.
    /// </summary>
    private static bool IsPositionalPair(SeparatedSyntaxList<ArgumentSyntax> arguments) =>
        arguments.Count == 2 && arguments.All(a => a.NameColon is null);

    /// <summary>
    /// The <c>Equals(object)</c> of <paramref name="operand"/>'s tuple type,
    /// before the conversion to <c>object</c>; <c>null</c> when the operand is
    /// not a tuple, or is a nullable one (which may be <c>null</c>).
    /// </summary>
    private static IMethodSymbol? TupleEqualsOf(ExpressionSyntax operand, SemanticModel model) =>
        model.GetTypeInfo(operand).Type is INamedTypeSymbol { IsTupleType: true } tuple
            ? InstanceEqualsOverObject(tuple)
            : null;

    private static IMethodSymbol? InstanceEqualsOverObject(INamedTypeSymbol type) =>
        type.GetMembers(nameof(object.Equals))
            .OfType<IMethodSymbol>()
            .FirstOrDefault(m =>
                m
                    is {
                        IsStatic: false,
                        Parameters: [{ Type.SpecialType: SpecialType.System_Object }]
                    }
            );
}
