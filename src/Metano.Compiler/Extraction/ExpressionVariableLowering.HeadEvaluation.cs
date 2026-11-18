using Metano.Compiler.IR;

namespace Metano.Compiler.Extraction;

internal sealed partial class ExpressionVariableLowering
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
    private sealed class HeadEvaluation(
        ExpressionVariableLowering lowering,
        List<IrStatement> prelude
    )
    {
        private readonly ExpressionVariableLowering _lowering = lowering;
        private readonly List<IrStatement> _prelude = prelude;
        private readonly HashSet<string> _localsRead = new(StringComparer.Ordinal);

        private readonly Dictionary<IrOutArgumentCall, IrExpression> _boundCalls = new(
            ReferenceEqualityComparer.Instance
        );

        private bool _stopped;
        private bool _substitutesOnly;

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

                case IrNumericConversion conversion:
                    return conversion with { Operand = Bind(conversion.Operand) };

                case IrUserOperatorApplication application:
                    return StopAfter(
                        application with
                        {
                            Operands = application.Operands.Select(Bind).ToList(),
                        }
                    );

                case IrUserConversion userConversion:
                    return BindUserConversion(userConversion);

                case IrUserOperatorStore store:
                    return BindUserStore(store);

                case IrIntegerOverflow overflow:
                    return overflow with { Value = Bind(overflow.Value) };

                case IrTypeCheck check:
                    return check with { Expression = Bind(check.Expression) };

                case IrBinaryExpression binary:
                    return BindBinary(binary);

                case IrConditionalExpression conditional:
                    return StopAfter(conditional with { Condition = Bind(conditional.Condition) });

                case IrArrayLiteral array:
                    return array with { Elements = array.Elements.Select(Bind).ToList() };

                case IrArrayCreation creation:
                    return creation with { Length = Bind(creation.Length) };

                case IrTupleLiteral tuple:
                    return tuple with { Elements = tuple.Elements.Select(Bind).ToList() };

                case IrTupleEqualityExpression tupleEquality:
                    return tupleEquality with
                    {
                        Left = Bind(tupleEquality.Left),
                        Right = Bind(tupleEquality.Right),
                    };

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

        /// <summary>
        /// A user conversion binds its operand, and its source form (the
        /// form Dart prints) takes the same bindings without hoisting
        /// anything of its own (<see cref="WithSameBindings"/>).
        /// </summary>
        private IrExpression BindUserConversion(IrUserConversion conversion)
        {
            var (operand, sourceForm) = WithSameBindings(
                () => Bind(conversion.Operand),
                () => Bind(conversion.SourceForm)
            );

            return StopAfter(conversion with { Operand = operand, SourceForm = sourceForm });
        }

        /// <summary>
        /// A user compound assignment or increment binds its read-modify-write
        /// as an assignment does, and its source form takes the same bindings.
        /// </summary>
        private IrExpression BindUserStore(IrUserOperatorStore store)
        {
            var (readModifyWrite, sourceForm) = WithSameBindings(
                () => Bind(store.ReadModifyWrite),
                () => Bind(store.SourceForm)
            );

            return StopAfter(
                store with
                {
                    ReadModifyWrite = readModifyWrite,
                    SourceForm = sourceForm,
                }
            );
        }

        /// <summary>
        /// Binds <paramref name="bound"/>, then walks <paramref name="mirrored"/>
        /// from the same state, replacing only the out-argument calls the
        /// first walk bound: both forms of a node evaluate the same calls,
        /// and each call is bound once.
        /// </summary>
        private (IrExpression Bound, IrExpression Mirrored) WithSameBindings(
            Func<IrExpression> bound,
            Func<IrExpression> mirrored
        )
        {
            var stoppedBefore = _stopped;
            var first = bound();
            var stoppedAfter = _stopped;

            _stopped = stoppedBefore;
            _substitutesOnly = true;

            var second = mirrored();

            _substitutesOnly = false;
            _stopped = stoppedAfter;

            return (first, second);
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

            // Binding hoists a call ahead of the statement, so the arguments
            // are visited in the order the source evaluates them.
            var sourceOrder = call.EvaluationOrder
                ?? Enumerable.Range(0, call.Arguments.Count).ToList();

            var bound = sourceOrder.ToDictionary(
                position => position,
                position => Bind(call.Arguments[position].Value)
            );

            var arguments = call
                .Arguments.Select((argument, position) => argument with { Value = bound[position] })
                .ToList();

            // Arguments that all read a binding or a constant now have no
            // order left to keep.
            var readsPlainValues = arguments.All(argument =>
                argument.Value is IrIdentifier or IrLiteral
            );

            return call with
            {
                Target = target,
                Arguments = arguments,
                EvaluationOrder = readsPlainValues ? null : call.EvaluationOrder,
            };
        }

        /// <summary>
        /// Binds <paramref name="call"/> ahead of the statement. Its own
        /// receiver and arguments run inside the binding, so they may bind
        /// nested calls first and never stop the walk for what follows.
        /// </summary>
        private IrExpression BindCall(IrOutArgumentCall call)
        {
            if (_boundCalls.TryGetValue(call, out var already))
                return already;

            if (_substitutesOnly || call.ResultType is null || AssignsLocalReadBefore(call))
                return StopAfter(call);

            var inner = BindCallParts(call.Call);

            // The receiver and arguments run inside the binding, so whatever
            // stopped the walk among them does not stop what follows the call.
            _stopped = false;

            var bound = call with { Call = inner };
            var result = _lowering.Bind(bound, ResultSlot.Temporary, _prelude)!;

            _boundCalls[call] = result;

            return result;
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
