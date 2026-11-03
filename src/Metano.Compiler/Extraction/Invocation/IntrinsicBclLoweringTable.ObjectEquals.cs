using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.Extraction.Invocation;

/// <summary>
/// <c>object.Equals(a, b)</c> over two C# tuples of the same type →
/// <c>a.Equals(b)</c>. The general lowering cannot tell a boxed tuple from any
/// other object at runtime (in TypeScript both are arrays, and a C# array
/// compares by reference), so it compares the tuples by reference. When both
/// operands are statically tuples of the same <c>ValueTuple</c> instantiation
/// (element names aside), the call is retargeted to the first one's own
/// <c>Equals(object)</c>, so the backend's <c>ValueTuple.Equals</c> mapping
/// applies (TypeScript's element-wise <c>tupleEquals</c>; Dart maps neither
/// call yet). Operands keep their source order.
/// <para>
/// A nullable tuple operand (<c>(int, int)?</c>, one or both) unwraps to the
/// same instantiation, so it retargets too: the runtime <c>tupleEquals</c>
/// guards the null cases C# answers — both null <c>true</c>, one null
/// <c>false</c>, else element-wise — and each operand is still evaluated
/// once. (A tuple is never <c>null</c> itself, so the non-nullable retarget
/// keeps agreeing with <c>object.Equals</c>.)
/// </para>
/// <para>
/// The retargeted call carries the receiver's tuple type, so a backend whose
/// tuples are positional arrays — where a nested tuple and a C# array are
/// indistinguishable — can compare nested tuple elements by value (#331).
/// </para>
/// <para>
/// Any other pair keeps the general lowering, where the element-wise
/// comparison could answer <c>true</c> while C# answers <c>false</c>: an
/// operand of another static type (an array, a class, or <c>object</c>, which
/// may hold an array of equal elements), or a tuple of another instantiation
/// (<c>(int, int)</c> against <c>(int, long)</c>: a different type, never
/// equal). So a boxed tuple (typed <c>object</c>) still compares by
/// reference: at runtime it cannot be told apart from a C# array, a
/// limitation the runtime keeps by design.
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

        if (!AreSameTupleType(first, second))
            return null;

        if (SymbolHelper.InstanceEqualsOverObject(first!) is not { } tupleEquals)
            return null;

        return new IrCallExpression(
            new IrMemberAccess(ctx.Recurse(arguments[0].Expression), tupleEquals.Name),
            [new IrArgument(ctx.Recurse(arguments[1].Expression))],
            Origin: _helpers.BuildOrigin(tupleEquals),
            TupleType: _helpers.MapTypeRef(first!) as IrTupleTypeRef
        );
    }

    /// <summary>
    /// <paramref name="operand"/>'s tuple type, before the conversion to
    /// <c>object</c>, unwrapping a nullable tuple (<c>(int, int)?</c>, which
    /// may be <c>null</c> at runtime) to the tuple itself; <c>null</c> when
    /// the operand is not a tuple.
    /// </summary>
    private static INamedTypeSymbol? TupleTypeOf(ExpressionSyntax operand, SemanticModel model)
    {
        var type = model.GetTypeInfo(operand).Type;

        if (
            type is INamedTypeSymbol
            {
                OriginalDefinition.SpecialType: SpecialType.System_Nullable_T,
            } nullable
        )
        {
            type = nullable.TypeArguments[0];
        }

        return type is INamedTypeSymbol { IsTupleType: true } tuple ? tuple : null;
    }

    /// <summary>
    /// Two tuples of the same <c>ValueTuple</c> instantiation: element names
    /// are compile-time only, so <c>(int x, int y)</c> and <c>(int p, int
    /// q)</c> are one type.
    /// </summary>
    private static bool AreSameTupleType(INamedTypeSymbol? first, INamedTypeSymbol? second) =>
        first is not null
        && second is not null
        && SymbolEqualityComparer.Default.Equals(
            first.TupleUnderlyingType ?? first,
            second.TupleUnderlyingType ?? second
        );
}
