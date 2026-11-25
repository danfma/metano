using Metano.Compiler.Analysis;
using Metano.Compiler.IR;
using Metano.Compiler.TypeScript.AST;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// Switch-expression lowering without a closure (spec 001 FR-095, #735): a
/// conditional chain over temporaries declared at the head of the enclosing
/// statement list (<see cref="TsTemporaries"/>),
/// <c>($s = subject, test1 ? result1 : test2 ? result2 : tail)</c>.
/// </summary>
public static partial class IrToTsExpressionBridge
{
    /// <summary>The runtime helper that throws from an expression position.</summary>
    private const string ThrowErrorHelper = "throwError";

    /// <summary>
    /// Prefix of the temporary that holds an arm designator (<c>Circle c</c> →
    /// <c>$c</c>). <c>$</c> is not a valid C# identifier character, so the
    /// temporary never collides with a user name.
    /// </summary>
    private const string DesignatorTemporaryPrefix = "$";

    /// <summary>
    /// <c>($s = subject, test1 ? result1 : … : tail)</c>. The subject is
    /// evaluated once into <c>$s</c>; each arm before the first catch-all is
    /// tested in source order, its <c>when</c> guard included, and only the
    /// matched arm's result runs. The tail is the catch-all arm or a throw
    /// of the <c>SwitchExpressionException</c>.
    /// </summary>
    private static TsExpression LowerSwitchAsChain(
        IrSwitchExpression sw,
        TsTemporaries temporaries,
        IrToTsBridgeContext context
    )
    {
        var scrutinee = temporaries.Declare(ScrutineeParameterName, context.ReservedLocals);
        var subject = Map(sw.Scrutinee, context);

        var conditionalArms = sw
            .Arms.TakeWhile(arm => !arm.IsCatchAll)
            .Select(arm => LowerChainArm(arm, scrutinee, temporaries, context))
            .ToList();

        var catchAll = sw.Arms.FirstOrDefault(arm => arm.IsCatchAll);

        var tail = catchAll is null
            ? ThrowFromExpression(
                IrToTsExceptionConstructionBridge.MapNonExhaustiveSwitch(scrutinee)
            )
            : LowerCatchAllArmValue(catchAll, scrutinee, temporaries, context);

        var chain = Enumerable
            .Reverse(conditionalArms)
            .Aggregate(tail, (otherwise, arm) =>
                new TsConditionalExpression(arm.Condition, arm.Value, otherwise)
            );

        return new TsParenthesized(Comma([new TsBinaryExpression(scrutinee, "=", subject), chain]));
    }

    /// <summary>
    /// Whether a lambda in an arm's guard or result reads one of the arm's
    /// designators. The chain binds a designator to one temporary of the
    /// enclosing statement list, so a closure created in a loop header
    /// (<c>while ((f = s switch { Circle c =&gt; () =&gt; c.R, … }) != null)</c>)
    /// would see the value of the last evaluation. The arm allocates a
    /// closure anyway, so such a switch keeps the self-invoking arrow, whose
    /// bindings are fresh per evaluation.
    /// </summary>
    private static bool ClosureCapturesADesignator(IrSwitchExpression sw) =>
        sw.Arms.Any(ArmClosureCapturesADesignator);

    private static bool ArmClosureCapturesADesignator(IrSwitchArm arm)
    {
        var names = IrPatternDesignators.DeclaredBy(arm.Pattern).Select(d => d.Name).ToList();

        IrExpression[] parts = arm.WhenClause is { } guard ? [guard, arm.Result] : [arm.Result];

        return names.Count > 0
            && parts.SelectMany(LambdasIn).Any(lambda => IrReferencedNames.AnyIn(lambda, names));
    }

    /// <summary>The outermost lambdas in <paramref name="expression"/>.</summary>
    private static IEnumerable<IrExpression> LambdasIn(IrExpression expression) =>
        expression is IrLambdaExpression
            ? [expression]
            : IrNodeChildren.ExpressionsOf(expression).SelectMany(LambdasIn);

    /// <summary>
    /// The condition and value of one conditional arm: its pattern test,
    /// which assigns the designators, joined with its guard; then its result.
    /// </summary>
    private static (TsExpression Condition, TsExpression Value) LowerChainArm(
        IrSwitchArm arm,
        TsExpression scrutinee,
        TsTemporaries temporaries,
        IrToTsBridgeContext context
    )
    {
        var armContext = WithDesignatorTemporaries(arm, temporaries, context);
        var test = BuildChainArmTest(arm, scrutinee, armContext);

        var condition = arm.WhenClause is null
            ? test ?? new TsLiteral("true")
            : ContinueWith(test, Map(arm.WhenClause, armContext));

        return (condition, LowerChainArmValue(arm.Result, armContext));
    }

    /// <summary>
    /// The value of the unguarded catch-all arm: its result, after binding
    /// the designator of a <c>var x</c> pattern (<c>($x = $s, result)</c>).
    /// </summary>
    private static TsExpression LowerCatchAllArmValue(
        IrSwitchArm arm,
        TsExpression scrutinee,
        TsTemporaries temporaries,
        IrToTsBridgeContext context
    )
    {
        var armContext = WithDesignatorTemporaries(arm, temporaries, context);
        var binding = BuildChainArmTest(arm, scrutinee, armContext);

        return ContinueWith(binding, LowerChainArmValue(arm.Result, armContext));
    }

    /// <summary>
    /// The pattern test of an arm, assigning its designators as it runs when
    /// the arm reads one (see <see cref="ReadsDesignators"/>); <c>null</c>
    /// for a discard or an unread <c>var</c>, which test nothing.
    /// </summary>
    private static TsExpression? BuildChainArmTest(
        IrSwitchArm arm,
        TsExpression scrutinee,
        IrToTsBridgeContext armContext
    ) =>
        ReadsDesignators(arm)
            ? BuildBindingPatternTest(scrutinee, arm.Pattern, armContext)
            : BuildPatternTestOrNull(scrutinee, arm.Pattern, armContext);

    /// <summary>
    /// Whether the arm's guard or result names one of its designators. An
    /// arm that reads none (<c>Circle c =&gt; 1</c>) only tests its pattern
    /// and takes no temporaries.
    /// </summary>
    private static bool ReadsDesignators(IrSwitchArm arm)
    {
        var names = IrPatternDesignators.DeclaredBy(arm.Pattern).Select(d => d.Name).ToList();

        return IrReferencedNames.AnyIn(arm.Result, names)
            || (arm.WhenClause is { } guard && IrReferencedNames.AnyIn(guard, names));
    }

    /// <summary>
    /// An arm result as a value; a C# throw expression
    /// (<c>_ =&gt; throw new X()</c>) throws through the runtime helper
    /// instead of a throwing arrow.
    /// </summary>
    private static TsExpression LowerChainArmValue(
        IrExpression result,
        IrToTsBridgeContext armContext
    ) =>
        result is IrThrowExpression thrown
            ? ThrowFromExpression(Map(thrown.Expression, armContext))
            : Map(result, armContext);

    /// <summary><c>throwError(error)</c>, typed <c>never</c>.</summary>
    private static TsCallExpression ThrowFromExpression(TsExpression error) =>
        new(new TsRuntimeHelperReference(ThrowErrorHelper), [error]);

    /// <summary>
    /// <paramref name="next"/> evaluated once <paramref name="test"/> held.
    /// A test that ends by assigning a designator (<c>…, ($c = $s, true)</c>)
    /// continues inside that sequence (<c>($c = $s, next)</c>); any other
    /// test joins with <c>&amp;&amp;</c>, and a missing one leaves
    /// <paramref name="next"/> alone.
    /// </summary>
    private static TsExpression ContinueWith(TsExpression? test, TsExpression next) =>
        test switch
        {
            null => next,
            TsBinaryExpression { Operator: ",", Right: TsLiteral { Raw: "true" } } binding =>
                binding with
                {
                    Right = next,
                },
            TsBinaryExpression
            {
                Operator: "&&",
                Right: TsBinaryExpression
                {
                    Operator: ",",
                    Right: TsLiteral { Raw: "true" }
                } binding,
            } conjunction => conjunction with
            {
                Right = binding with { Right = next },
            },
            _ => new TsBinaryExpression(test, "&&", next),
        };

    /// <summary>
    /// The arm's context with each designator bound to a typed temporary of
    /// its own (<c>let $c: Circle;</c>), so the pattern test assigns it and
    /// every read or write in the guard and result names it.
    /// </summary>
    private static IrToTsBridgeContext WithDesignatorTemporaries(
        IrSwitchArm arm,
        TsTemporaries temporaries,
        IrToTsBridgeContext context
    ) =>
        !ReadsDesignators(arm)
            ? context
            : IrPatternDesignators
                .DeclaredBy(arm.Pattern)
                .Aggregate(context, (bound, designator) =>
                    bound with
                    {
                        BoundElements = bound.BoundElements.SetItem(
                            designator.Name,
                            temporaries.DeclareTyped(
                                DesignatorTemporaryPrefix
                                    + IrToTsNamingPolicy.ToLocalName(designator.Name),
                                IrToTsTypeMapper.Map(designator.Type, context.TypeOverrides),
                                context.ReservedLocals
                            )
                        ),
                    }
                );
}
