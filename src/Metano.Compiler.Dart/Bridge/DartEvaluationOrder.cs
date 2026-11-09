using Metano.Compiler.Analysis;
using Metano.Compiler.IR;

namespace Metano.Compiler.Dart.Bridge;

/// <summary>
/// Keeps C#'s evaluation order for a call or construction whose bound
/// arguments are passed in parameter order (spec 013 FR-017,
/// <see cref="IrSelectedOverload.EvaluationOrder"/>): when named arguments put
/// a side effect out of parameter order, each argument is bound to a name in
/// the order the source evaluates it — a receiver that is not a plain value
/// first, as C# reads it before the arguments — and the call passes the names
/// in parameter order. The bindings are nested <see cref="IrLetExpression"/>s,
/// which Dart prints as immediately invoked closures, so the rewrite holds in
/// any expression position.
/// </summary>
internal static class DartEvaluationOrder
{
    private const string ArgumentPrefix = "$arg";
    private const string ReceiverName = "$recv";

    /// <summary>
    /// The call <paramref name="build"/> makes of <paramref name="callee"/> and
    /// <paramref name="arguments"/>, bound in <paramref name="order"/>; the
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
        var boundCallee = receiver is null
            ? callee
            : ((IrMemberAccess)callee) with
            {
                Target = new IrIdentifier(ReceiverName),
            };

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

        var bound = build(boundCallee, passed);

        foreach (var position in order.Reverse())
            bound = new IrLetExpression(
                ArgumentPrefix + position,
                arguments[position].Value,
                bound
            );

        return receiver is null ? bound : new IrLetExpression(ReceiverName, receiver, bound);
    }

    /// <summary>
    /// The receiver of an instance call that must evaluate once, before the
    /// arguments; <c>null</c> for a plain value (a local, <c>this</c>,
    /// <c>super</c>, a type), which reads the same inside the bindings.
    /// </summary>
    private static IrExpression? BoundReceiverOf(IrExpression callee) =>
        callee is IrMemberAccess { Target: var receiver }
        && !IrEvaluationTraits.IsReusableReceiver(receiver)
            ? receiver
            : null;
}
