using Metano.Compiler.Analysis;
using Metano.Compiler.IR;
using Metano.Compiler.TypeScript.AST;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// The one JS <c>catch</c> block the catch clauses of a C# <c>try</c> lower to:
/// its parameter and its statements.
/// </summary>
internal sealed record TsCatchHandler(string Parameter, IReadOnlyList<TsStatement> Body);

/// <summary>
/// Lowers the catch clauses of a C# <c>try</c> statement. JS has a single,
/// untyped <c>catch</c>, so the clauses become one handler that tests the
/// caught value against each clause in source order and rethrows it when none
/// matches:
/// <code>
/// } catch (e$) {
///   if (e$ instanceof NotFoundException) {
///     const e = e$;
///     …
///   } else if (e$ instanceof Error &amp;&amp; ((e) =&gt; e.message === "x")(e$)) {
///     const e = e$;
///     …
///   } else {
///     throw e$;
///   }
/// }
/// </code>
/// <list type="bullet">
///   <item><b>Type test.</b> The same test an <c>is</c> pattern on the clause's
///   type lowers to (<see cref="IrToTsExpressionBridge.BuildTypeTest"/>):
///   <c>instanceof</c> the class of a transpiled exception, and
///   <c>instanceof Error</c> for a BCL exception type, which has no class of
///   its own. A general <c>catch</c> and <c>catch (Exception)</c> take every
///   value and need no test.</item>
///   <item><b>Filter.</b> A <c>when</c> condition joins the type test with
///   <c>&amp;&amp;</c>, so a clause whose filter is false falls through to the
///   next clause. A filter that reads the clause's variable binds it through
///   an immediately invoked arrow, since the arm's <c>const</c> is not
///   declared yet.</item>
///   <item><b>Variable.</b> Each arm declares the clause's variable from the
///   caught value: a fresh binding per catch, as C# scopes it, typed by the
///   arm's <c>instanceof</c> narrowing.</item>
///   <item><b>Rethrow.</b> <c>throw;</c> throws the handler's parameter, the
///   value that was caught, whatever the clause's variable holds by then.</item>
/// </list>
/// A lone clause that takes every exception keeps the plain
/// <c>catch (e) { … }</c> shape, its variable being the handler's parameter.
/// <para>
/// Known deviations: the BCL-type test is coarse (extraction reports MS0001
/// where that would take a later clause's exceptions, see
/// <c>UnsupportedCatchClauseReporter</c>), and an exception thrown by a filter
/// propagates, where .NET swallows it and treats the filter as false.
/// </para>
/// </summary>
internal static class IrToTsCatchChainBridge
{
    /// <summary>
    /// The handler parameter of a chain. C# identifiers cannot contain
    /// <c>$</c>, so no local of the clause bodies collides with it.
    /// </summary>
    private const string CaughtValue = "e$";

    /// <summary>The handler parameter of a lone clause that declares no variable.</summary>
    private const string UnnamedCaughtValue = "_err";

    public static TsCatchHandler Map(
        IReadOnlyList<IrCatchClause> clauses,
        IrToTsBridgeContext context
    )
    {
        if (clauses is [var only] && KeepsPlainCatch(only))
            return MapPlainCatch(only, context);

        var arms = clauses.Select(clause => MapArm(clause, context)).ToList();

        return new TsCatchHandler(CaughtValue, RenderChain(WithRethrowArm(arms)));
    }

    private static TsCatchHandler MapPlainCatch(IrCatchClause clause, IrToTsBridgeContext context)
    {
        var parameter = clause.VariableName is { } name
            ? IrToTsNamingPolicy.ToLocalName(name)
            : UnnamedCaughtValue;

        return new TsCatchHandler(
            parameter,
            IrToTsStatementBridge.MapBody(clause.Body, context.Rethrowing(parameter))
        );
    }

    private static CatchArm MapArm(IrCatchClause clause, IrToTsBridgeContext context)
    {
        var condition = IrTypeCheckBuilder.AllOf([MapTypeTest(clause), MapFilter(clause, context)]);

        var body = IrToTsStatementBridge.MapBody(clause.Body, context.Rethrowing(CaughtValue));

        return new CatchArm(condition, [.. MapVariable(clause), .. body]);
    }

    private static TsExpression? MapTypeTest(IrCatchClause clause) =>
        clause is { CatchesEveryException: false, ExceptionType: { } type } && HasTypeTest(type)
            ? IrToTsExpressionBridge.BuildTypeTest(new TsIdentifier(CaughtValue), type)
            : null;

    private static TsExpression? MapFilter(IrCatchClause clause, IrToTsBridgeContext context)
    {
        if (clause.Filter is not { } filter)
            return null;

        var condition = IrToTsExpressionBridge.Map(filter, context);

        if (clause.VariableName is not { } name || !Reads(filter, name))
            return condition;

        return TsImmediateInvocation.Of(
            [new TsImmediateBinding(IrToTsNamingPolicy.ToLocalName(name), CaughtReference())],
            [new TsReturnStatement(condition)],
            isAsync: false
        );
    }

    /// <summary>
    /// The arm's declaration of the clause's variable: <c>const e = e$;</c>,
    /// or <c>let</c> when the clause body rebinds it. A body that never reads
    /// the variable (only the filter does) declares nothing.
    /// </summary>
    private static IEnumerable<TsStatement> MapVariable(IrCatchClause clause)
    {
        if (clause.VariableName is not { } name || !clause.Body.Any(s => Reads(s, name)))
            return [];

        return
        [
            new TsVariableDeclaration(
                IrToTsNamingPolicy.ToLocalName(name),
                CaughtReference(),
                Const: !Rebinds(clause, name)
            ),
        ];
    }

    /// <summary>
    /// Whether a lone clause lowers to the plain <c>catch (e) { … }</c>: it
    /// takes every exception, and its variable can stand for the caught value
    /// — which a body that rebinds the variable and then rethrows cannot.
    /// </summary>
    private static bool KeepsPlainCatch(IrCatchClause clause) =>
        clause.HandlesEveryException
        && !(clause.VariableName is { } name && Rebinds(clause, name) && Rethrows(clause.Body));

    private static bool Rebinds(IrCatchClause clause, string variable) =>
        IrLocalReassignmentCollector.CollectFromStatements(clause.Body).Contains(variable);

    /// <summary>
    /// A chain whose last arm is conditional ends by rethrowing the caught
    /// value, as C# propagates an exception no clause handles.
    /// </summary>
    private static IReadOnlyList<CatchArm> WithRethrowArm(IReadOnlyList<CatchArm> arms) =>
        arms[^1].Condition is null
            ? arms
            : [.. arms, new CatchArm(null, [new TsThrowStatement(CaughtReference())])];

    /// <summary>
    /// The <c>if</c> / <c>else if</c> / <c>else</c> chain of the arms. A
    /// single unconditional arm is its statements alone.
    /// </summary>
    private static IReadOnlyList<TsStatement> RenderChain(IReadOnlyList<CatchArm> arms)
    {
        if (arms is [{ Condition: null } only])
            return only.Body;

        var text = string.Concat(arms.Select(RenderArm));

        var chain = new TsRawStatement(text)
        {
            EmbeddedStatements = [.. arms.SelectMany(arm => arm.Body)],
            EmbeddedExpressions = [.. arms.Select(arm => arm.Condition).OfType<TsExpression>()],
        };

        return [chain];
    }

    private static string RenderArm(CatchArm arm, int index)
    {
        var test = arm.Condition is { } condition
            ? $"if ({IrToTsStatementBridge.PrintExpression(condition)}) "
            : string.Empty;

        var head = index == 0 ? test : $" else {test}";

        return $"{head}{{\n{IrToTsStatementBridge.RenderIndentedBody(arm.Body)}}}";
    }

    private static TsExpression CaughtReference() => new TsIdentifier(CaughtValue);

    /// <summary>
    /// A type extraction reported as untestable (MS0001) has no test, so its
    /// arm takes every value.
    /// </summary>
    private static bool HasTypeTest(IrTypeRef type) =>
        IrRuntimeTypeTest.Of(type) is not IrRuntimeTypeTest.None;

    /// <summary>Whether <paramref name="expression"/> reads the local, nested lambdas included.</summary>
    private static bool Reads(IrExpression expression, string local) =>
        expression is IrIdentifier identifier && identifier.Name == local
        || IrNodeChildren.ExpressionsOf(expression).Any(child => Reads(child, local))
        || IrNodeChildren.StatementsOf(expression).Any(statement => Reads(statement, local));

    private static bool Reads(IrStatement statement, string local) =>
        IrNodeChildren.ExpressionsOf(statement).Any(child => Reads(child, local))
        || IrNodeChildren.StatementsOf(statement).Any(nested => Reads(nested, local));

    /// <summary>
    /// Whether <paramref name="statements"/> contain a <c>throw;</c>. One
    /// nested in an inner catch clause counts too: it only keeps a rare shape
    /// on the general lowering.
    /// </summary>
    private static bool Rethrows(IEnumerable<IrStatement> statements) =>
        statements.Any(statement =>
            statement is IrRethrowStatement || Rethrows(IrNodeChildren.StatementsOf(statement))
        );

    /// <summary>
    /// One arm of the chain; a <c>null</c> <paramref name="Condition"/> is the
    /// final <c>else</c>.
    /// </summary>
    private sealed record CatchArm(TsExpression? Condition, IReadOnlyList<TsStatement> Body);
}
