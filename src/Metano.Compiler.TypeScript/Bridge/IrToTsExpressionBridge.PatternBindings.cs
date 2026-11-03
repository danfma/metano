using Metano.Compiler.Analysis;
using Metano.Compiler.IR;
using Metano.Compiler.Mappings;
using Metano.Compiler.TypeScript.AST;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// Pattern tests that assign the pattern's designators as they run
/// (<c>o is Circle c</c> → <c>o instanceof Circle &amp;&amp; ((c = o), true)</c>).
/// Extraction declares each designator as a typed <c>let</c> ahead of the
/// statement, in the scope C# gives it; assigning it inside the test, right
/// after the check that narrows the value, keeps TypeScript's
/// definite-assignment and narrowing analyses in charge of every read.
/// </summary>
public static partial class IrToTsExpressionBridge
{
    /// <summary>
    /// The boolean test of <paramref name="pattern"/> against
    /// <paramref name="value"/>, assigning each designator once the part of
    /// the pattern that binds it matched. A designator under <c>not</c> is
    /// assigned when the negated pattern matches — the case where C# treats
    /// it as definitely assigned (<c>if (o is not Circle c) return;</c>).
    /// </summary>
    private static TsExpression BuildBindingPatternTest(
        TsExpression value,
        IrPattern pattern,
        IrToTsBridgeContext context
    ) =>
        pattern switch
        {
            IrTypePattern { Designator: { } designator } typePattern => new TsBinaryExpression(
                BuildTypeTest(value, typePattern.Type),
                "&&",
                AssignDesignator(designator, value)
            ),
            IrVarPattern varPattern => AssignDesignator(varPattern.Designator, value),
            IrPropertyPattern property => BuildBindingPropertyPatternTest(value, property, context),
            IrPositionalPattern positional => BuildBindingPositionalPatternTest(
                value,
                positional,
                context
            ),
            IrListPattern list => BuildBindingListPatternTest(value, list, context),
            IrLogicalPattern { Operator: IrLogicalOp.Not } not => new TsUnaryExpression(
                "!",
                BuildBindingPatternTest(value, not.Left, context)
            ),
            IrLogicalPattern { Operator: IrLogicalOp.And, Right: { } right } and =>
                BuildBindingConjunction(value, and.Left, right, context),
            _ => BuildPatternTest(value, pattern, context),
        };

    /// <summary>
    /// <c>left and right</c>. When <paramref name="left"/> is a <c>var</c>
    /// capture of the whole value (as extraction emits for a subject it
    /// evaluates once), <paramref name="right"/> reads the captured local
    /// instead of the value.
    /// </summary>
    private static TsExpression BuildBindingConjunction(
        TsExpression value,
        IrPattern left,
        IrPattern right,
        IrToTsBridgeContext context
    )
    {
        var rightValue = left is IrVarPattern capture
            ? new TsIdentifier(IrToTsNamingPolicy.ToLocalName(capture.Designator.Name))
            : value;

        return new TsBinaryExpression(
            BuildBindingPatternTest(value, left, context),
            "&&",
            BuildBindingPatternTest(rightValue, right, context)
        );
    }

    private static TsExpression BuildBindingPropertyPatternTest(
        TsExpression value,
        IrPropertyPattern pattern,
        IrToTsBridgeContext context
    )
    {
        var typeTest = pattern.Type is null
            ? NonNullTest(value)
            : BuildTypeTest(value, pattern.Type);

        var memberTests = pattern.Subpatterns.Select(sub =>
            BuildBindingPatternTestOrNull(
                PropertyPatternMemberAccess(value, sub),
                sub.Pattern,
                context
            )
        );

        return JoinWithAnd([
            typeTest,
            .. OwnDesignatorAssignment(pattern.Designator, value),
            .. memberTests.OfType<TsExpression>(),
        ]);
    }

    private static TsExpression BuildBindingPositionalPatternTest(
        TsExpression value,
        IrPositionalPattern pattern,
        IrToTsBridgeContext context
    )
    {
        var elementTests = pattern.Elements.Select(
            (element, i) =>
                BuildBindingPatternTestOrNull(
                    PositionalElementAccess(value, pattern, i),
                    element,
                    context
                )
        );

        var tests = new[] { PositionalTypeTest(value, pattern) }
            .Concat(OwnDesignatorAssignment(pattern.Designator, value))
            .Concat(elementTests)
            .OfType<TsExpression>()
            .ToList();

        return tests.Count == 0 ? new TsLiteral("true") : JoinWithAnd(tests);
    }

    private static TsExpression BuildBindingListPatternTest(
        TsExpression value,
        IrListPattern pattern,
        IrToTsBridgeContext context
    )
    {
        var elementTests = pattern.Elements.Select(
            (element, i) =>
                BuildBindingPatternTestOrNull(
                    ListElementAccess(value, pattern, i),
                    element,
                    context
                )
        );

        var sliceAssignment = pattern.SlicePattern is IrVarPattern rest
            ? [AssignDesignator(rest.Designator, ListSliceAccess(value, pattern))]
            : Array.Empty<TsExpression>();

        return JoinWithAnd([
            ListLengthCheck(value, pattern),
            .. elementTests.OfType<TsExpression>(),
            .. sliceAssignment,
        ]);
    }

    /// <summary>
    /// <see cref="BuildBindingPatternTest"/>, or <c>null</c> for a discard,
    /// which neither tests nor binds.
    /// </summary>
    private static TsExpression? BuildBindingPatternTestOrNull(
        TsExpression value,
        IrPattern pattern,
        IrToTsBridgeContext context
    ) => pattern is IrDiscardPattern ? null : BuildBindingPatternTest(value, pattern, context);

    private static IEnumerable<TsExpression> OwnDesignatorAssignment(
        IrPatternDesignator? designator,
        TsExpression value
    ) => designator is null ? [] : [AssignDesignator(designator, value)];

    /// <summary>
    /// <c>(name = value), true</c>: assigns the designator and lets the
    /// conjunction go on. The printer parenthesizes the sequence inside
    /// <c>&amp;&amp;</c>. An element read is asserted non-undefined
    /// (<c>xs[0]!</c>): the designator is declared with its C# type, and the
    /// <c>undefined</c> that <c>noUncheckedIndexedAccess</c> adds to an index
    /// read stands for an out-of-range read, which C# would have thrown on
    /// (a list pattern's length check rules it out).
    /// </summary>
    private static TsExpression AssignDesignator(IrPatternDesignator designator, TsExpression value)
    {
        var assigned = value is TsElementAccess ? new TsPostfixUnaryExpression(value, "!") : value;

        return new TsBinaryExpression(
            new TsBinaryExpression(
                new TsIdentifier(IrToTsNamingPolicy.ToLocalName(designator.Name)),
                "=",
                assigned
            ),
            ",",
            new TsLiteral("true")
        );
    }

    /// <summary>
    /// The test of one pattern label of a <c>switch</c> statement lowered to
    /// <c>switch (true)</c> (see <c>IrToTsSwitchStatementBridge</c>). Case
    /// tests do not carry definite assignment into the section body, so the
    /// test never binds: a <c>when</c> guard that reads the designators runs
    /// in a self-invoking arrow whose parameters take them
    /// (<c>$s instanceof Square &amp;&amp; ((s) =&gt; s.side &gt; 2)($s)</c>),
    /// and the body binds them again (<see cref="BuildCaseLabelBindings"/>).
    /// </summary>
    internal static TsExpression BuildCasePatternTest(
        TsExpression subject,
        IrPatternCaseLabel label,
        IrToTsBridgeContext context
    )
    {
        var test = BuildPatternTestOrNull(subject, label.Pattern, context);

        if (label.WhenClause is not { } guard)
            return test ?? new TsLiteral("true");

        return JoinWithGuard(test, BuildCaseGuard(subject, label.Pattern, guard, context));
    }

    private static TsExpression BuildCaseGuard(
        TsExpression subject,
        IrPattern pattern,
        IrExpression guard,
        IrToTsBridgeContext context
    )
    {
        var bindings = EnumeratePatternBindings(subject, pattern).ToList();
        var lowered = Map(guard, context);

        if (!IrReferencedNames.AnyIn(guard, bindings.Select(binding => binding.Name)))
            return lowered;

        return TsImmediateInvocation.Of(
            [
                .. bindings.Select(binding => new TsImmediateBinding(
                    IrToTsNamingPolicy.ToLocalName(binding.Name),
                    binding.Value
                )),
            ],
            [new TsReturnStatement(lowered)],
            isAsync: IrEvaluationTraits.ContainsAwait(guard)
        );
    }

    /// <summary>
    /// The declarations that bind a pattern label's designators at the top of
    /// its section body, reading the access paths the label tested; inside
    /// the section TypeScript narrows the subject by the case test. A
    /// designator the body reassigns (<paramref name="reassigned"/>) is a
    /// <c>let</c>.
    /// </summary>
    internal static IReadOnlyList<TsStatement> BuildCaseLabelBindings(
        TsExpression subject,
        IrPatternCaseLabel label,
        IReadOnlySet<string> reassigned
    ) => BuildPatternBindings(subject, label.Pattern, reassigned);
}
