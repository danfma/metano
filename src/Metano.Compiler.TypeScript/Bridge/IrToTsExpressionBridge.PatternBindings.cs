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
                BuildTypeTest(value, typePattern.Type, typePattern.InputType, context),
                "&&",
                AssignDesignator(
                    designator,
                    UnboxedBindingValue(value, typePattern, context),
                    context
                )
            ),
            IrVarPattern varPattern => AssignDesignator(varPattern.Designator, value, context),
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
            ? DesignatorReference(capture.Designator, context)
            : ConjunctionRightValue(value, left, context.RuntimeTypes.BoxingEnabled);

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
        var typeTest = PropertyPatternTypeTest(value, pattern, context);

        var memberTests = pattern.Subpatterns.Select(sub =>
            BuildBindingPatternTestOrNull(
                PropertyPatternMemberAccess(value, sub, context),
                sub.Pattern,
                context
            )
        );

        return JoinWithAnd([
            typeTest,
            .. OwnDesignatorAssignment(
                pattern.Designator,
                UnboxedBindingValue(value, pattern.Type, pattern.InputType, context),
                context
            ),
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

        var tests = new[] { PositionalTypeTest(value, pattern, context) }
            .Concat(OwnDesignatorAssignment(pattern.Designator, value, context))
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
            ? [AssignDesignator(rest.Designator, ListSliceAccess(value, pattern), context)]
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
        TsExpression value,
        IrToTsBridgeContext context
    ) => designator is null ? [] : [AssignDesignator(designator, value, context)];

    /// <summary>
    /// <c>(name = value), true</c>: assigns the designator and lets the
    /// conjunction go on. The printer parenthesizes the sequence inside
    /// <c>&amp;&amp;</c>. An element read is asserted non-undefined
    /// (<c>xs[0]!</c>): the designator is declared with its C# type, and the
    /// <c>undefined</c> that <c>noUncheckedIndexedAccess</c> adds to an index
    /// read stands for an out-of-range read, which C# would have thrown on
    /// (a list pattern's length check rules it out).
    /// </summary>
    private static TsExpression AssignDesignator(
        IrPatternDesignator designator,
        TsExpression value,
        IrToTsBridgeContext context
    )
    {
        var assigned = value is TsElementAccess ? new TsPostfixUnaryExpression(value, "!") : value;

        return new TsBinaryExpression(
            new TsBinaryExpression(DesignatorReference(designator, context), "=", assigned),
            ",",
            new TsLiteral("true")
        );
    }

    /// <summary>
    /// The local a designator lowers to: the temporary a switch-expression
    /// arm bound it to (see <see cref="WithDesignatorTemporaries"/>), or the
    /// local of its own name that extraction declared.
    /// </summary>
    private static TsExpression DesignatorReference(
        IrPatternDesignator designator,
        IrToTsBridgeContext context
    ) =>
        context.BoundElements.TryGetValue(designator.Name, out var temporary)
            ? temporary
            : new TsIdentifier(IrToTsNamingPolicy.ToLocalName(designator.Name));

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
        var bindings = EnumeratePatternBindings(subject, pattern, context).ToList();

        if (!IrReferencedNames.AnyIn(guard, bindings.Select(binding => binding.Name)))
            return Map(guard, context);

        if (
            context.Temporaries is { } temporaries
            && !bindings.Any(binding => IrReferencedNames.IsReadByAClosure(guard, binding.Name))
        )
            return BindCaseGuardInline(bindings, guard, temporaries, context);

        // Outside a statement list, or where a closure in the guard reads a
        // designator, which then needs a scope of its own per evaluation: the
        // designators are the parameters of an arrow invoked on the spot.
        var lowered = Map(guard, context);

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
    /// A case guard that reads designators, over temporaries of the enclosing
    /// statement list that hold them while it runs:
    /// <c>$s instanceof Square &amp;&amp; ($s1 = $s, $s1.side &gt; 2)</c>. Each
    /// temporary is named after its designator with a <c>$</c>, so it never
    /// collides with the local the section body declares for it.
    /// </summary>
    private static TsExpression BindCaseGuardInline(
        IReadOnlyList<PatternBinding> bindings,
        IrExpression guard,
        TsTemporaries temporaries,
        IrToTsBridgeContext context
    )
    {
        var read = bindings
            .Where(binding => IrReferencedNames.AnyIn(guard, [binding.Name]))
            .ToList();

        var temporariesByName = read.ToDictionary(
            binding => binding.Name,
            binding =>
                temporaries.TemporaryFor(
                    guard,
                    () =>
                        temporaries.Declare(
                            "$" + IrToTsNamingPolicy.ToLocalName(binding.Name),
                            context.ReservedLocals
                        ),
                    role: binding.Name
                )
        );

        var guardContext = context with
        {
            BoundElements = temporariesByName.Aggregate(context.BoundElements, (bound, entry) =>
                bound.SetItem(entry.Key, entry.Value)
            ),
        };

        return Sequence([
            .. read.Select(binding => new TsBinaryExpression(
                temporariesByName[binding.Name],
                "=",
                binding.Value
            )),
            Map(guard, guardContext),
        ]);
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
        IReadOnlySet<string> reassigned,
        IrToTsBridgeContext context
    ) => BuildPatternBindings(subject, label.Pattern, reassigned, context);
}
