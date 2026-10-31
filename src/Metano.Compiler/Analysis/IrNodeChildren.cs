using Metano.Compiler.IR;

namespace Metano.Compiler.Analysis;

/// <summary>
/// The direct children of IR expressions and statements, so analyses can walk
/// a body without each re-implementing the per-node switch. A lambda's body
/// is a statement list, reported by <see cref="StatementsOf(IrExpression)"/>
/// and never mixed into <see cref="ExpressionsOf(IrExpression)"/>, which lets
/// an analysis decide whether it crosses function boundaries.
/// </summary>
public static class IrNodeChildren
{
    /// <summary>The direct sub-expressions of <paramref name="expression"/>.</summary>
    public static IEnumerable<IrExpression> ExpressionsOf(IrExpression expression) =>
        expression switch
        {
            IrMemberAccess access => [access.Target],
            IrElementAccess element => [element.Target, element.Index],
            IrSliceExpression slice => new[]
            {
                slice.Target,
                slice.Start,
                slice.End,
            }.OfType<IrExpression>(),
            IrOptionalChain chain => [chain.Target],
            IrCallExpression call => [call.Target, .. call.Arguments.Select(a => a.Value)],
            IrNewExpression creation =>
            [
                .. creation.Arguments.Select(a => a.Value),
                .. (creation.Initializers ?? []).Select(i => i.Value),
            ],
            IrBinaryExpression binary => [binary.Left, binary.Right],
            IrUnaryExpression unary => [unary.Operand],
            IrConditionalExpression conditional =>
            [
                conditional.Condition,
                conditional.WhenTrue,
                conditional.WhenFalse,
            ],
            IrAwaitExpression awaited => [awaited.Expression],
            IrYieldExpression { Value: { } yielded } => [yielded],
            IrArrayLiteral array => array.Elements,
            IrObjectLiteral obj => obj.Properties.Select(p => p.Value),
            IrSpreadExpression spread => [spread.Expression],
            IrStringInterpolation interpolation => interpolation
                .Parts.OfType<IrInterpolationExpression>()
                .Select(p => p.Expression),
            IrIsPatternExpression isPattern =>
            [
                isPattern.Expression,
                .. ExpressionsOf(isPattern.Pattern),
            ],
            IrSwitchExpression sw => [sw.Scrutinee, .. sw.Arms.SelectMany(ExpressionsOf)],
            IrWithExpression with => [with.Source, .. with.Assignments.Select(a => a.Value)],
            IrThrowExpression thrown => [thrown.Expression],
            IrLetExpression let => [let.Value, let.Body],
            IrRuntimeHelperCall helper => helper.Arguments,
            IrTemplateExpression template =>
            [
                .. (template.Receiver is null ? [] : new[] { template.Receiver }),
                .. template.Arguments,
            ],
            IrCastExpression cast => [cast.Expression],
            IrTypeCheck check => [check.Expression],
            IrLinqChain linq => [linq.Source, .. linq.Stages.SelectMany(s => s.Arguments)],
            _ => [],
        };

    /// <summary>The statement body an expression owns (a lambda's body).</summary>
    public static IEnumerable<IrStatement> StatementsOf(IrExpression expression) =>
        expression is IrLambdaExpression lambda ? lambda.Body : [];

    /// <summary>The expressions a statement evaluates directly.</summary>
    public static IEnumerable<IrExpression> ExpressionsOf(IrStatement statement) =>
        statement switch
        {
            IrExpressionStatement es => [es.Expression],
            IrReturnStatement { Value: { } value } => [value],
            IrVariableDeclaration { Initializer: { } initializer } => [initializer],
            IrTupleDeconstruction td => [td.Initializer],
            IrIfStatement ifs => [ifs.Condition],
            IrThrowStatement th => [th.Expression],
            IrForEachStatement fe => [fe.Collection],
            IrForStatement fs => new[] { fs.Condition, fs.Increment }.OfType<IrExpression>(),
            IrWhileStatement ws => [ws.Condition],
            IrDoWhileStatement dw => [dw.Condition],
            IrSwitchStatement sw => [sw.Expression, .. sw.Cases.SelectMany(c => c.Labels)],
            _ => [],
        };

    /// <summary>The statements nested directly inside a statement.</summary>
    public static IEnumerable<IrStatement> StatementsOf(IrStatement statement) =>
        statement switch
        {
            IrIfStatement ifs => [.. ifs.Then, .. ifs.Else ?? []],
            IrBlockStatement block => block.Statements,
            IrForEachStatement fe => fe.Body,
            IrForStatement fs =>
            [
                .. fs.Initializer is null ? [] : new[] { fs.Initializer },
                .. fs.Body,
            ],
            IrWhileStatement ws => ws.Body,
            IrDoWhileStatement dw => dw.Body,
            IrTryStatement ts =>
            [
                .. ts.Body,
                .. (ts.Catches ?? []).SelectMany(c => c.Body),
                .. ts.Finally ?? [],
            ],
            IrSwitchStatement sw => sw.Cases.SelectMany(c => c.Body),
            _ => [],
        };

    private static IEnumerable<IrExpression> ExpressionsOf(IrSwitchArm arm) =>
        [
            .. ExpressionsOf(arm.Pattern),
            .. arm.WhenClause is null ? [] : new[] { arm.WhenClause },
            arm.Result,
        ];

    private static IEnumerable<IrExpression> ExpressionsOf(IrPattern pattern) =>
        pattern switch
        {
            IrConstantPattern constant => [constant.Value],
            IrRelationalPattern relational => [relational.Value],
            IrPropertyPattern property => property.Subpatterns.SelectMany(s =>
                ExpressionsOf(s.Pattern)
            ),
            IrPositionalPattern positional => positional.Elements.SelectMany(ExpressionsOf),
            IrListPattern list =>
            [
                .. list.Elements.SelectMany(ExpressionsOf),
                .. list.SlicePattern is null ? [] : ExpressionsOf(list.SlicePattern),
            ],
            IrLogicalPattern logical =>
            [
                .. ExpressionsOf(logical.Left),
                .. logical.Right is null ? [] : ExpressionsOf(logical.Right),
            ],
            _ => [],
        };
}
