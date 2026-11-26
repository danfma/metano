using Metano.Compiler.Analysis;
using Metano.Compiler.IR;
using Metano.Compiler.TypeScript.AST;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// Lowers the catch clauses of a C# <c>try</c> statement. JS has a single,
/// untyped <c>catch</c>, so the clauses become one handler that tests the
/// caught value against each clause in source order and rethrows it when none
/// matches:
/// <code>
/// } catch (e$) {
///   const e0 = toException(e$);
///   if (e0 instanceof NotFoundException) {
///     const e = e0;
///     …
///   } else if (e0 instanceof InvalidOperationException &amp;&amp; tryFilter(() =&gt; e0.message === "x")) {
///     const e = e0;
///     …
///   } else {
///     throw e$;
///   }
/// }
/// </code>
/// <list type="bullet">
///   <item><b>Normalization.</b> When an arm observes the caught value as an
///   exception, <c>const e0 = toException(e$);</c> converts it once, so a
///   JavaScript <c>TypeError</c> is a <c>NullReferenceException</c>, an
///   <c>AbortError</c> a <c>TaskCanceledException</c>, any other <c>Error</c>
///   or thrown value an <c>Exception</c>; the type tests, the filters and the
///   variables use <c>e0</c> (see <c>toException</c> in <c>metano-runtime</c>).</item>
///   <item><b>Type test.</b> The same test an <c>is</c> pattern on the clause's
///   type lowers to (<see cref="IrToTsExpressionBridge.BuildTypeTest"/>):
///   <c>instanceof</c> the class of a transpiled exception, or the runtime
///   class of a BCL exception (<see cref="BclExceptionClasses"/>). A general
///   <c>catch</c> and <c>catch (Exception)</c> take every value and need no
///   test.</item>
///   <item><b>Filter.</b> A <c>when</c> condition joins the type test with
///   <c>&amp;&amp;</c> through <c>tryFilter(() =&gt; …)</c>, so a clause whose
///   filter is false, or throws, falls through to the next clause. A filter
///   that reads the clause's variable takes it as the arrow's parameter,
///   since the arm's <c>const</c> is not declared yet.</item>
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
/// Spec 011 FR-015. Known deviation: a filter runs after the <c>finally</c>
/// blocks between the throw and the clause, where .NET runs it before them.
/// A clause on an exception without a runtime class is reported (MS0045, see
/// <c>UnsupportedCatchClauseReporter</c>).
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

    /// <summary>
    /// The name of the value <c>toException</c> normalized the caught one to,
    /// marked again (<c>e0$</c>, …) when a clause already names it.
    /// </summary>
    private const string NormalizedValue = "e0";

    private const string ToExceptionHelper = "toException";

    private const string TryFilterHelper = "tryFilter";

    public static TsCatchClause Map(
        IReadOnlyList<IrCatchClause> clauses,
        IrToTsBridgeContext context
    )
    {
        if (clauses is [var only] && KeepsPlainCatch(only))
            return MapPlainCatch(only, context);

        if (clauses is [var bound] && BindsCaughtValueDirectly(bound))
            return MapDirectBinding(bound, context);

        var caught = FreeName(CaughtValue, clauses);
        var normalized = NeedsNormalization(clauses) ? FreeName(NormalizedValue, clauses) : null;
        var subject = normalized ?? caught;
        var arms = clauses.Select(clause => MapArm(clause, subject, caught, context));

        return new TsCatchClause(
            caught,
            [.. MapNormalization(normalized, caught), .. RenderChain(EndChain(arms, caught))]
        );
    }

    /// <summary>
    /// <c>const e0 = toException(e$);</c>: the caught value as a
    /// <c>System.Exception</c>, which every arm tests and binds, so a
    /// JavaScript <c>TypeError</c> is a <c>NullReferenceException</c> and a
    /// thrown string an <c>Exception</c>.
    /// </summary>
    private static IEnumerable<TsStatement> MapNormalization(string? normalized, string caught)
    {
        if (normalized is null)
            return [];

        return
        [
            new TsVariableDeclaration(
                normalized,
                new TsCallExpression(
                    new TsRuntimeHelperReference(ToExceptionHelper),
                    [new TsIdentifier(caught)]
                ),
                Const: true
            ),
        ];
    }

    /// <summary>
    /// Whether an arm the chain can reach observes the caught value as a
    /// <c>System.Exception</c>: a clause that names an exception type
    /// (<c>System.Exception</c> included) and tests it, filters on it or reads
    /// its variable. The rule does not look at which arms could match a
    /// <c>TypeError</c>. A general <c>catch</c>, and a <c>catch (Exception)</c>
    /// that reads nothing, observe nothing.
    /// </summary>
    private static bool NeedsNormalization(IReadOnlyList<IrCatchClause> clauses) =>
        IrCatchClause.Reachable(clauses).Any(ObservesNormalizedValue);

    private static bool ObservesNormalizedValue(IrCatchClause clause) =>
        clause.ExceptionType is not null && (!clause.IsUnconditional || ReadsVariable(clause));

    private static bool ReadsVariable(IrCatchClause clause) =>
        clause.VariableName is { } name && IrReferencedNames.AnyIn(clause.Body, [name]);

    /// <summary>
    /// Whether a lone clause that takes every exception and reads its variable
    /// binds it straight from the caught value
    /// (<c>const error = toException(e$);</c>), with no chain to test.
    /// </summary>
    private static bool BindsCaughtValueDirectly(IrCatchClause clause) =>
        clause.IsUnconditional && clause.ExceptionType is not null && ReadsVariable(clause);

    private static TsCatchClause MapDirectBinding(IrCatchClause clause, IrToTsBridgeContext context)
    {
        var caught = FreeName(CaughtValue, [clause]);

        var variable = new TsVariableDeclaration(
            IrToTsNamingPolicy.ToLocalName(clause.VariableName!),
            new TsCallExpression(
                new TsRuntimeHelperReference(ToExceptionHelper),
                [new TsIdentifier(caught)]
            ),
            Const: !Rebinds(clause, clause.VariableName!)
        );

        return new TsCatchClause(
            caught,
            [variable, .. IrToTsStatementBridge.MapBody(clause.Body, context.Rethrowing(caught))]
        );
    }

    private static TsCatchClause MapPlainCatch(IrCatchClause clause, IrToTsBridgeContext context)
    {
        if (!ReadsVariable(clause) && !Rethrows(clause.Body))
            return new TsCatchClause(null, IrToTsStatementBridge.MapBody(clause.Body, context));

        var parameter = clause.VariableName is { } name
            ? IrToTsNamingPolicy.ToLocalName(name)
            : FreeUnnamedCaughtValueName(clause);

        return new TsCatchClause(
            parameter,
            IrToTsStatementBridge.MapBody(clause.Body, context.Rethrowing(parameter))
        );
    }

    private static CatchArm MapArm(
        IrCatchClause clause,
        string subject,
        string caught,
        IrToTsBridgeContext context
    )
    {
        var condition = IrTypeCheckBuilder.AllOf([
            MapTypeTest(clause, subject, context),
            MapFilter(clause, subject, context),
        ]);

        var body = IrToTsStatementBridge.MapBody(clause.Body, context.Rethrowing(caught));

        return new CatchArm(condition, [.. MapVariable(clause, subject), .. body]);
    }

    /// <summary>
    /// The clause's type test, <c>null</c> when it has none: the clause takes
    /// every exception (<see cref="NarrowedType"/>), or its type cannot be
    /// tested at runtime, which extraction reported (MS0001, or MS0045 for an
    /// exception without a runtime class) and the arm then takes every value.
    /// </summary>
    private static TsExpression? MapTypeTest(
        IrCatchClause clause,
        string caught,
        IrToTsBridgeContext context
    ) =>
        NarrowedType(clause) is { } type && CanBeTested(type)
            ? IrToTsExpressionBridge.BuildTypeTest(
                new TsIdentifier(caught),
                type,
                staticType: null,
                context
            )
            : null;

    /// <summary>The type the clause narrows to; <c>null</c> for one that takes every exception.</summary>
    private static IrTypeRef? NarrowedType(IrCatchClause clause) =>
        clause.AcceptsEveryExceptionType ? null : clause.ExceptionType;

    private static bool CanBeTested(IrTypeRef type) =>
        IrRuntimeTypeTest.ExistsFor(type) && !IsUnmappedException(type);

    /// <summary>
    /// A BCL exception without a runtime class, whose clause extraction
    /// reported (MS0045): no test can tell it apart.
    /// </summary>
    private static bool IsUnmappedException(IrTypeRef type) =>
        type is IrNamedTypeRef named
        && BclExceptionClasses.IsBclException(named)
        && !IrToTsTypeMapper.ExceptionClasses.IsMapped(named);

    private static TsExpression? MapFilter(
        IrCatchClause clause,
        string subject,
        IrToTsBridgeContext context
    )
    {
        if (clause.Filter is not { } filter)
            return null;

        var condition = IrToTsExpressionBridge.Map(filter, context);
        var helper = new TsRuntimeHelperReference(TryFilterHelper);

        if (clause.VariableName is not { } name || !IrReferencedNames.AnyIn(filter, [name]))
            return new TsCallExpression(helper, [new TsArrowFunction([], Return(condition))]);

        var variable = new TsParameter(IrToTsNamingPolicy.ToLocalName(name), null);
        var filterWithVariable = new TsArrowFunction([variable], Return(condition));

        return new TsCallExpression(helper, [filterWithVariable, new TsIdentifier(subject)]);
    }

    private static IReadOnlyList<TsStatement> Return(TsExpression value) =>
        [new TsReturnStatement(value)];

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
        clause.IsUnconditional && !RethrowsAfterRebinding(clause) && !NeedsNormalization([clause]);

    /// <summary>
    /// Whether the clause's body rebinds the clause's variable and has a
    /// <c>throw;</c>, which must still throw the value that was caught.
    /// </summary>
    private static bool RethrowsAfterRebinding(IrCatchClause clause) =>
        clause.VariableName is { } name && Rebinds(clause, name) && Rethrows(clause.Body);

    private static bool Rebinds(IrCatchClause clause, string variable) =>
        IrLocalReassignmentCollector.CollectFromStatements(clause.Body).Contains(variable);

    /// <summary>
    /// <c>e$</c> (or <c>e0</c>), marked again (<c>e$$</c>, …) while a clause
    /// names it, so the handler's names capture none of the clauses' names.
    /// </summary>
    private static string FreeName(string name, IReadOnlyList<IrCatchClause> clauses) =>
        TsFreeName.FirstFree(name, candidate => clauses.Any(clause => Names(clause, candidate)));

    /// <summary>
    /// <c>_err</c>, marked again (<c>_err$</c>, …) while the clause's body
    /// reads or declares it, so the parameter of a lone general <c>catch</c>
    /// is neither captured by a local nor redeclared by one.
    /// </summary>
    private static string FreeUnnamedCaughtValueName(IrCatchClause clause) =>
        TsFreeName.FirstFree(UnnamedCaughtValue, candidate =>
            Names(clause, candidate) || DeclaresLocal(clause.Body, candidate)
        );

    private static bool Names(IrCatchClause clause, string name) =>
        IrReferencedNames.AnyReadOrEmittedNameIn(clause.Body, [name])
        || IsEmittedTypeName(clause.ExceptionType, name)
        || (
            clause.Filter is { } filter && IrReferencedNames.AnyReadOrEmittedNameIn(filter, [name])
        );

    /// <summary>
    /// Whether the clause's own exception type is emitted under
    /// <paramref name="name"/> (a type renamed with <c>[Name]</c>), which the
    /// arm's type test names.
    /// </summary>
    private static bool IsEmittedTypeName(IrTypeRef? type, string name) =>
        type is IrNamedTypeRef named && named.Name == name;

    /// <summary>
    /// Whether the statements declare, at their own level, a local that is
    /// emitted as <paramref name="name"/>; the block of a <c>catch</c> cannot
    /// redeclare the handler's parameter.
    /// </summary>
    private static bool DeclaresLocal(IEnumerable<IrStatement> body, string name) =>
        body.Any(statement =>
            statement switch
            {
                IrVariableDeclaration declaration => IrToTsNamingPolicy.ToLocalName(
                    declaration.Name
                ) == name,
                IrTupleDeconstruction deconstruction => deconstruction.BoundNames.Any(bound =>
                    IrToTsNamingPolicy.ToLocalName(bound) == name
                ),
                _ => false,
            }
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
