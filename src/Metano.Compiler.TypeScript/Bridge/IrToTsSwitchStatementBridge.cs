using Metano.Compiler.Analysis;
using Metano.Compiler.IR;
using Metano.Compiler.Mappings;
using Metano.Compiler.TypeScript.AST;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// Lowers a C# <c>switch</c> statement. One whose labels are all plain
/// constants maps to a JS <c>switch</c> on the subject. Every other label
/// needs a test a JS <c>switch</c> cannot express by <c>===</c> — a pattern, a
/// <c>when</c> guard, a NaN constant (<c>NaN === NaN</c> is false), or
/// <c>null</c> (which C# also matches against an absent value) — so the
/// statement becomes <c>switch (true)</c> with one boolean test per label:
/// <list type="bullet">
///   <item>Labels keep their source order, sections their fallthrough shape
///   (empty cases ahead of the shared body) and <c>default</c> its place;
///   <c>break</c> keeps leaving the switch.</item>
///   <item>The tests read the subject once each, so a subject other than
///   <c>this</c> or a local no guard reassigns is first evaluated into a
///   block-scoped <c>const $subject</c>.</item>
///   <item>A single-label section whose pattern declares locals binds them at
///   the top of its body, in a block of its own (sections share one JS
///   scope); TypeScript narrows the subject there by the case test.</item>
///   <item>An unguarded <c>case var x:</c> / <c>case _:</c> matches whatever
///   is left, so without an explicit <c>default</c> it becomes the
///   <c>default</c> case, which also lets TypeScript see the switch is
///   exhaustive.</item>
/// </list>
/// </summary>
internal static class IrToTsSwitchStatementBridge
{
    /// <summary>
    /// The block-scoped local holding a hoisted subject. <c>$</c> keeps it
    /// clear of every C# identifier; a nested switch shadows it in its own
    /// block.
    /// </summary>
    private const string SubjectLocalName = "$subject";

    public static TsStatement Map(IrSwitchStatement sw, DeclarativeMappingRegistry? bclRegistry)
    {
        var subject = IrToTsExpressionBridge.Map(sw.Expression, bclRegistry);

        if (!NeedsBooleanCaseTests(sw))
        {
            return new TsSwitchStatement(
                subject,
                [.. sw.Cases.SelectMany(section => MapConstantSection(section, bclRegistry))]
            );
        }

        if (CanReadSubjectPerTest(sw))
            return LowerToBooleanCaseTests(sw, subject, bclRegistry);

        var hoisted = new TsIdentifier(SubjectLocalName);

        return new TsBlockStatement([
            new TsVariableDeclaration(SubjectLocalName, HoistedSubjectValue(subject)),
            LowerToBooleanCaseTests(sw, hoisted, bclRegistry),
        ]);
    }

    /// <summary>
    /// An element read is asserted non-undefined (<c>xs[0]!</c>): the
    /// <c>undefined</c> that <c>noUncheckedIndexedAccess</c> adds stands for
    /// an out-of-range read, which C# would have thrown on, and would
    /// otherwise reach every designator bound from the subject.
    /// </summary>
    private static TsExpression HoistedSubjectValue(TsExpression subject) =>
        subject is TsElementAccess ? new TsPostfixUnaryExpression(subject, "!") : subject;

    private static bool NeedsBooleanCaseTests(IrSwitchStatement sw) =>
        sw.Cases.SelectMany(section => section.Labels).Any(NeedsBooleanTest);

    private static bool NeedsBooleanTest(IrCaseLabel label) =>
        label switch
        {
            IrPatternCaseLabel => true,
            IrConstantCaseLabel { Value: var value } => DefeatsStrictEquality(value),
            _ => false,
        };

    /// <summary>
    /// <c>NaN</c> is never <c>===</c> to itself, and C# <c>null</c> also
    /// matches the <c>undefined</c> of an absent value.
    /// </summary>
    private static bool DefeatsStrictEquality(IrExpression constant) =>
        IrToTsExpressionBridge.IsNaNConstant(constant)
        || constant is IrLiteral { Kind: IrLiteralKind.Null };

    /// <summary>
    /// Whether re-reading the subject in every case test observes the value
    /// C# evaluated once: <c>this</c>, or a local that no guard reassigns.
    /// </summary>
    private static bool CanReadSubjectPerTest(IrSwitchStatement sw) =>
        sw.Expression switch
        {
            IrThisExpression => true,
            IrIdentifier { Name: var name } => !IrLocalReassignmentCollector
                .CollectFromExpressions([.. sw.Cases.SelectMany(GuardsOf)])
                .Contains(name),
            _ => false,
        };

    private static IEnumerable<IrExpression?> GuardsOf(IrSwitchCase section) =>
        section.Labels.OfType<IrPatternCaseLabel>().Select(label => label.WhenClause);

    /// <summary>
    /// <c>switch (true)</c> with one test per label. Without an explicit
    /// <c>default</c>, an unguarded <c>case var x:</c> / <c>case _:</c> is
    /// promoted to the <c>default</c> case; its section still binds <c>x</c>.
    /// </summary>
    private static TsSwitchStatement LowerToBooleanCaseTests(
        IrSwitchStatement sw,
        TsExpression subject,
        DeclarativeMappingRegistry? bclRegistry
    )
    {
        var promoteCatchAllToDefault = !sw.Cases.SelectMany(c => c.Labels).Any(IsDefaultLabel);

        var cases = sw.Cases.SelectMany(section =>
            MapTestedSection(section, subject, promoteCatchAllToDefault, bclRegistry)
        );

        return new TsSwitchStatement(new TsLiteral("true"), [.. cases]);
    }

    private static bool IsDefaultLabel(IrCaseLabel label) => label is IrDefaultCaseLabel;

    /// <summary>An unguarded <c>case var x:</c> or <c>case _:</c>.</summary>
    private static bool IsCatchAllLabel(IrCaseLabel label) =>
        label is IrPatternCaseLabel { WhenClause: null, Pattern: IrVarPattern or IrDiscardPattern };

    private static IEnumerable<TsSwitchCase> MapConstantSection(
        IrSwitchCase section,
        DeclarativeMappingRegistry? bclRegistry
    ) =>
        AttachBody(
            section.Labels.Select(label =>
                label is IrConstantCaseLabel constant
                    ? IrToTsExpressionBridge.Map(constant.Value, bclRegistry)
                    : null
            ),
            IrToTsStatementBridge.MapBody(section.Body, bclRegistry)
        );

    private static IEnumerable<TsSwitchCase> MapTestedSection(
        IrSwitchCase section,
        TsExpression subject,
        bool promoteCatchAllToDefault,
        DeclarativeMappingRegistry? bclRegistry
    ) =>
        AttachBody(
            section.Labels.Select(label =>
                promoteCatchAllToDefault && IsCatchAllLabel(label)
                    ? null
                    : BuildLabelTest(label, subject, bclRegistry)
            ),
            BuildTestedSectionBody(section, subject, bclRegistry)
        );

    /// <summary>The label's case test; <c>null</c> for <c>default</c>.</summary>
    private static TsExpression? BuildLabelTest(
        IrCaseLabel label,
        TsExpression subject,
        DeclarativeMappingRegistry? bclRegistry
    ) =>
        label switch
        {
            IrConstantCaseLabel constant => IrToTsExpressionBridge.BuildCaseLabelTest(
                subject,
                constant.Value,
                bclRegistry
            ),
            IrPatternCaseLabel pattern => IrToTsExpressionBridge.BuildCasePatternTest(
                subject,
                pattern,
                bclRegistry
            ),
            _ => null,
        };

    /// <summary>
    /// The section body, preceded by its designator bindings in a block of
    /// their own. Only a single-label section binds: C# never lets the body
    /// of a multi-label section read a designator, since no one label is
    /// known to have matched.
    /// </summary>
    private static IReadOnlyList<TsStatement> BuildTestedSectionBody(
        IrSwitchCase section,
        TsExpression subject,
        DeclarativeMappingRegistry? bclRegistry
    )
    {
        var body = IrToTsStatementBridge.MapBody(section.Body, bclRegistry);

        if (section.Labels is not [IrPatternCaseLabel label])
            return body;

        var reassigned = IrLocalReassignmentCollector.CollectFromStatements(section.Body);
        var bindings = IrToTsExpressionBridge.BuildCaseLabelBindings(subject, label, reassigned);

        return bindings.Count == 0 ? body : [new TsBlockStatement([.. bindings, .. body])];
    }

    /// <summary>
    /// One JS case per label, in order: every label but the last gets an
    /// empty body and falls through to the shared one, as C# lets the labels
    /// of a section share it. A <c>null</c> test is the <c>default</c> case.
    /// </summary>
    private static IEnumerable<TsSwitchCase> AttachBody(
        IEnumerable<TsExpression?> tests,
        IReadOnlyList<TsStatement> body
    )
    {
        var labelTests = tests.ToList();

        return labelTests.Select(
            (test, i) => new TsSwitchCase(test, i == labelTests.Count - 1 ? body : [])
        );
    }
}
