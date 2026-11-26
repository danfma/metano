using Metano.Compiler.Analysis;
using Metano.Compiler.IR;
using Metano.Compiler.TypeScript.AST;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// An expression lambda invoked on the spot — an <c>[Inline]</c> expansion whose
/// arguments must evaluate once, a fused LINQ predicate calling the predicates it
/// joins — lowered with no closure (W13-1, #804): its parameters become
/// temporaries of the enclosing statement list, assigned the arguments in order
/// before the body, <c>($value = next(), ($value + $value) | 0)</c>.
/// </summary>
public static partial class IrToTsExpressionBridge
{
    /// <summary>
    /// The body of an expression lambda invoked on the spot with, as each
    /// argument, the identifier its parameter is named after — the fused
    /// predicate <c>(item) =&gt; p(item) &amp;&amp; q(item)</c> invoking
    /// <c>(item) =&gt; …</c> on its own <c>item</c> (<c>IrLinqChainFuser</c>):
    /// each parameter reads the value the argument holds, so the body is the
    /// call's value. <c>null</c> for any other call.
    /// </summary>
    private static IrExpression? InvokedLambdaBodyOverItsOwnNames(IrCallExpression call) =>
        call.BoundStaticCall is null
        && InvokedExpressionLambdaOf(call) is { } lambda
        && lambda
            .Parameters.Zip(call.Arguments)
            .All(pair =>
                pair.Second.Value is IrIdentifier argument && argument.Name == pair.First.Name
            )
            ? ((IrReturnStatement)lambda.Body[0]).Value
            : null;

    /// <summary>
    /// The invoked lambda over temporaries of the statement list, or <c>null</c>
    /// where that does not apply: outside a statement list, with a spread
    /// argument, when a function nested in the body declares a name a parameter
    /// has (the temporaries replace the parameters by name, which would reach a
    /// shadowing declaration too), or when one reads a parameter (a temporary is
    /// reassigned by every evaluation, so a closure kept from an earlier one would
    /// read the later value; the arrow's parameter is a binding per call). A
    /// parameter with a type keeps it on its temporary (<c>let $value: number;</c>),
    /// as the arrow's parameter had it.
    /// </summary>
    private static TsExpression? TryMapInvokedLambdaInline(
        IrCallExpression call,
        IReadOnlyList<TsExpression> loweredArgs,
        IrToTsBridgeContext context
    )
    {
        if (
            context.Temporaries is not { } temporaries
            || InvokedExpressionLambdaOf(call) is not { } lambda
            || loweredArgs.Count != lambda.Parameters.Count
            || loweredArgs.Any(argument => argument is TsSpreadExpression)
        )
            return null;

        var body = ((IrReturnStatement)lambda.Body[0]).Value!;
        var parameterNames = lambda.Parameters.Select(parameter => parameter.Name).ToHashSet();

        if (
            NamesDeclaredInNestedFunctions(body).Overlaps(parameterNames)
            || parameterNames.Any(name => IrReferencedNames.IsReadByAClosure(body, name))
        )
            return null;

        var temporariesByParameter = lambda
            .Parameters.Select(parameter =>
                temporaries.TemporaryFor(
                    call,
                    () => DeclareParameterTemporary(parameter, temporaries, context),
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

        return Sequence([
            .. temporariesByParameter.Select(
                (temporary, index) => new TsBinaryExpression(temporary, "=", loweredArgs[index])
            ),
            Map(body, bodyContext),
        ]);
    }

    /// <summary>
    /// The lambda <paramref name="call"/> invokes when it is an expression
    /// lambda (one <c>return</c>) taking exactly the call's arguments, with no
    /// <c>params</c> array, hidden type arguments, receiver or name of its own
    /// (a recursive <c>[Inline]</c> expansion calls itself by name).
    /// </summary>
    private static IrLambdaExpression? InvokedExpressionLambdaOf(IrCallExpression call) =>
        call.Target
            is IrLambdaExpression
            {
                IsAsync: false,
                UsesThis: false,
                Name: null,
                ReifiedTypeParameters.Count: 0,
                Body: [IrReturnStatement { Value: not null }],
            } lambda
        && call.Arguments.Count == lambda.Parameters.Count
        && !lambda.Parameters.Any(parameter => parameter.IsParams)
            ? lambda
            : null;

    private static TsIdentifier DeclareParameterTemporary(
        IrParameter parameter,
        TsTemporaries temporaries,
        IrToTsBridgeContext context
    )
    {
        var stem = "$" + IrToTsNamingPolicy.ToLocalName(parameter.Name);

        return LowerLambdaParameterType(parameter, context) is { } type
            ? temporaries.DeclareTyped(stem, type, context.ReservedLocals)
            : temporaries.Declare(stem, context.ReservedLocals);
    }

    /// <summary>
    /// The parameters and locals the functions nested in <paramref name="expression"/>
    /// declare.
    /// </summary>
    private static HashSet<string> NamesDeclaredInNestedFunctions(IrExpression expression)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);

        CollectNestedDeclarations(expression, names);

        return names;
    }

    private static void CollectNestedDeclarations(IrExpression expression, HashSet<string> names)
    {
        if (expression is IrLambdaExpression lambda)
        {
            names.UnionWith(lambda.Parameters.Select(parameter => parameter.Name));

            foreach (var statement in lambda.Body)
                CollectNestedDeclarations(statement, names);
        }

        foreach (var child in IrNodeChildren.ExpressionsOf(expression))
            CollectNestedDeclarations(child, names);
    }

    private static void CollectNestedDeclarations(IrStatement statement, HashSet<string> names)
    {
        names.UnionWith(NamesDeclaredBy(statement));

        foreach (var child in IrNodeChildren.ExpressionsOf(statement))
            CollectNestedDeclarations(child, names);

        foreach (var nested in IrNodeChildren.StatementsOf(statement))
            CollectNestedDeclarations(nested, names);
    }

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
