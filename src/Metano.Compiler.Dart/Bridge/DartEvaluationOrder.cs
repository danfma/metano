using Metano.Compiler.Dart.AST;
using Metano.Compiler.IR;

namespace Metano.Compiler.Dart.Bridge;

/// <summary>
/// Keeps C#'s argument evaluation order for Dart calls and constructions whose
/// named arguments are out of parameter order (spec 013 FR-017,
/// <see cref="IrCallExpression.EvaluationOrder"/>): binds the arguments (and any
/// non-trivial call receiver) to parameters of one immediately invoked closure in
/// the order the source evaluates them, then passes them positionally in
/// parameter order.
/// </summary>
internal static class DartEvaluationOrder
{
    private const string ReceiverParameterName = "__target";
    private const string ArgumentPrefix = "__arg";

    public static IrExpression BindCall(IrCallExpression call)
    {
        var order = call.Overload?.EvaluationOrder ?? call.EvaluationOrder;

        if (order is null)
            return ClearOrder(call) with
            {
                Arguments = WrapExpandedParams(call.Arguments, call.ExpandedParamsIndex),
                ExpandedParamsIndex = null,
            };

        var (bindings, receiverParam) = BindReceiverIfNeeded(call.Target);
        var (parameters, boundArgs) = BindArguments(call.Arguments, order, bindings);

        var target =
            receiverParam is not null && call.Target is IrMemberAccess access
                ? access with
                {
                    Target = receiverParam,
                }
                : call.Target;

        var innerCall = ClearOrder(call) with
        {
            Target = target,
            Arguments = WrapExpandedParams(boundArgs, call.ExpandedParamsIndex),
            ExpandedParamsIndex = null,
        };

        return WrapInClosure(parameters, bindings, innerCall);
    }

    public static IrExpression BindNew(IrNewExpression creation)
    {
        if (creation.EvaluationOrder is not { } order)
            return creation with
            {
                Arguments = WrapExpandedParams(creation.Arguments, creation.ExpandedParamsIndex),
                ExpandedParamsIndex = null,
            };

        var bindings = new List<IrExpression>();
        var (parameters, boundArgs) = BindArguments(creation.Arguments, order, bindings);
        var innerNew = creation with
        {
            Arguments = WrapExpandedParams(boundArgs, creation.ExpandedParamsIndex),
            EvaluationOrder = null,
            ExpandedParamsIndex = null,
        };

        return WrapInClosure(parameters, bindings, innerNew);
    }

    public static IrExpression BindNamedConstruction(
        DartNamedConstruction construction,
        IReadOnlyList<int>? order,
        int? expandedParamsIndex = null
    )
    {
        if (order is null)
            return construction with
            {
                Arguments = WrapExpandedParams(construction.Arguments, expandedParamsIndex),
            };

        var bindings = new List<IrExpression>();
        var (parameters, boundArgs) = BindArguments(construction.Arguments, order, bindings);
        var innerConstruction = construction with
        {
            Arguments = WrapExpandedParams(boundArgs, expandedParamsIndex),
        };

        return WrapInClosure(parameters, bindings, innerConstruction);
    }

    private static IReadOnlyList<IrArgument> WrapExpandedParams(
        IReadOnlyList<IrArgument> arguments,
        int? expandedParamsIndex
    )
    {
        if (expandedParamsIndex is not { } index || index > arguments.Count)
            return arguments;

        var elements = arguments.Skip(index).Select(arg => arg.Value).ToList();

        return [.. arguments.Take(index), new IrArgument(new IrArrayLiteral(elements))];
    }

    private static IrCallExpression ClearOrder(IrCallExpression call) =>
        call with
        {
            EvaluationOrder = null,
            Overload = call.Overload is { } overload
                ? overload with
                {
                    EvaluationOrder = null,
                }
                : null,
        };

    private static (List<IrExpression> Bindings, IrIdentifier? ReceiverParam) BindReceiverIfNeeded(
        IrExpression target
    )
    {
        var bindings = new List<IrExpression>();

        if (
            target is not IrMemberAccess access
            || access.Target is IrThisExpression or IrBaseExpression or IrTypeReference
        )
            return (bindings, null);

        bindings.Add(access.Target);
        return (bindings, new IrIdentifier(ReceiverParameterName));
    }

    private static (List<IrParameter> Parameters, List<IrArgument> BoundArgs) BindArguments(
        IReadOnlyList<IrArgument> arguments,
        IReadOnlyList<int> order,
        List<IrExpression> bindings
    )
    {
        var parameters = new List<IrParameter>(order.Count + bindings.Count);

        if (bindings.Count > 0)
            parameters.Add(new IrParameter(ReceiverParameterName, new IrUnknownTypeRef()));

        var nameByPosition = new Dictionary<int, string>(order.Count);

        foreach (var position in order)
        {
            if (position >= arguments.Count)
                continue;

            var name = ArgumentPrefix + position;
            nameByPosition[position] = name;
            parameters.Add(new IrParameter(name, new IrUnknownTypeRef()));
            bindings.Add(arguments[position].Value);
        }

        var boundArgs = arguments
            .Select(
                (arg, index) =>
                    nameByPosition.TryGetValue(index, out var name)
                        ? new IrArgument(new IrIdentifier(name))
                        : arg
            )
            .ToList();

        return (parameters, boundArgs);
    }

    private static IrCallExpression WrapInClosure(
        IReadOnlyList<IrParameter> parameters,
        IReadOnlyList<IrExpression> bindings,
        IrExpression inner
    )
    {
        var lambda = new IrLambdaExpression(
            parameters,
            ReturnType: null,
            [new IrReturnStatement(inner)]
        );
        var args = bindings.Select(value => new IrArgument(value)).ToList();

        return new IrCallExpression(lambda, args);
    }
}
