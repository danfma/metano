using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.Extraction.Invocation;

/// <summary>
/// <c>object.Equals(a, b)</c> over two C# tuples of the same type →
/// <c>a.Equals(b)</c>. The general lowering cannot tell a boxed tuple from any
/// other object at runtime (in TypeScript both are arrays, and a C# array
/// compares by reference), so it compares the tuples by reference. When both
/// operands are statically (non-nullable) tuples of the same
/// <c>ValueTuple</c> instantiation (element names aside), the call is
/// retargeted to the first one's own <c>Equals(object)</c>, so the backend's
/// <c>ValueTuple.Equals</c> mapping applies (TypeScript's element-wise
/// <c>tupleEquals</c>; Dart maps neither call yet). The two calls agree: a
/// tuple is never <c>null</c>, and <c>object.Equals(a, b)</c> is
/// <c>a.Equals(b)</c> once <c>a</c> is not <c>null</c>. Operands keep their
/// source order.
/// <para>
/// Any other pair keeps the general lowering, where the element-wise
/// comparison could answer <c>true</c> while C# answers <c>false</c>: an
/// operand of another static type (an array, a class, or <c>object</c>, which
/// may hold an array of equal elements), or a tuple of another instantiation
/// (<c>(int, int)</c> against <c>(int, long)</c>: a different type, never
/// equal). So a boxed tuple (typed <c>object</c>) and a nullable tuple
/// (<c>(int, int)?</c>, which may be <c>null</c> and has no receiver to
/// retarget to) still compare by reference.
/// </para>
/// <para>
/// Known gap: <c>tupleEquals</c> compares a nested tuple element by
/// reference, so <c>object.Equals(((1, 2), 3), ((1, 2), 3))</c> is
/// <c>false</c> in TypeScript and <c>true</c> in C#. At runtime a nested tuple
/// and a C# array are both arrays, so a fix needs the element types from the
/// compiler.
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
    /// The receiver is the operand written first. Tuple equality is
    /// symmetric, so this holds for named arguments in either order
    /// (<c>Equals(objB: t, objA: u)</c>) and keeps the source evaluation
    /// order.
    /// </summary>
    private IrExpression? BuildTupleEqualsCall(InvocationRewriteContext ctx)
    {
        var arguments = ctx.Invocation.ArgumentList.Arguments;

        if (arguments.Count != 2)
            return null;

        var first = TupleTypeOf(arguments[0].Expression, ctx.SemanticModel);
        var second = TupleTypeOf(arguments[1].Expression, ctx.SemanticModel);

        if (first is null || second is null || !IsSameTupleType(first, second))
            return null;

        if (InstanceEqualsOverObject(first) is not { } tupleEquals)
            return null;

        return new IrCallExpression(
            new IrMemberAccess(ctx.Recurse(arguments[0].Expression), tupleEquals.Name),
            [new IrArgument(ctx.Recurse(arguments[1].Expression))],
            Origin: _helpers.BuildOrigin(tupleEquals)
        );
    }

    /// <summary>
    /// <paramref name="operand"/>'s tuple type, before the conversion to
    /// <c>object</c>; <c>null</c> when the operand is not a tuple, or is a
    /// nullable one (which may be <c>null</c>).
    /// </summary>
    private static INamedTypeSymbol? TupleTypeOf(ExpressionSyntax operand, SemanticModel model) =>
        model.GetTypeInfo(operand).Type is INamedTypeSymbol { IsTupleType: true } tuple
            ? tuple
            : null;

    /// <summary>
    /// The same <c>ValueTuple</c> instantiation: element names are compile-time
    /// only, so <c>(int x, int y)</c> and <c>(int p, int q)</c> are one type.
    /// </summary>
    private static bool IsSameTupleType(INamedTypeSymbol first, INamedTypeSymbol second) =>
        SymbolEqualityComparer.Default.Equals(
            first.TupleUnderlyingType ?? first,
            second.TupleUnderlyingType ?? second
        );

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
