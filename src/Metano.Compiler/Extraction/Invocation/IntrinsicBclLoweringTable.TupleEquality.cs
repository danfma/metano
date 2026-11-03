using Metano.Compiler.Analysis;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.Extraction.Invocation;

/// <summary>
/// C# tuple value equality over the equality calls the declarative mappings
/// cannot lower on their own, because the answer needs the operands' static
/// types: <c>object.Equals(a, b)</c>, a tuple's own <c>Equals</c> — the
/// strongly-typed overload, <c>Equals(object)</c> (a boxed nullable argument
/// included), through <c>Nullable&lt;T&gt;.Equals</c> or a conditional access
/// (<c>t?.Equals(u)</c>) — and named arguments in either form. Every call is
/// recognized from its resolved method symbol, not its syntax shape, when
/// one operand unwraps to a tuple (the instance form: the receiver), and
/// lowered to one node.
/// <para>
/// <see cref="IrTupleEqualityExpression"/> when the other operand compares
/// as that tuple: the strongly-typed overload's argument (overload
/// resolution converted it to the receiver's instantiation, whatever its
/// natural type), or an operand that may hold the tuple — the same
/// instantiation (element names aside, a nullable tuple unwrapping to it),
/// <c>object</c>, <c>ValueType</c>, an interface, a type parameter (struct
/// constrained or not) or the <c>null</c> literal. The runtime comparison
/// null-guards both operands, so it answers what C# answers for whatever
/// that operand holds at run time.
/// </para>
/// <para>
/// A fold to C#'s answer when the other operand provably cannot hold the
/// tuple (<see cref="TupleEqualitySymbols.CannotHoldTuple"/>: another
/// instantiation, any other concrete value type, a class such as an
/// array): <c>false</c>, unless both operands can be <c>null</c> and are.
/// The operands still evaluate once, in order.
/// </para>
/// <para>
/// A receiver that is not a tuple is not recognized at all, so a user
/// <c>Equals</c> override (renamed or not, over a class, <c>string</c> or a
/// generic <c>T</c>) keeps the general lowering; so do two operands that
/// both may hold a tuple without either being one (<c>object</c> and
/// <c>object</c>). The recognizer runs before the declarative
/// <c>[MapMethod]</c> registry, so a user mapping of
/// <c>ValueTuple.Equals</c> does not apply to a recognized call.
/// </para>
/// <para>
/// Limitation (type erasure): a boxed value whose runtime shape is the
/// tuple's — an array or tuple of equal elements under a different C# type,
/// such as <c>object o = (1, 2L)</c> or an <c>int[]</c> boxed as
/// <c>object</c> — compares equal in TypeScript and Dart, where C# answers
/// <c>false</c>.
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

    /// <summary>
    /// Fixed binding names for the folded operands. <c>$</c> cannot appear
    /// in a C# identifier, so neither captures a source local, and the
    /// let-lowerings scope each binding to its own immediately invoked
    /// function, so a nested fold never reads an enclosing one's binding.
    /// </summary>
    private const string LeftTemp = "tuple$left";

    private const string RightTemp = "tuple$right";

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

        return symbol.IsStatic
            ? BuildStaticTupleEquality(ctx)
            : BuildInstanceTupleEquality(ctx, symbol);
    }

    /// <summary>
    /// <c>object.Equals(a, b)</c>: the operands in the order written — named
    /// arguments in either order included, since tuple equality is
    /// symmetric — each lowered once. One operand must unwrap to a tuple;
    /// the other compares against it as the instance form's argument does.
    /// </summary>
    private IrExpression? BuildStaticTupleEquality(InvocationRewriteContext ctx)
    {
        if (ctx.Invocation.ArgumentList.Arguments is not [{ } first, { } second])
            return null;

        var firstType = ctx.SemanticModel.GetTypeInfo(first.Expression).Type;
        var secondType = ctx.SemanticModel.GetTypeInfo(second.Expression).Type;

        var tuple =
            TupleEqualitySymbols.UnderlyingTupleOf(firstType)
            ?? TupleEqualitySymbols.UnderlyingTupleOf(secondType);

        if (tuple is null)
            return null;

        var left = ctx.Recurse(first.Expression);
        var right = ctx.Recurse(second.Expression);

        var eitherCannotHold =
            TupleEqualitySymbols.CannotHoldTuple(firstType, tuple)
            || TupleEqualitySymbols.CannotHoldTuple(secondType, tuple);

        return eitherCannotHold
            ? FoldedEquality(left, firstType, right, secondType)
            : TupleEqualityNode(tuple, left, right);
    }

    /// <summary>
    /// <c>receiver.Equals(argument)</c>, the receiver written as
    /// <c>t.Equals</c> or bound by a conditional access (<c>t?.Equals</c>).
    /// The receiver value lowers to the conditional placeholder in that
    /// form, so the enclosing null-conditional guard re-reads — never
    /// re-evaluates — the receiver.
    /// </summary>
    private IrExpression? BuildInstanceTupleEquality(
        InvocationRewriteContext ctx,
        IMethodSymbol symbol
    )
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

        var argumentType = ctx.SemanticModel.GetTypeInfo(argument.Expression).Type;

        var comparesAsTuple =
            IsStronglyTypedTupleEquals(symbol)
            || !TupleEqualitySymbols.CannotHoldTuple(argumentType, receiverTuple);

        if (comparesAsTuple)
        {
            return TupleEqualityNode(
                receiverTuple,
                ReceiverValueOf(ctx, receiver),
                ctx.Recurse(argument.Expression)
            );
        }

        // The enclosing null-conditional guard already evaluated the
        // receiver and answers null for it, so the fold is left with the
        // argument alone.
        if (receiver.IsConditionalAccess)
            return FalseAfter(ctx.Recurse(argument.Expression));

        return FoldedEquality(
            ctx.Recurse(receiver.Syntax),
            receiverType,
            ctx.Recurse(argument.Expression),
            argumentType
        );
    }

    /// <summary>
    /// The strongly-typed <c>ValueTuple.Equals(ValueTuple&lt;…&gt;)</c>:
    /// overload resolution converted the argument to the receiver's own
    /// instantiation (<c>(int, int)</c> to <c>(long, long)</c>), so it
    /// compares as that tuple whatever its natural type.
    /// </summary>
    private static bool IsStronglyTypedTupleEquals(IMethodSymbol symbol) =>
        symbol.Parameters is [{ Type: var parameterType }]
        && TupleEqualitySymbols.UnderlyingTupleOf(parameterType) is not null;

    private IrExpression? TupleEqualityNode(
        INamedTypeSymbol tuple,
        IrExpression left,
        IrExpression right
    )
    {
        var tupleTypeRef = _helpers.MapTypeRef(tuple);

        return tupleTypeRef is IrTupleTypeRef typeRef
            ? new IrTupleEqualityExpression(left, right, typeRef)
            : null;
    }

    /// <summary>
    /// C#'s answer for two operands of which one provably cannot hold the
    /// other's tuple: <c>false</c>, unless both can be <c>null</c> and are.
    /// The operands still evaluate once, in the order given.
    /// </summary>
    private static IrExpression FoldedEquality(
        IrExpression left,
        ITypeSymbol? leftType,
        IrExpression right,
        ITypeSymbol? rightType
    )
    {
        var bothMayBeNull =
            TupleEqualitySymbols.MayBeNull(leftType) && TupleEqualitySymbols.MayBeNull(rightType);

        return bothMayBeNull ? FalseUnlessBothNull(left, right) : FalseAfter(left, right);
    }

    /// <summary>
    /// <c>false</c> after every operand's evaluation, in order — the flat
    /// literal when all the operands are side-effect free.
    /// </summary>
    private static IrExpression FalseAfter(params IrExpression[] operands) =>
        operands.All(IrEvaluationTraits.IsSideEffectFree)
            ? FalseLiteral()
            : new IrSequenceExpression(operands, FalseLiteral());

    /// <summary>
    /// <c>false</c>, unless both operands are <c>null</c>. The operands are
    /// bound when either has side effects, so each still evaluates exactly
    /// once, left first.
    /// </summary>
    private static IrExpression FalseUnlessBothNull(IrExpression left, IrExpression right)
    {
        if (IrEvaluationTraits.IsSideEffectFree(left) && IrEvaluationTraits.IsSideEffectFree(right))
        {
            return new IrConditionalExpression(IsNull(left), IsNull(right), FalseLiteral());
        }

        return new IrLetExpression(
            LeftTemp,
            left,
            new IrLetExpression(
                RightTemp,
                right,
                new IrConditionalExpression(
                    IsNull(new IrIdentifier(LeftTemp)),
                    IsNull(new IrIdentifier(RightTemp)),
                    FalseLiteral()
                )
            )
        );
    }

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
