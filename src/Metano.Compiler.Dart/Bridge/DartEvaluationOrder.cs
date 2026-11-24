using Metano.Compiler.Dart.AST;
using Metano.Compiler.Diagnostics;
using Metano.Compiler.Extraction;
using Metano.Compiler.IR;

namespace Metano.Compiler.Dart.Bridge;

/// <summary>
/// Keeps C#'s argument evaluation order for Dart calls and constructions whose
/// named arguments are out of parameter order (spec 013 FR-017,
/// <see cref="IrCallExpression.EvaluationOrder"/>): binds the arguments (and any
/// non-trivial call receiver) to typed parameters of one immediately invoked
/// closure in the order the source evaluates them, then passes them
/// positionally in parameter order.
/// </summary>
internal static class DartEvaluationOrder
{
    private const string ReceiverParameterName = "__target";
    private const string ArgumentPrefix = "__arg";

    public static IrExpression BindCall(IrCallExpression call)
    {
        ReportIfOpaqueDefault(call.Arguments);

        var order = call.Overload?.EvaluationOrder ?? call.EvaluationOrder;

        if (order is null)
            return PlainCall(call);

        var (bindings, receiverParam, receiverType) = BindReceiverIfNeeded(call.Target);

        if (
            (receiverParam is not null && !IsKnownType(receiverType))
            || !CanTypeArguments(call.Arguments, order)
        )
        {
            ReportUntypedOrderFallback();
            return PlainCall(call);
        }

        var (parameters, boundArgs) = BindArguments(
            call.Arguments,
            order,
            bindings,
            receiverParam is not null ? receiverType : null
        );

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
            HasExplicitTypeArguments =
                call.HasExplicitTypeArguments || call.TypeArguments is { Count: > 0 },
        };

        return WrapInClosure(parameters, bindings, innerCall);
    }

    public static IrExpression BindNew(IrNewExpression creation)
    {
        ReportIfOpaqueDefault(creation.Arguments);

        if (creation.EvaluationOrder is not { } order)
            return PlainNew(creation);

        if (!CanTypeArguments(creation.Arguments, order))
        {
            ReportUntypedOrderFallback();
            return PlainNew(creation);
        }

        var bindings = new List<IrExpression>();
        var (parameters, boundArgs) = BindArguments(creation.Arguments, order, bindings, null);
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
        ReportIfOpaqueDefault(construction.Arguments);

        if (order is null)
            return construction with
            {
                Arguments = WrapExpandedParams(construction.Arguments, expandedParamsIndex),
            };

        if (!CanTypeArguments(construction.Arguments, order))
        {
            ReportUntypedOrderFallback();
            return construction with
            {
                Arguments = WrapExpandedParams(construction.Arguments, expandedParamsIndex),
            };
        }

        var bindings = new List<IrExpression>();
        var (parameters, boundArgs) = BindArguments(construction.Arguments, order, bindings, null);
        var innerConstruction = construction with
        {
            Arguments = WrapExpandedParams(boundArgs, expandedParamsIndex),
        };

        return WrapInClosure(parameters, bindings, innerConstruction);
    }

    private static IrCallExpression PlainCall(IrCallExpression call) =>
        ClearOrder(call) with
        {
            Arguments = WrapExpandedParams(call.Arguments, call.ExpandedParamsIndex),
            ExpandedParamsIndex = null,
        };

    private static IrNewExpression PlainNew(IrNewExpression creation) =>
        creation with
        {
            Arguments = WrapExpandedParams(creation.Arguments, creation.ExpandedParamsIndex),
            EvaluationOrder = null,
            ExpandedParamsIndex = null,
        };

    private static bool IsKnownType(IrTypeRef? type) => type is not (null or IrUnknownTypeRef);

    private static bool CanTypeArguments(
        IReadOnlyList<IrArgument> arguments,
        IReadOnlyList<int> order
    )
    {
        foreach (var position in order)
        {
            if (position < arguments.Count && !IsKnownType(arguments[position].Type))
                return false;
        }

        return true;
    }

    internal static void ReportIfOpaqueDefault(IReadOnlyList<IrArgument> arguments)
    {
        if (arguments.Any(arg => arg.Value is IrLiteral { Kind: IrLiteralKind.Default }))
        {
            ExtractionDiagnostics.Report(
                new MetanoDiagnostic(
                    MetanoDiagnosticSeverity.Warning,
                    DiagnosticCodes.UnsupportedFeature,
                    "A named-argument call in Dart cannot skip an optional parameter whose "
                        + "default value is not representable as a Dart constant."
                )
            );
        }
    }

    private static void ReportUntypedOrderFallback() =>
        ExtractionDiagnostics.Report(
            new MetanoDiagnostic(
                MetanoDiagnosticSeverity.Warning,
                DiagnosticCodes.UnsupportedFeature,
                "Named arguments out of parameter order with more than one evaluation that C# "
                    + "runs in written order are not supported by this target: a closure "
                    + "parameter type could not be resolved (spec 013 FR-017)."
            )
        );

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

    private static (
        List<IrExpression> Bindings,
        IrIdentifier? ReceiverParam,
        IrTypeRef? ReceiverType
    ) BindReceiverIfNeeded(IrExpression target)
    {
        var bindings = new List<IrExpression>();

        if (
            target is not IrMemberAccess access
            || access.Target is IrThisExpression or IrBaseExpression or IrTypeReference
        )
            return (bindings, null, null);

        var receiverType =
            access.Target is IrConditionalReceiver && access.TargetType is IrNullableTypeRef n
                ? n.Inner
                : access.TargetType;

        bindings.Add(access.Target);
        return (bindings, new IrIdentifier(ReceiverParameterName), receiverType);
    }

    private static (List<IrParameter> Parameters, List<IrArgument> BoundArgs) BindArguments(
        IReadOnlyList<IrArgument> arguments,
        IReadOnlyList<int> order,
        List<IrExpression> bindings,
        IrTypeRef? receiverType
    )
    {
        var parameters = new List<IrParameter>(order.Count + bindings.Count);

        if (bindings.Count > 0)
            parameters.Add(new IrParameter(ReceiverParameterName, receiverType!));

        var nameByPosition = new Dictionary<int, string>(order.Count);

        foreach (var position in order)
        {
            if (position >= arguments.Count)
                continue;

            var name = ArgumentPrefix + position;
            nameByPosition[position] = name;
            parameters.Add(new IrParameter(name, arguments[position].Type!));
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
