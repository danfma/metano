using Metano.Compiler.IR;

namespace Metano.Compiler.Dart.Bridge;

/// <summary>
/// Keeps C#'s evaluation order for a call or construction whose bound
/// arguments are passed in parameter order (spec 013 FR-017,
/// <see cref="IrSelectedOverload.EvaluationOrder"/>): when named arguments put
/// a side effect out of parameter order, the arguments are passed, in the
/// order the source evaluates them, to one immediately invoked closure whose
/// parameters the call then passes in parameter order —
/// <c>(($recv, $arg1, $arg0) => $recv.subXY($arg0, $arg1))(c.inner, next(), next())</c>.
/// Dart evaluates a call's arguments left to right, so the receiver, unless it
/// is a plain value, evaluates first, as C# reads it before the arguments. The
/// arguments stay outside the closure, so an <c>await</c> among them keeps the
/// enclosing function's context.
/// </summary>
internal static class DartEvaluationOrder
{
    private const string ArgumentPrefix = "$arg";
    private const string ReceiverName = "$recv";

    /// <summary>
    /// The call <paramref name="build"/> makes of <paramref name="callee"/> and
    /// <paramref name="arguments"/>, evaluated in <paramref name="order"/>; the
    /// call itself when <paramref name="order"/> is <c>null</c>.
    /// </summary>
    public static IrExpression Bind(
        IrExpression callee,
        IReadOnlyList<IrArgument> arguments,
        IReadOnlyList<int>? order,
        Func<IrExpression, IReadOnlyList<IrArgument>, IrExpression> build
    )
    {
        if (order is null)
            return build(callee, arguments);

        var receiver = BoundReceiverOf(callee);
        var boundCallee = receiver is null ? callee : WithReceiver(callee, ReceiverName);

        var passed = arguments
            .Select(
                (argument, position) =>
                    order.Contains(position)
                        ? argument with
                        {
                            Value = new IrIdentifier(ArgumentPrefix + position),
                        }
                        : argument
            )
            .ToList();

        var bindings = order
            .Select(position => (Name: ArgumentPrefix + position, arguments[position].Value))
            .ToList();

        if (receiver is not null)
            bindings.Insert(0, (ReceiverName, receiver));

        var closure = new IrLambdaExpression(
            bindings
                .Select(binding => new IrParameter(
                    binding.Name,
                    new IrUnknownTypeRef(),
                    HasExplicitType: false
                ))
                .ToList(),
            ReturnType: null,
            Body: [new IrReturnStatement(build(boundCallee, passed))]
        );

        return new IrCallExpression(
            closure,
            bindings.Select(binding => new IrArgument(binding.Value)).ToList()
        );
    }

    /// <summary>
    /// The receiver of an instance call that must evaluate once, before the
    /// arguments; <c>null</c> for a plain value (a local, <c>this</c>,
    /// <c>super</c>, a type), which no argument can change. A member access
    /// chain (<c>o.inner</c>) is bound: an argument may reassign it.
    /// </summary>
    private static IrExpression? BoundReceiverOf(IrExpression callee) =>
        callee is IrMemberAccess { Target: var receiver } && !IsPlainValue(receiver)
            ? receiver
            : null;

    private static bool IsPlainValue(IrExpression expression) =>
        expression
            is IrIdentifier
                or IrThisExpression
                or IrBaseExpression
                or IrTypeReference
                or IrConditionalReceiver;

    private static IrExpression WithReceiver(IrExpression callee, string name) =>
        ((IrMemberAccess)callee) with
        {
            Target = new IrIdentifier(name),
        };
}
