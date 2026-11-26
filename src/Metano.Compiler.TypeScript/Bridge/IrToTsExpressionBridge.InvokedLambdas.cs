using Metano.Compiler.Analysis;
using Metano.Compiler.IR;
using Metano.Compiler.TypeScript.AST;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// An expression lambda invoked on the spot — an <c>[Inline]</c> expansion whose
/// arguments must evaluate once, a fused LINQ predicate calling the predicates it
/// joins — lowered with no closure (W13-1, #804): its parameters become
/// temporaries of the enclosing statement list, assigned the arguments in order
/// before the body, <c>($value = next(), ($value + $value) | 0)</c>. Each
/// parameter gets its own copy even when the argument is a same-named local: the
/// body may write the parameter, which must not reach the caller's local (W13-1b).
/// </summary>
public static partial class IrToTsExpressionBridge
{
    /// <summary>
    /// The invoked lambda over temporaries of the statement list, or <c>null</c>
    /// where that does not apply: outside a statement list, with a spread
    /// argument, or when a function in the lowered body reads a parameter
    /// (<see cref="CapturesABinding"/>; the arrow's parameter is a binding per
    /// call). The temporaries replace the parameters by name; a function nested in
    /// the body that declares the same name shadows them
    /// (<see cref="WithoutShadowedBindings"/>). A parameter with a type keeps it on its temporary
    /// (<c>let $value: number;</c>), as the arrow's parameter had it.
    /// </summary>
    private static TsExpression? TryMapInvokedLambdaInline(
        IrCallExpression call,
        IReadOnlyList<TsExpression> loweredArgs,
        IrToTsBridgeContext context
    )
    {
        if (
            context.Temporaries is not { } temporaries
            || InvokedExpressionLambdaOf(call) is not (var lambda, var body)
            || loweredArgs.Count != lambda.Parameters.Count
            || loweredArgs.Any(argument => argument is TsSpreadExpression)
        )
            return null;

        var temporariesByParameter = lambda
            .Parameters.Select(parameter =>
                temporaries.TemporaryFor(
                    call,
                    () =>
                        temporaries.Claim(
                            "$" + IrToTsNamingPolicy.ToLocalName(parameter.Name),
                            context.ReservedLocals
                        ),
                    role: parameter.Name
                )
            )
            .ToList();

        var bodyContext = context with
        {
            BoundElements = lambda
                .Parameters.Zip(temporariesByParameter)
                .Aggregate(context.BoundElements, (bound, pair) =>
                    bound.SetItem(pair.First.Name, pair.Second)
                ),
        };

        var mappedBody = Map(body, bodyContext);

        if (
            CapturesABinding(
                [mappedBody],
                temporariesByParameter.Select(temporary => temporary.Name)
            )
        )
            return null;

        foreach (var (parameter, temporary) in lambda.Parameters.Zip(temporariesByParameter))
            temporaries.DeclareClaimed(temporary, LowerLambdaParameterType(parameter, context));

        return Sequence([
            .. temporariesByParameter.Select(
                (temporary, index) => new TsBinaryExpression(temporary, "=", loweredArgs[index])
            ),
            mappedBody,
        ]);
    }

    /// <summary>
    /// The lambda <paramref name="call"/> invokes, and the expression it returns,
    /// when it is an expression lambda (one <c>return</c>) taking exactly the
    /// call's arguments, with no <c>params</c> array, hidden type arguments,
    /// receiver or name of its own (a recursive <c>[Inline]</c> expansion calls
    /// itself by name).
    /// </summary>
    private static (IrLambdaExpression Lambda, IrExpression Body)? InvokedExpressionLambdaOf(
        IrCallExpression call
    ) =>
        call.Target
            is IrLambdaExpression
            {
                IsAsync: false,
                UsesThis: false,
                Name: null,
                ReifiedTypeParameters.Count: 0,
                Body: [IrReturnStatement { Value: { } body }],
            } lambda
        && call.Arguments.Count == lambda.Parameters.Count
        && !lambda.Parameters.Any(parameter => parameter.IsParams)
            ? (lambda, body)
            : null;

    /// <summary>
    /// The context of <paramref name="lambda"/>'s body: a name it declares (a
    /// parameter, a local, its own name) shadows any outer name the context binds
    /// (<see cref="IrToTsBridgeContext.BoundElements"/> — the temporary of an
    /// inlined parameter, a renamed binding), so its reads stay its own.
    /// </summary>
    private static IrToTsBridgeContext WithoutShadowedBindings(
        IrLambdaExpression lambda,
        IrToTsBridgeContext context
    ) =>
        context.BoundElements.IsEmpty
            ? context
            : context with
            {
                BoundElements = context.BoundElements.RemoveRange(NamesDeclaredIn(lambda)),
            };

    private static IEnumerable<string> NamesDeclaredIn(IrLambdaExpression lambda) =>
        lambda
            .Parameters.Select(parameter => parameter.Name)
            .Concat(lambda.Name is { } name ? [name] : [])
            .Concat(lambda.Body.SelectMany(NamesDeclaredWithin));

    private static IEnumerable<string> NamesDeclaredWithin(IrStatement statement) =>
        NamesDeclaredBy(statement)
            .Concat(IrNodeChildren.StatementsOf(statement).SelectMany(NamesDeclaredWithin));

    private static IEnumerable<string> NamesDeclaredBy(IrStatement statement) =>
        statement switch
        {
            IrVariableDeclaration declaration => [declaration.Name],
            IrForEachStatement loop => [loop.Variable],
            IrTupleDeconstruction deconstruction => deconstruction
                .Elements.Select(element => element.Name)
                .OfType<string>(),
            IrTryStatement attempt => (attempt.Catches ?? [])
                .Select(clause => clause.VariableName)
                .OfType<string>(),
            _ => [],
        };
}
