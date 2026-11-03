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
/// recognized from its resolved method symbol, not its syntax shape, and
/// lowered to one node: <see cref="IrTupleEqualityExpression"/> when both
/// operands are statically the same <c>ValueTuple</c> instantiation (element
/// names aside, a nullable tuple unwrapping to it).
/// <para>
/// An argument that may hold the receiver's tuple — statically
/// <c>object</c>, <c>ValueType</c>, an interface the tuple implements
/// (<c>IEquatable&lt;(int, int)&gt;</c>, <c>ITuple</c>, …) or a type
/// parameter — lowers to the same node: the runtime comparison null-guards
/// both operands and answers what <c>Nullable&lt;T&gt;.Equals(object)</c>
/// answers, whatever the argument holds at run time.
/// </para>
/// <para>
/// An argument that cannot hold the tuple — statically another tuple, or
/// any other value type (its boxing is that value, never the tuple) — folds
/// to C#'s answer, <c>false</c>, unless both operands can be <c>null</c>
/// and are; the operands still evaluate once, receiver first. A receiver
/// that is not a tuple is not recognized at all, so a user
/// <c>Equals</c> override (renamed or not, over a class, <c>string</c> or a
/// generic <c>T</c>) keeps the general lowering.
/// </para>
/// <para>
/// A boxed tuple (an operand statically <c>object</c>) compares by value
/// when the other operand is statically the tuple; two operands both
/// statically <c>object</c> keep the reference comparison — at runtime
/// either may hold a C# array. The recognizer runs before the declarative
/// <c>[MapMethod]</c> registry, so a user mapping of
/// <c>ValueTuple.Equals</c> does not apply to a same-instantiation tuple
/// equality.
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

        var argumentType = ctx.SemanticModel.GetTypeInfo(argument.Expression).Type;
        var argumentTuple = TupleEqualitySymbols.UnderlyingTupleOf(argumentType);

        if (
            TupleEqualitySymbols.IsSameInstantiation(
                receiverTuple,
                argumentTuple,
                out var tupleType
            )
        )
        {
            return TupleEqualityNode(
                tupleType,
                ReceiverValueOf(ctx, receiver),
                ctx.Recurse(argument.Expression)
            );
        }

        // An argument that may hold the tuple (object, ValueType, an
        // interface, a type parameter) compares like the same instantiation
        // — the runtime null-guards answer whatever the argument holds. Only
        // a value-type argument cannot: its boxing is that value, never the
        // tuple, so C# answers false (unless both are null).
        if (argumentType is not { IsValueType: true })
        {
            return TupleEqualityNode(
                receiverTuple,
                ReceiverValueOf(ctx, receiver),
                ctx.Recurse(argument.Expression)
            );
        }

        if (receiver.IsConditionalAccess)
            return FalseAfter(ctx.Recurse(argument.Expression));

        if (!TupleEqualitySymbols.IsNullableTuple(receiverType))
            return FalseAfter(ctx.Recurse(receiver.Syntax), ctx.Recurse(argument.Expression));

        if (!TupleEqualitySymbols.MayBeNull(argumentType))
        {
            return FalseAfter(ctx.Recurse(receiver.Syntax), ctx.Recurse(argument.Expression));
        }

        return FalseUnlessBothNull(ctx.Recurse(receiver.Syntax), ctx.Recurse(argument.Expression));
    }

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
    /// <c>false</c> after every operand's evaluation (C# evaluates the
    /// receiver, then the boxed argument) — the flat literal when all the
    /// operands are side-effect free.
    /// </summary>
    private static IrExpression FalseAfter(params IrExpression[] operands) =>
        operands.All(AreSideEffectFree)
            ? FalseLiteral()
            : new IrSequenceExpression(operands, FalseLiteral());

    private static bool AreSideEffectFree(IrExpression operand) =>
        IrEvaluationTraits.IsSideEffectFree(operand);

    /// <summary>
    /// C#'s answer for a nullable tuple against a value-type argument that
    /// can itself be <c>null</c>: <c>false</c>, unless both operands are
    /// <c>null</c>. The operands are bound when either has side effects, so
    /// each still evaluates exactly once, receiver first.
    /// </summary>
    private static IrExpression FalseUnlessBothNull(IrExpression receiver, IrExpression argument)
    {
        if (AreSideEffectFree(receiver) && AreSideEffectFree(argument))
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
