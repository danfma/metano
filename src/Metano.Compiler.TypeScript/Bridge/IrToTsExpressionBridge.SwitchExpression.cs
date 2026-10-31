using Metano.Compiler.Analysis;
using Metano.Compiler.IR;
using Metano.Compiler.Mappings;
using Metano.Compiler.TypeScript.AST;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// Switch-expression lowering: a nested ternary chain for the simple shape,
/// a self-invoking arrow for everything else.
/// </summary>
public static partial class IrToTsExpressionBridge
{
    /// <summary>
    /// Name of the switch-expression IIFE parameter that holds the
    /// scrutinee. <c>$</c> is not a valid C# identifier character, so it
    /// cannot collide with a user name.
    /// </summary>
    private const string ScrutineeParameterName = "$s";

    /// <summary>
    /// Lowers a C# <c>switch</c> expression. When the arm chain ends with a
    /// bare discard (<c>_ =&gt; value</c>, no when-clause), every earlier
    /// arm has a pattern that can lower to a plain boolean test, and the
    /// scrutinee is safe to read once per arm, we emit a nested ternary
    /// chain. Everything else — designator bindings, non-exhaustive matches,
    /// a scrutinee with side effects — lowers to a self-invoking arrow (see
    /// <see cref="LowerSwitchAsIife"/>), which evaluates the scrutinee once.
    /// </summary>
    private static TsExpression MapSwitchExpression(
        IrSwitchExpression sw,
        DeclarativeMappingRegistry? bclRegistry
    ) =>
        CanLowerSwitchAsTernary(sw)
            ? LowerSwitchAsTernary(sw, bclRegistry)
            : LowerSwitchAsIife(sw, bclRegistry);

    private static bool CanLowerSwitchAsTernary(IrSwitchExpression sw) =>
        sw.Arms.Count > 0
        && sw.Arms[^1] is { Pattern: IrDiscardPattern, WhenClause: null }
        && sw.Arms.Take(sw.Arms.Count - 1).All(arm => CanLowerPatternAsBooleanTest(arm.Pattern))
        && IsStableReference(sw.Scrutinee);

    private static TsExpression LowerSwitchAsTernary(
        IrSwitchExpression sw,
        DeclarativeMappingRegistry? bclRegistry
    )
    {
        var scrutinee = Map(sw.Scrutinee, bclRegistry);
        var fallback = Map(sw.Arms[^1].Result, bclRegistry);

        return sw
            .Arms.Take(sw.Arms.Count - 1)
            .Reverse()
            .Aggregate(
                fallback,
                (otherwise, arm) =>
                    new TsConditionalExpression(
                        BuildArmCondition(scrutinee, arm, bclRegistry),
                        Map(arm.Result, bclRegistry),
                        otherwise
                    )
            );
    }

    /// <summary>
    /// The full condition of a binding-free arm: its pattern test joined
    /// with its guard. A guarded discard arm tests only the guard.
    /// </summary>
    private static TsExpression BuildArmCondition(
        TsExpression scrutinee,
        IrSwitchArm arm,
        DeclarativeMappingRegistry? bclRegistry
    )
    {
        var test = BuildPatternTestOrNull(scrutinee, arm.Pattern, bclRegistry);

        if (arm.WhenClause is null)
            return test ?? new TsLiteral("true");

        return JoinWithGuard(test, Map(arm.WhenClause, bclRegistry));
    }

    private static bool CanLowerPatternAsBooleanTest(IrPattern pattern) =>
        pattern switch
        {
            IrConstantPattern or IrDiscardPattern or IrRelationalPattern => true,
            IrTypePattern t => t.DesignatorName is null,
            IrPropertyPattern p => p.DesignatorName is null
                && p.Subpatterns.All(s => CanLowerPatternAsBooleanTest(s.Pattern)),
            IrLogicalPattern { Operator: IrLogicalOp.Not } n => CanLowerPatternAsBooleanTest(
                n.Left
            ),
            IrLogicalPattern l => CanLowerPatternAsBooleanTest(l.Left)
                && l.Right is not null
                && CanLowerPatternAsBooleanTest(l.Right),
            _ => false,
        };

    /// <summary>
    /// Lowers a switch expression to a self-invoking arrow whose untyped
    /// parameter <c>$s</c> holds the scrutinee (see
    /// <see cref="BuildSwitchIife"/>). Each arm before the first catch-all (an unguarded
    /// <c>_</c> / <c>var x</c>) becomes an <c>if</c> on its pattern test whose
    /// body binds the pattern's designators and then returns (or throws) the
    /// arm result. The catch-all, when present, runs unconditionally last —
    /// later arms are unreachable — and otherwise a "non-exhaustive" throw
    /// mirrors C#'s runtime failure.
    /// </summary>
    private static TsExpression LowerSwitchAsIife(
        IrSwitchExpression sw,
        DeclarativeMappingRegistry? bclRegistry
    )
    {
        var scrutinee = new TsIdentifier(ScrutineeParameterName);
        var conditionalArms = sw.Arms.TakeWhile(arm => !IsCatchAllArm(arm));
        var catchAll = sw.Arms.FirstOrDefault(IsCatchAllArm);

        var conditionalStatements = conditionalArms.Select(arm =>
            LowerConditionalSwitchArm(arm, scrutinee, bclRegistry)
        );

        var tail = catchAll is null
            ? [BuildNonExhaustiveSwitchThrow()]
            : LowerSwitchArmBody(catchAll, scrutinee, bclRegistry);

        return BuildSwitchIife(sw, [.. conditionalStatements, .. tail], bclRegistry);
    }

    /// <summary>
    /// <c>(($s) =&gt; { … })(scrutinee)</c> (see
    /// <see cref="TsImmediateInvocation.Of"/>); the arms' <c>instanceof</c>
    /// tests narrow the untyped parameter for the designators.
    /// </summary>
    private static TsExpression BuildSwitchIife(
        IrSwitchExpression sw,
        IReadOnlyList<TsStatement> body,
        DeclarativeMappingRegistry? bclRegistry
    ) =>
        TsImmediateInvocation.Of(
            [new TsImmediateBinding(ScrutineeParameterName, Map(sw.Scrutinee, bclRegistry))],
            body,
            isAsync: sw.Arms.Any(ArmAwaits)
        );

    private static bool ArmAwaits(IrSwitchArm arm) =>
        (arm.WhenClause is not null && IrEvaluationTraits.ContainsAwait(arm.WhenClause))
        || IrEvaluationTraits.ContainsAwait(arm.Result);

    private static TsThrowStatement BuildNonExhaustiveSwitchThrow() =>
        new(
            new TsNewExpression(
                new TsIdentifier("Error"),
                [new TsStringLiteral("Non-exhaustive switch expression")]
            )
        );

    private static bool IsCatchAllArm(IrSwitchArm arm) =>
        arm.WhenClause is null && IsAlwaysMatchingPattern(arm.Pattern);

    /// <summary>
    /// <c>if (test) { bindings; outcome }</c>. Without designators the
    /// <c>when</c> guard joins the test; with designators it nests inside
    /// the block so it can read them, and a failed guard falls through to
    /// the next arm. An always-matching pattern reaching here carries a
    /// guard (see <see cref="LowerGuardedCatchAllArm"/>).
    /// </summary>
    private static TsStatement LowerConditionalSwitchArm(
        IrSwitchArm arm,
        TsExpression scrutinee,
        DeclarativeMappingRegistry? bclRegistry
    )
    {
        if (IsAlwaysMatchingPattern(arm.Pattern) && arm.WhenClause is { } guard)
            return LowerGuardedCatchAllArm(arm, guard, scrutinee, bclRegistry);

        if (HasDesignators(arm.Pattern) || arm.WhenClause is null)
        {
            return new TsIfStatement(
                BuildPatternTest(scrutinee, arm.Pattern, bclRegistry),
                LowerSwitchArmBody(arm, scrutinee, bclRegistry)
            );
        }

        return new TsIfStatement(
            BuildArmCondition(scrutinee, arm, bclRegistry),
            [LowerSwitchArmOutcome(arm.Result, bclRegistry)]
        );
    }

    /// <summary>
    /// <c>var x when guard =&gt; ...</c> / <c>_ when guard =&gt; ...</c>:
    /// the guard is the only test. A <c>var</c> binding the guard reads is
    /// declared in a block of its own so it stays arm-local.
    /// </summary>
    private static TsStatement LowerGuardedCatchAllArm(
        IrSwitchArm arm,
        IrExpression guard,
        TsExpression scrutinee,
        DeclarativeMappingRegistry? bclRegistry
    )
    {
        var bindings = BuildArmBindings(arm, scrutinee);

        var guarded = new TsIfStatement(
            Map(guard, bclRegistry),
            [LowerSwitchArmOutcome(arm.Result, bclRegistry)]
        );

        return bindings.Count == 0 ? guarded : new TsBlockStatement([.. bindings, guarded]);
    }

    /// <summary>
    /// The statements that run once an arm's pattern matched: the
    /// designator bindings, then the outcome — guarded by the <c>when</c>
    /// clause when present, so the guard can read the bindings.
    /// </summary>
    private static IReadOnlyList<TsStatement> LowerSwitchArmBody(
        IrSwitchArm arm,
        TsExpression scrutinee,
        DeclarativeMappingRegistry? bclRegistry
    )
    {
        var outcome = LowerSwitchArmOutcome(arm.Result, bclRegistry);

        var guardedOutcome = arm.WhenClause is null
            ? outcome
            : new TsIfStatement(Map(arm.WhenClause, bclRegistry), [outcome]);

        return [.. BuildArmBindings(arm, scrutinee), guardedOutcome];
    }

    /// <summary>
    /// The arm's designator declarations; a designator the guard or result
    /// reassigns (<c>Circle c =&gt; (c = Fallback).Radius</c>) is a
    /// <c>let</c>.
    /// </summary>
    private static IReadOnlyList<TsStatement> BuildArmBindings(
        IrSwitchArm arm,
        TsExpression scrutinee
    )
    {
        var reassigned = IrLocalReassignmentCollector.CollectFromExpressions([
            arm.WhenClause,
            arm.Result,
        ]);

        return BuildPatternBindings(scrutinee, arm.Pattern, reassigned);
    }

    /// <summary>
    /// An arm result is a value to return, except a C# throw expression
    /// (<c>_ =&gt; throw new X()</c>), which lowers to a plain
    /// <c>throw</c> statement instead of a throwing IIFE.
    /// </summary>
    private static TsStatement LowerSwitchArmOutcome(
        IrExpression result,
        DeclarativeMappingRegistry? bclRegistry
    ) =>
        result is IrThrowExpression thrown
            ? new TsThrowStatement(Map(thrown.Expression, bclRegistry))
            : new TsReturnStatement(Map(result, bclRegistry));

    /// <summary>
    /// Shape-based check that reading the expression again yields the same
    /// value without new side effects: identifiers, <c>this</c>, literals,
    /// member-access chains, and operators over those. A member access may
    /// still run a property getter, which is assumed to be side-effect free.
    /// </summary>
    private static bool IsStableReference(IrExpression expression) =>
        expression switch
        {
            IrIdentifier or IrThisExpression or IrLiteral => true,
            IrMemberAccess access => IsStableReference(access.Target),
            IrBinaryExpression binary when !binary.Operator.IsAssignment() => IsStableReference(
                binary.Left
            ) && IsStableReference(binary.Right),
            IrUnaryExpression
            {
                Operator: not (IrUnaryOp.Increment or IrUnaryOp.Decrement)
            } unary => IsStableReference(unary.Operand),
            _ => false,
        };
}
