using Metano.Compiler.Analysis;
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
/// The recognizer declines — leaving the general lowering untouched —
/// whenever the receiver itself does not unwrap to a tuple, so a user
/// <c>Equals</c> override (renamed or not) keeps being called. An argument
/// that is not the same instantiation declines too: the general lowering
/// could answer <c>true</c> where C# answers <c>false</c> (an
/// <c>object</c> may hold an array of equal elements; a tuple of another
/// instantiation is a different type, never equal). A nullable-tuple
/// receiver cannot fall through to the instance form — the lowered
/// <c>t.equals(o)</c> would crash on a positional array — so it lowers to
/// C#'s answer instead: <c>false</c>, unless both operands are
/// <c>null</c> (<c>false</c> after the argument's evaluation when the
/// receiver is guarded by a conditional access).
/// </para>
/// <para>
/// A boxed tuple (an operand statically <c>object</c>) still compares by
/// reference: at runtime it cannot be told apart from a C# array. A generic
/// type parameter <c>T</c> is never a tuple here, so <c>T.Equals</c> keeps
/// the general lowering too.
/// </para>
/// <para>
/// The recognizer runs before the declarative <c>[MapMethod]</c> registry,
/// so a user mapping of <c>ValueTuple.Equals</c> does not apply to a
/// same-instantiation tuple equality.
/// </para>
/// </summary>
public sealed partial class IntrinsicBclLoweringTable
{
    /// <summary>
    /// The widest tuple the equality is seeded for: an eight-element tuple
    /// rests on <c>ValueTuple&lt;…, TRest&gt;</c>, whose rest slot is a
    /// plain element to the shape.
    /// </summary>
    private const int TupleEqualsMaxArity = 8;

    private const string ReceiverTemp = "tuple$receiver";

    private const string ArgumentTemp = "tuple$argument";

    private void SeedTupleEquality(Compilation compilation)
    {
        var objectType = compilation.GetSpecialType(SpecialType.System_Object);

        foreach (var equals in objectType.GetMembers(nameof(object.Equals)).OfType<IMethodSymbol>())
        {
            SeedObjectEqualsOverload(equals);
        }

        foreach (var arity in Enumerable.Range(1, TupleEqualsMaxArity))
        {
            SeedStronglyTypedTupleEquals(compilation, arity);
        }
    }

    private void SeedObjectEqualsOverload(IMethodSymbol equals)
    {
        if (equals is { IsStatic: true, Parameters.Length: 2 })
        {
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
    /// The strongly-typed <c>Equals(ValueTuple&lt;…&gt;)</c> of one arity:
    /// it overrides nothing, so it does not root at <c>object.Equals</c>.
    /// </summary>
    private void SeedStronglyTypedTupleEquals(Compilation compilation, int arity)
    {
        var valueTuple = compilation.GetTypeByMetadataName($"System.ValueTuple`{arity}");

        if (valueTuple is null)
            return;

        var typedEquals = StronglyTypedEqualsOf(valueTuple);

        if (typedEquals is not null)
        {
            _entries[typedEquals.OriginalDefinition] = BuildTupleEquality;
        }
    }

    private static IMethodSymbol? StronglyTypedEqualsOf(INamedTypeSymbol valueTuple) =>
        valueTuple
            .GetMembers(nameof(object.Equals))
            .OfType<IMethodSymbol>()
            .FirstOrDefault(m =>
                m is { IsStatic: false, Parameters.Length: 1 }
                && SymbolEqualityComparer.Default.Equals(m.Parameters[0].Type, valueTuple)
            );

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

        var left = TupleEqualitySymbols.UnderlyingTupleOf(
            ctx.SemanticModel.GetTypeInfo(arguments[0].Expression).Type
        );
        var right = TupleEqualitySymbols.UnderlyingTupleOf(
            ctx.SemanticModel.GetTypeInfo(arguments[1].Expression).Type
        );

        if (!TupleEqualitySymbols.IsSameInstantiation(left, right, out var tupleType))
            return null;

        return TupleEqualityNode(
            tupleType,
            ctx.Recurse(arguments[0].Expression),
            ctx.Recurse(arguments[1].Expression)
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

        // Only a receiver that unwraps to a tuple is recognized: anything
        // else keeps the general lowering, so a user `Equals` override is
        // still the call the receiver makes.
        var receiverType = ctx.SemanticModel.GetTypeInfo(receiver.Syntax).Type;
        var receiverTuple = TupleEqualitySymbols.UnderlyingTupleOf(receiverType);

        if (receiverTuple is null)
            return null;

        var argumentTuple = TupleEqualitySymbols.UnderlyingTupleOf(
            ctx.SemanticModel.GetTypeInfo(argument.Expression).Type
        );

        var receiverValue = ReceiverValueOf(ctx, receiver);
        var argumentValue = ctx.Recurse(argument.Expression);

        if (
            TupleEqualitySymbols.IsSameInstantiation(
                receiverTuple,
                argumentTuple,
                out var tupleType
            )
        )
        {
            return TupleEqualityNode(tupleType, receiverValue, argumentValue);
        }

        if (receiver.IsConditionalAccess)
            return FalseAfter(argumentValue);

        if (!TupleEqualitySymbols.IsNullableTuple(receiverType))
            return null;

        var argumentType = ctx.SemanticModel.GetTypeInfo(argument.Expression).Type;

        return MismatchedNullableTuple(
            receiverValue,
            argumentValue,
            argumentMayBeNull: TupleEqualitySymbols.MayBeNull(argumentType)
        );
    }

    private IrExpression? TupleEqualityNode(
        INamedTypeSymbol tuple,
        IrExpression left,
        IrExpression right
    ) =>
        _helpers.MapTypeRef(tuple) is IrTupleTypeRef tupleType
            ? new IrTupleEqualityExpression(left, right, tupleType)
            : null;

    /// <summary>
    /// C#'s answer for a nullable tuple against a different instantiation:
    /// <c>false</c>, unless both operands are <c>null</c> — and nothing but
    /// the operands' evaluation when the argument can never be <c>null</c>.
    /// The operands are bound when either has side effects, so each still
    /// evaluates exactly once, receiver first.
    /// </summary>
    private static IrExpression MismatchedNullableTuple(
        IrExpression receiver,
        IrExpression argument,
        bool argumentMayBeNull
    )
    {
        if (!argumentMayBeNull)
            return FalseAfterOperands(receiver, argument);

        if (
            IrEvaluationTraits.IsSideEffectFree(receiver)
            && IrEvaluationTraits.IsSideEffectFree(argument)
        )
        {
            return new IrConditionalExpression(IsNull(receiver), IsNull(argument), FalseLiteral());
        }

        return new IrLetExpression(
            ReceiverTemp,
            receiver,
            new IrLetExpression(
                ArgumentTemp,
                argument,
                new IrConditionalExpression(
                    IsNull(new IrIdentifier(ReceiverTemp)),
                    IsNull(new IrIdentifier(ArgumentTemp)),
                    FalseLiteral()
                )
            )
        );
    }

    /// <summary>
    /// <c>false</c> after both operands' evaluation (C# evaluates the
    /// receiver, then the boxed argument), flat when neither has side
    /// effects.
    /// </summary>
    private static IrExpression FalseAfterOperands(IrExpression receiver, IrExpression argument)
    {
        if (
            IrEvaluationTraits.IsSideEffectFree(receiver)
            && IrEvaluationTraits.IsSideEffectFree(argument)
        )
            return FalseLiteral();

        return new IrSequenceExpression([receiver, argument], FalseLiteral());
    }

    /// <summary>
    /// <c>t?.Equals(o)</c> over a mismatched argument: the enclosing guard
    /// already answers <c>null</c> for a null receiver, so the body is
    /// <c>false</c> — after the argument's evaluation, which C# performs
    /// whenever the receiver has a value.
    /// </summary>
    private static IrExpression FalseAfter(IrExpression argument) =>
        IrEvaluationTraits.IsSideEffectFree(argument)
            ? FalseLiteral()
            : new IrSequenceExpression([argument], FalseLiteral());

    private static IrBinaryExpression IsNull(IrExpression operand) =>
        new(operand, IrBinaryOp.Equal, new IrLiteral(null, IrLiteralKind.Null));

    private static IrLiteral FalseLiteral() => new(false, IrLiteralKind.Boolean);

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
