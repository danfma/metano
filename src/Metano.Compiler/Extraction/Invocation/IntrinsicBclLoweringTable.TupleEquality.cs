using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.Extraction.Invocation;

/// <summary>
/// C# tuple value equality over the equality calls the declarative mappings
/// cannot lower on their own, because the answer needs the operands' static
/// types: <c>object.Equals(a, b)</c>, a tuple's own <c>Equals</c> — the
/// strongly-typed overload, <c>Equals(object)</c> (so an argument boxed from
/// a nullable tuple, <c>t.Equals(nullableU)</c>, included), through
/// <c>Nullable&lt;T&gt;.Equals</c> or a conditional access
/// (<c>t?.Equals(u)</c>) — and named arguments in either form. Every call is
/// recognized from its resolved method symbol, not its syntax shape, and
/// lowered to one node: <see cref="IrTupleEqualityExpression"/> when both
/// operands are statically the same <c>ValueTuple</c> instantiation (element
/// names aside, a nullable tuple unwrapping to it).
/// <para>
/// Any other operand pair keeps the general lowering, which could answer
/// <c>true</c> where C# answers <c>false</c> (an <c>object</c> may hold an
/// array of equal elements; a tuple of another instantiation is a different
/// type, never equal). A nullable-tuple receiver whose argument is not the
/// same instantiation cannot fall through to the instance form — the lowered
/// <c>t.equals(o)</c> would crash on a positional array — so it retargets to
/// the static <c>object.Equals(t, o)</c> lowering (<c>objectEquals</c>),
/// which is null-safe and reference-compares a non-tuple.
/// </para>
/// <para>
/// A boxed tuple (an operand statically <c>object</c>) still compares by
/// reference: at runtime it cannot be told apart from a C# array. A generic
/// type parameter <c>T</c> is never a tuple here, so <c>T.Equals</c> keeps
/// the general lowering too.
/// </para>
/// </summary>
public sealed partial class IntrinsicBclLoweringTable
{
    private IMethodSymbol? _staticObjectEquals;

    private void SeedTupleEquality(Compilation compilation)
    {
        var objectType = compilation.GetSpecialType(SpecialType.System_Object);

        foreach (var equals in objectType.GetMembers(nameof(object.Equals)).OfType<IMethodSymbol>())
        {
            SeedObjectEqualsOverload(equals);
        }

        SeedValueTupleTypedEquals(compilation);
    }

    private void SeedObjectEqualsOverload(IMethodSymbol equals)
    {
        if (equals is { IsStatic: true, Parameters.Length: 2 })
        {
            _staticObjectEquals = equals;
            _entries[equals] = BuildTupleEquality;
            return;
        }

        // Every instance `Equals(object)` override roots here (ValueTuple's,
        // Nullable<T>'s, user records'), so this one entry sees them all and
        // declines for non-tuple receivers.
        if (
            equals is
            { IsStatic: false, Parameters: [{ Type.SpecialType: SpecialType.System_Object }] }
        )
        {
            _entries[equals.OriginalDefinition] = BuildTupleEquality;
        }
    }

    /// <summary>
    /// The strongly-typed <c>Equals(ValueTuple&lt;…&gt;)</c> of every arity:
    /// it overrides nothing, so it does not root at <c>object.Equals</c>.
    /// </summary>
    private void SeedValueTupleTypedEquals(Compilation compilation)
    {
        foreach (var arity in Enumerable.Range(1, SymbolHelper.MaxFlatTupleArity))
        {
            var valueTuple = compilation.GetTypeByMetadataName($"System.ValueTuple`{arity}");

            if (valueTuple is null)
                continue;

            foreach (
                var equals in valueTuple
                    .GetMembers(nameof(object.Equals))
                    .OfType<IMethodSymbol>()
                    .Where(m =>
                        m is { IsStatic: false, Parameters.Length: 1 }
                        && SymbolEqualityComparer.Default.Equals(m.Parameters[0].Type, valueTuple)
                    )
            )
            {
                _entries[equals.OriginalDefinition] = BuildTupleEquality;
            }
        }
    }

    private IrExpression? BuildTupleEquality(InvocationRewriteContext ctx)
    {
        if (ctx.Symbol is not { } symbol)
            return null;

        return symbol.IsStatic ? BuildStaticTupleEquality(ctx) : BuildInstanceTupleEquality(ctx);
    }

    /// <summary>
    /// <c>object.Equals(a, b)</c>: the operands in the order written — named
    /// arguments in either order included, since tuple equality is
    /// symmetric — each lowered once.
    /// </summary>
    private IrExpression? BuildStaticTupleEquality(InvocationRewriteContext ctx)
    {
        var arguments = ctx.Invocation.ArgumentList.Arguments;

        if (arguments.Count != 2)
            return null;

        return TupleEqualityOf(
            ctx,
            arguments[0].Expression,
            arguments[1].Expression,
            ctx.Recurse(arguments[0].Expression),
            retargetWhenMismatched: false
        );
    }

    /// <summary>
    /// <c>receiver.Equals(argument)</c>, the receiver written as
    /// <c>t.Equals</c> or bound by a conditional access (<c>t?.Equals</c>).
    /// The receiver value lowers to the conditional placeholder in that
    /// form, so the enclosing null-conditional guard re-reads — never
    /// re-evaluates — the receiver.
    /// </summary>
    private IrExpression? BuildInstanceTupleEquality(InvocationRewriteContext ctx)
    {
        if (ReceiverOperand.Of(ctx.Invocation) is not { } receiver)
            return null;

        if (ctx.Invocation.ArgumentList.Arguments is not [{ } argument])
            return null;

        var receiverType = ctx.SemanticModel.GetTypeInfo(receiver.Syntax).Type;
        var isNullableTuple =
            receiverType
                is INamedTypeSymbol
                {
                    OriginalDefinition.SpecialType: SpecialType.System_Nullable_T,
                } nullable
            && TupleEqualitySymbols.UnderlyingTupleOf(nullable.TypeArguments[0]) is not null;

        return TupleEqualityOf(
            ctx,
            receiver.Syntax,
            argument.Expression,
            ReceiverValueOf(ctx, receiver),
            retargetWhenMismatched: receiver.IsConditionalAccess || isNullableTuple
        );
    }

    /// <summary>
    /// The node when both operands are statically the same tuple
    /// instantiation; <c>null</c> to keep the general lowering. A nullable
    /// receiver over a different argument retargets to the static
    /// <c>object.Equals(receiver, argument)</c>, whose lowering is
    /// null-safe — see the class doc.
    /// </summary>
    private IrExpression? TupleEqualityOf(
        InvocationRewriteContext ctx,
        ExpressionSyntax leftSyntax,
        ExpressionSyntax rightSyntax,
        IrExpression leftValue,
        bool retargetWhenMismatched
    )
    {
        var leftTuple = TupleEqualitySymbols.UnderlyingTupleOf(
            ctx.SemanticModel.GetTypeInfo(leftSyntax).Type
        );
        var rightTuple = TupleEqualitySymbols.UnderlyingTupleOf(
            ctx.SemanticModel.GetTypeInfo(rightSyntax).Type
        );

        if (TupleEqualitySymbols.IsSameInstantiation(leftTuple, rightTuple))
        {
            return _helpers.MapTypeRef(leftTuple!) is IrTupleTypeRef tupleType
                ? new IrTupleEqualityExpression(leftValue, ctx.Recurse(rightSyntax), tupleType)
                : null;
        }

        if (!retargetWhenMismatched)
            return null;

        return StaticObjectEqualsCall(leftValue, ctx.Recurse(rightSyntax));
    }

    private IrExpression? StaticObjectEqualsCall(IrExpression left, IrExpression right)
    {
        if (_staticObjectEquals is not { } equals)
            return null;

        if (_helpers.BuildOrigin(equals) is not { } origin)
            return null;

        return new IrCallExpression(
            new IrMemberAccess(new IrIdentifier("object"), origin.MemberName, origin),
            [new IrArgument(left), new IrArgument(right)],
            Origin: origin
        );
    }

    private static IrExpression ReceiverValueOf(
        InvocationRewriteContext ctx,
        ReceiverOperand receiver
    ) => receiver.IsConditionalAccess ? new IrConditionalReceiver() : ctx.Recurse(receiver.Syntax);

    /// <summary>
    /// The receiver of an instance call: the expression a
    /// <c>MemberAccessExpressionSyntax</c> calls through, or the value a
    /// conditional access (<c>t?.Equals(u)</c>) guards.
    /// </summary>
    private readonly record struct ReceiverOperand(
        ExpressionSyntax Syntax,
        bool IsConditionalAccess
    )
    {
        internal static ReceiverOperand? Of(InvocationExpressionSyntax invocation) =>
            invocation.Expression switch
            {
                MemberAccessExpressionSyntax member => new ReceiverOperand(
                    member.Expression,
                    false
                ),
                MemberBindingExpressionSyntax binding => OfConditionalBinding(binding, invocation),
                _ => null,
            };

        /// <summary>
        /// <c>t?.Equals(u)</c>: the bound member sits on the inner invocation
        /// the conditional access wraps, and the receiver is the value that
        /// access guards.
        /// </summary>
        private static ReceiverOperand? OfConditionalBinding(
            MemberBindingExpressionSyntax binding,
            InvocationExpressionSyntax invocation
        )
        {
            if (!ReferenceEquals(binding.Parent, invocation))
                return null;

            if (invocation.Parent is not ConditionalAccessExpressionSyntax conditional)
                return null;

            return new ReceiverOperand(conditional.Expression, true);
        }
    }
}
