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
///   <item><b>Variable.</b> An arm whose body reads the clause's variable
///   declares it from the caught value: a fresh binding per catch, as C#
///   scopes it, typed by the arm's <c>instanceof</c> narrowing.</item>
///   <item><b>Rethrow.</b> <c>throw;</c> throws the handler's parameter, the
///   value that was caught, whatever the clause's variable holds by then.</item>
/// </list>
/// The first clause that takes every value (no type test and no filter) ends
/// the chain: C# never tries the clauses after it. A lone clause that takes
/// every exception keeps the plain <c>catch (e) { … }</c> shape, its variable
/// being the handler's parameter.
/// <para>
/// An interim, partial delivery of spec 011 FR-015. Known deviations:
/// generated code constructs every BCL exception as a plain <c>Error</c>, so
/// the BCL-type test is coarse (#651; extraction reports MS0001 where that
/// would take a later clause's exceptions, see
/// <c>UnsupportedCatchClauseReporter</c>); an exception thrown by a filter
/// propagates, where .NET swallows it and treats the filter as false; and a
/// filter runs after the <c>finally</c> blocks between the throw and the
/// clause, where .NET runs it before them.
/// </para>
/// </summary>
internal static class IrToTsCatchChainBridge
{
    /// <summary>
    /// The handler parameter of a chain, marked again (<c>e$$</c>, …) when a
    /// clause already names it. C# identifiers cannot contain <c>$</c>, but
    /// the <c>[Name]</c> override of a member can; a type renamed to it is
    /// not looked for.
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

        var caught = FreeCaughtValueName(clauses);
        var arms = clauses.Select(clause => MapArm(clause, caught, context));

        return new TsCatchHandler(caught, RenderChain(EndChain(arms, caught)));
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

    private static CatchArm MapArm(IrCatchClause clause, string caught, IrToTsBridgeContext context)
    {
        var condition = IrTypeCheckBuilder.AllOf([
            MapTypeTest(clause, caught),
            MapFilter(clause, caught, context),
        ]);

        var body = IrToTsStatementBridge.MapBody(clause.Body, context.Rethrowing(caught));

        return new CatchArm(condition, [.. MapVariable(clause, caught), .. body]);
    }

    /// <summary>
    /// The clause's type test; <c>null</c> for a clause that takes every
    /// exception, and for a type extraction reported as untestable (MS0001).
    /// </summary>
    private static TsExpression? MapTypeTest(IrCatchClause clause, string caught) =>
        clause is { CatchesEveryException: false, ExceptionType: { } type }
        && IrRuntimeTypeTest.ExistsFor(type)
            ? IrToTsExpressionBridge.BuildTypeTest(new TsIdentifier(caught), type)
            : null;

    private static TsExpression? MapFilter(
        IrCatchClause clause,
        string caught,
        IrToTsBridgeContext context
    )
    {
        if (clause.Filter is not { } filter)
            return null;

        var condition = IrToTsExpressionBridge.Map(filter, context);

        if (clause.VariableName is not { } name || !IrReferencedNames.AnyIn(filter, [name]))
            return condition;

        return TsImmediateInvocation.Of(
            [
                new TsImmediateBinding(
                    IrToTsNamingPolicy.ToLocalName(name),
                    new TsIdentifier(caught)
                ),
            ],
            [new TsReturnStatement(condition)],
            isAsync: false
        );
    }

    /// <summary>
    /// The arm's declaration of the clause's variable: <c>const e = e$;</c>,
    /// or <c>let</c> when the clause body rebinds it. A body that never reads
    /// the variable (only the filter does) declares nothing.
    /// </summary>
    private static IEnumerable<TsStatement> MapVariable(IrCatchClause clause, string caught)
    {
        if (clause.VariableName is not { } name || !IrReferencedNames.AnyIn(clause.Body, [name]))
            return [];

        return
        [
            new TsVariableDeclaration(
                IrToTsNamingPolicy.ToLocalName(name),
                new TsIdentifier(caught),
                Const: !Rebinds(clause, name)
            ),
        ];
    }

    /// <summary>
    /// Whether a lone clause lowers to the plain <c>catch (e) { … }</c>: it
    /// takes every exception, and its variable can stand for the caught value.
    /// </summary>
    private static bool KeepsPlainCatch(IrCatchClause clause) =>
        clause.HandlesEveryException && !RethrowsAfterRebinding(clause);

    /// <summary>
    /// Whether the clause's body rebinds the clause's variable and has a
    /// <c>throw;</c>, which must still throw the value that was caught.
    /// </summary>
    private static bool RethrowsAfterRebinding(IrCatchClause clause) =>
        clause.VariableName is { } name && Rebinds(clause, name) && Rethrows(clause.Body);

    private static bool Rebinds(IrCatchClause clause, string variable) =>
        IrLocalReassignmentCollector.CollectFromStatements(clause.Body).Contains(variable);

    /// <summary>
    /// <c>e$</c>, marked again (<c>e$$</c>, …) while a clause names it, so
    /// the handler's parameter captures none of the clauses' names.
    /// </summary>
    private static string FreeCaughtValueName(IReadOnlyList<IrCatchClause> clauses) =>
        TsFreeName.FirstFree(
            CaughtValue,
            candidate => clauses.Any(clause => Names(clause, candidate))
        );

    private static bool Names(IrCatchClause clause, string name) =>
        IrReferencedNames.AnyReadOrEmittedMemberIn(clause.Body, [name])
        || (
            clause.Filter is { } filter
            && IrReferencedNames.AnyReadOrEmittedMemberIn(filter, [name])
        );

    /// <summary>
    /// The arms C# can reach: the first unconditional arm takes every value,
    /// so it ends the chain and the arms after it are dropped. A chain with no
    /// such arm ends by rethrowing the caught value, as C# propagates an
    /// exception no clause handles.
    /// </summary>
    private static IReadOnlyList<CatchArm> EndChain(IEnumerable<CatchArm> arms, string caught)
    {
        var chain = new List<CatchArm>();

        foreach (var arm in arms)
        {
            chain.Add(arm);

            if (arm.IsUnconditional)
                return chain;
        }

        chain.Add(new CatchArm(null, [new TsThrowStatement(new TsIdentifier(caught))]));

        return chain;
    }

    /// <summary>
    /// The <c>if</c> / <c>else if</c> / <c>else</c> chain of the arms, the
    /// last of which is the only unconditional one (see
    /// <see cref="EndChain"/>). A single arm is its statements alone.
    /// </summary>
    private static IReadOnlyList<TsStatement> RenderChain(IReadOnlyList<CatchArm> arms)
    {
        if (arms is [var only])
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
    /// One arm of the chain. Without a <paramref name="Condition"/> the arm
    /// takes every value: a clause with neither a type test nor a filter, or
    /// the rethrow that ends a chain of conditional arms.
    /// </summary>
    private sealed record CatchArm(TsExpression? Condition, IReadOnlyList<TsStatement> Body)
    {
        public bool IsUnconditional => Condition is null;
    }
}
