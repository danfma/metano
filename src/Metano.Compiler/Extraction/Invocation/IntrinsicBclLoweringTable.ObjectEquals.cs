using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.Extraction.Invocation;

/// <summary>
/// <c>object.Equals(a, b)</c> over two C# tuples → <c>a.Equals(b)</c>. The
/// general lowering cannot tell a boxed tuple from any other object at
/// runtime (in TypeScript both are arrays, and a C# array compares by
/// reference), so it compares the tuples by reference. When both operands are
/// statically (non-nullable) tuples, the call is retargeted to the first
/// one's own <c>Equals(object)</c>, so the backend's <c>ValueTuple.Equals</c>
/// mapping applies (TypeScript's element-wise <c>tupleEquals</c>; Dart maps
/// neither call yet). The two calls agree: a tuple is never <c>null</c>, and
/// <c>object.Equals(a, b)</c> is <c>a.Equals(b)</c> once <c>a</c> is not
/// <c>null</c>. Operands keep their source order.
/// <para>
/// Both must be tuples: against an operand of another static type (an array,
/// a class, or <c>object</c>, which may hold an array of equal elements), the
/// element-wise comparison could answer <c>true</c> where C# answers
/// <c>false</c>, so the call keeps the general lowering. A tuple statically
/// typed <c>object</c> (boxed) therefore still compares by reference.
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

    private IrExpression? BuildTupleEqualsCall(InvocationRewriteContext ctx)
    {
        var arguments = ctx.Invocation.ArgumentList.Arguments;

        if (!IsPositionalPair(arguments) || !IsTupleOperand(arguments[1].Expression, ctx))
            return null;

        if (TupleEqualsOf(arguments[0].Expression, ctx.SemanticModel) is not { } tupleEquals)
            return null;

        return new IrCallExpression(
            new IrMemberAccess(ctx.Recurse(arguments[0].Expression), tupleEquals.Name),
            [ctx.ExtractArgument(arguments[1])],
            Origin: _helpers.BuildOrigin(tupleEquals)
        );
    }

    /// <summary>
    /// Two positional arguments: a named one may not follow the operand order.
    /// </summary>
    private static bool IsPositionalPair(SeparatedSyntaxList<ArgumentSyntax> arguments) =>
        arguments.Count == 2 && arguments.All(a => a.NameColon is null);

    private static bool IsTupleOperand(ExpressionSyntax operand, InvocationRewriteContext ctx) =>
        TupleEqualsOf(operand, ctx.SemanticModel) is not null;

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
