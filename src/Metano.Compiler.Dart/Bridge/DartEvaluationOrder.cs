using Metano.Compiler.IR;

namespace Metano.Compiler.Dart.Bridge;

/// <summary>
/// Keeps C#'s evaluation order for a call or construction whose bound
/// arguments are passed in parameter order (spec 013 FR-017,
/// <see cref="IrSelectedOverload.EvaluationOrder"/>): when named arguments put
/// a side effect out of parameter order, the arguments whose evaluation can be
/// observed are passed, in the order the source evaluates them, to one
/// immediately invoked closure whose parameters the call then passes in
/// parameter order —
/// <c>(($recv, $arg1) => $recv.mAB(3, $arg1))(o.inner, swap(o))</c>.
/// Dart evaluates a call's arguments left to right, so a receiver an argument
/// could change evaluates first, as C# reads it before the arguments.
/// <para>
/// An argument whose evaluation nobody can observe — a literal, a constant, a
/// local no argument assigns, a lambda, a collection literal of such values —
/// stays inline in the call: its order does not matter, and Dart types it from
/// the parameter it is passed to (<c>2</c> as a <c>double</c>, <c>[]</c> as a
/// <c>List&lt;int&gt;</c>, a lambda's parameters), which a closure parameter
/// would lose. The closure parameters are untyped: a bound argument is an
/// expression with side effects (a call, an assignment, an object creation),
/// whose static type Dart computes from the expression itself, so the value
/// passes through unchanged. An <c>await</c> among the arguments is a closure
/// argument, evaluated in the enclosing function.
/// </para>
/// </summary>
internal static class DartEvaluationOrder
{
    private const string ArgumentPrefix = "$arg";
    private const string ReceiverName = "$recv";

    /// <summary>
    /// The call <paramref name="build"/> makes of <paramref name="callee"/> and
    /// <paramref name="arguments"/>, evaluated in <paramref name="order"/>; the
    /// call itself when <paramref name="order"/> is <c>null</c> or at most one
    /// evaluation is observable.
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

        var assigned = AssignedLocalsOf(arguments);

        var boundPositions = order
            .Where(position => !StaysInline(arguments[position].Value, assigned))
            .ToList();

        var receiver = BoundReceiverOf(callee, assigned);

        // One observable evaluation has no order to keep: the call stays as is.
        if (boundPositions.Count + (receiver is null ? 0 : 1) <= 1)
            return build(callee, arguments);

        var boundCallee = receiver is null ? callee : WithReceiver(callee, ReceiverName);

        var passed = arguments
            .Select(
                (argument, position) =>
                    boundPositions.Contains(position)
                        ? argument with
                        {
                            Value = new IrIdentifier(ArgumentPrefix + position),
                        }
                        : argument
            )
            .ToList();

        var bindings = boundPositions
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
    /// arguments; <c>null</c> when no argument can change it.
    /// </summary>
    private static IrExpression? BoundReceiverOf(
        IrExpression callee,
        IReadOnlySet<string> assigned
    ) =>
        callee is IrMemberAccess { Target: var receiver }
        && !IsUnchangeableByArguments(receiver, assigned)
            ? receiver
            : null;

    /// <summary>
    /// Whether an argument cannot change what <paramref name="receiver"/>
    /// reads: <c>this</c>, <c>super</c>, a type, the conditional receiver, or
    /// a local no argument assigns. Deliberately narrower than
    /// <see cref="Analysis.IrEvaluationTraits.IsReusableReceiver"/>, which
    /// accepts member-access chains: <c>o.inner</c> is reusable when nothing
    /// runs in between, but an argument may reassign it, so it is bound.
    /// </summary>
    private static bool IsUnchangeableByArguments(
        IrExpression receiver,
        IReadOnlySet<string> assigned
    ) =>
        receiver switch
        {
            IrIdentifier identifier => !assigned.Contains(identifier.Name),
            IrThisExpression or IrBaseExpression or IrTypeReference or IrConditionalReceiver =>
                true,
            _ => false,
        };

    /// <summary>
    /// Whether an argument's evaluation cannot be observed, so it stays inline
    /// and Dart types it from its parameter.
    /// </summary>
    private static bool StaysInline(IrExpression argument, IReadOnlySet<string> assigned) =>
        argument switch
        {
            IrLiteral or IrLambdaExpression or IrTypeReference => true,
            IrIdentifier identifier => !assigned.Contains(identifier.Name),
            IrMemberAccess { Origin.IsCompileTimeConstant: true } => true,
            IrArrayLiteral array => array.Elements.All(element => StaysInline(element, assigned)),
            IrUnaryExpression
            {
                Operator: not (IrUnaryOp.Increment or IrUnaryOp.Decrement)
            } unary => StaysInline(unary.Operand, assigned),
            _ => false,
        };

    /// <summary>The locals an argument assigns or increments.</summary>
    private static HashSet<string> AssignedLocalsOf(IReadOnlyList<IrArgument> arguments)
    {
        var assigned = new HashSet<string>(StringComparer.Ordinal);

        foreach (var argument in arguments)
            CollectAssignedLocals(argument.Value, assigned);

        return assigned;
    }

    private static void CollectAssignedLocals(IrExpression expression, HashSet<string> assigned)
    {
        if (AssignedLocalOf(expression) is { } name)
            assigned.Add(name);

        foreach (var child in DartIrChildren.ExpressionsOf(expression))
            CollectAssignedLocals(child, assigned);
    }

    private static string? AssignedLocalOf(IrExpression expression) =>
        expression switch
        {
            IrBinaryExpression { Left: IrIdentifier target } binary
                when binary.Operator.IsAssignment() => target.Name,
            IrUnaryExpression
            {
                Operator: IrUnaryOp.Increment or IrUnaryOp.Decrement,
                Operand: IrIdentifier target,
            } => target.Name,
            _ => null,
        };

    private static IrExpression WithReceiver(IrExpression callee, string name) =>
        ((IrMemberAccess)callee) with
        {
            Target = new IrIdentifier(name),
        };
}
