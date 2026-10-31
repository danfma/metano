using Metano.Compiler.IR;

namespace Metano.Compiler.Extraction;

internal sealed partial class OutArgumentCallLowering
{
    /// <summary>
    /// Walks one statement's head expression in C# evaluation order, binding
    /// each out-argument call ahead of the statement while nothing evaluated
    /// before it could observe the move: literals, <c>this</c>, type names,
    /// lambdas and reads of locals the call does not assign. Anything else (a
    /// member or element read, a call, an assignment, an <c>await</c>, a
    /// short-circuit or conditional operand) stops the binding for the rest of
    /// the expression; the calls after it stay in place. Member reads are
    /// treated as order-sensitive even when they are plain fields.
    /// </summary>
    private sealed class HeadEvaluation(OutArgumentCallLowering lowering, List<IrStatement> prelude)
    {
        private readonly OutArgumentCallLowering _lowering = lowering;
        private readonly List<IrStatement> _prelude = prelude;
        private readonly HashSet<string> _localsRead = new(StringComparer.Ordinal);
        private bool _stopped;

        public IrExpression Bind(IrExpression expression)
        {
            if (_stopped)
                return expression;

            switch (expression)
            {
                case IrOutArgumentCall call:
                    return BindCall(call);

                case IrIdentifier identifier:
                    _localsRead.Add(identifier.Name);
                    return expression;

                case IrLiteral
                or IrThisExpression
                or IrTypeReference
                or IrLambdaExpression:
                    return expression;

                case IrUnaryExpression { Operator: IrUnaryOp.Increment or IrUnaryOp.Decrement }:
                    _stopped = true;
                    return expression;

                case IrUnaryExpression unary:
                    return unary with { Operand = Bind(unary.Operand) };

                case IrCastExpression cast:
                    return cast with { Expression = Bind(cast.Expression) };

                case IrTypeCheck check:
                    return check with { Expression = Bind(check.Expression) };

                case IrBinaryExpression binary:
                    return BindBinary(binary);

                case IrConditionalExpression conditional:
                    return StopAfter(conditional with { Condition = Bind(conditional.Condition) });

                case IrArrayLiteral array:
                    return array with { Elements = array.Elements.Select(Bind).ToList() };

                case IrTupleLiteral tuple:
                    return tuple with { Elements = tuple.Elements.Select(Bind).ToList() };

                case IrCallExpression call:
                    return StopAfter(BindCallParts(call));

                case IrMemberAccess access:
                    return StopAfter(access with { Target = Bind(access.Target) });

                case IrAwaitExpression awaited:
                    return StopAfter(awaited with { Expression = Bind(awaited.Expression) });

                default:
                    _stopped = true;
                    return expression;
            }
        }

        private IrExpression StopAfter(IrExpression expression)
        {
            _stopped = true;

            return expression;
        }

        private IrExpression BindBinary(IrBinaryExpression binary)
        {
            if (
                binary.Operator
                is IrBinaryOp.LogicalAnd
                    or IrBinaryOp.LogicalOr
                    or IrBinaryOp.NullCoalescing
            )
                return StopAfter(binary with { Left = Bind(binary.Left) });

            if (binary.Operator.IsAssignment())
            {
                if (binary.Left is not IrIdentifier target)
                    return StopAfter(binary);

                // A compound assignment reads its target before the right side.
                if (binary.Operator is not IrBinaryOp.Assign)
                    _localsRead.Add(target.Name);

                return StopAfter(binary with { Right = Bind(binary.Right) });
            }

            var left = Bind(binary.Left);

            return binary with
            {
                Left = left,
                Right = Bind(binary.Right),
            };
        }

        /// <summary>
        /// Binds the calls <paramref name="call"/>'s receiver and arguments
        /// make ahead of it, in evaluation order.
        /// </summary>
        public IrCallExpression BindCallParts(IrCallExpression call)
        {
            var target = call.Target is IrMemberAccess access
                ? access with
                {
                    Target = Bind(access.Target),
                }
                : call.Target;

            var arguments = call
                .Arguments.Select(argument => argument with { Value = Bind(argument.Value) })
                .ToList();

            return call with
            {
                Target = target,
                Arguments = arguments,
            };
        }

        /// <summary>
        /// Binds <paramref name="call"/> ahead of the statement. Its own
        /// receiver and arguments run inside the binding, so they may bind
        /// nested calls first and never stop the walk for what follows.
        /// </summary>
        private IrExpression BindCall(IrOutArgumentCall call)
        {
            if (call.ResultType is null || AssignsLocalReadBefore(call))
                return StopAfter(call);

            var inner = BindCallParts(call.Call);

            // The receiver and arguments run inside the binding, so whatever
            // stopped the walk among them does not stop what follows the call.
            _stopped = false;

            var bound = call with { Call = inner };

            return _lowering.Bind(bound, ResultSlot.Temporary, _prelude)!;
        }

        private bool AssignsLocalReadBefore(IrOutArgumentCall call) =>
            call
                .OutArguments.Select(o => o.Target)
                .OfType<IrOutAssignment>()
                .Any(o =>
                    o.Destination is IrIdentifier { Name: var name } && _localsRead.Contains(name)
                );
    }
}
