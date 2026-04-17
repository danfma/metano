using Metano.Compiler.Extraction;
using Metano.Compiler.IR;

namespace Metano.TypeScript.Bridge;

/// <summary>
/// Walks an IR body and returns <c>true</c> when every node it contains is within
/// the currently-supported subset (no <see cref="IrUnsupportedExpression"/> and no
/// <see cref="IrUnsupportedStatement"/>). Used by the TypeScript pipeline to decide
/// whether a method/constructor body can be lowered through the IR bridges or
/// whether it must stay on the legacy <c>ExpressionTransformer</c> path to
/// preserve BCL mapping, <c>[Emit]</c> inlining, operator dispatch, etc.
/// </summary>
public static class IrBodyCoverageProbe
{
    public static bool IsFullyCovered(IReadOnlyList<IrStatement> body)
    {
        foreach (var s in body)
            if (!IsCoveredStatement(s))
                return false;
        return true;
    }

    private static bool IsCoveredStatement(IrStatement s) =>
        s switch
        {
            IrUnsupportedStatement => false,
            IrExpressionStatement es => IsCoveredExpression(es.Expression),
            IrReturnStatement ret => ret.Value is null || IsCoveredExpression(ret.Value),
            IrVariableDeclaration vd => vd.Initializer is null
                || IsCoveredExpression(vd.Initializer),
            IrIfStatement ifs => IsCoveredExpression(ifs.Condition)
                && IsFullyCovered(ifs.Then)
                && (ifs.Else is null || IsFullyCovered(ifs.Else)),
            IrBlockStatement block => IsFullyCovered(block.Statements),
            IrThrowStatement th => IsCoveredExpression(th.Expression),
            IrForEachStatement fe => IsCoveredExpression(fe.Collection) && IsFullyCovered(fe.Body),
            IrForStatement fs => (fs.Initializer is null || IsCoveredStatement(fs.Initializer))
                && (fs.Condition is null || IsCoveredExpression(fs.Condition))
                && (fs.Increment is null || IsCoveredExpression(fs.Increment))
                && IsFullyCovered(fs.Body),
            IrWhileStatement ws => IsCoveredExpression(ws.Condition) && IsFullyCovered(ws.Body),
            IrDoWhileStatement dw => IsCoveredExpression(dw.Condition) && IsFullyCovered(dw.Body),
            IrTryStatement ts => IsFullyCovered(ts.Body)
                && (ts.Catches is null || ts.Catches.All(c => IsFullyCovered(c.Body)))
                && (ts.Finally is null || IsFullyCovered(ts.Finally)),
            IrSwitchStatement sw => IsCoveredExpression(sw.Expression)
                && sw.Cases.All(c => c.Labels.All(IsCoveredExpression) && IsFullyCovered(c.Body)),
            IrBreakStatement or IrContinueStatement => true,
            _ => false,
        };

    private static bool IsCoveredExpression(IrExpression e) =>
        e switch
        {
            IrUnsupportedExpression => false,
            IrLiteral => true,
            IrIdentifier => true,
            IrTypeReference => true,
            IrThisExpression => true,
            IrBaseExpression => true,
            IrMemberAccess ma => IsCoveredExpression(ma.Target),
            IrElementAccess ea => IsCoveredExpression(ea.Target) && IsCoveredExpression(ea.Index),
            IrCallExpression call => IsCoveredExpression(call.Target)
                && call.Arguments.All(a => IsCoveredExpression(a.Value)),
            IrNewExpression ne => ne.Arguments.All(a => IsCoveredExpression(a.Value)),
            IrBinaryExpression bin => IsCoveredExpression(bin.Left)
                && IsCoveredExpression(bin.Right),
            IrUnaryExpression un => IsCoveredExpression(un.Operand),
            IrConditionalExpression cond => IsCoveredExpression(cond.Condition)
                && IsCoveredExpression(cond.WhenTrue)
                && IsCoveredExpression(cond.WhenFalse),
            IrAwaitExpression aw => IsCoveredExpression(aw.Expression),
            IrThrowExpression th => IsCoveredExpression(th.Expression),
            IrCastExpression cast => IsCoveredExpression(cast.Expression),
            IrLambdaExpression lambda => IsFullyCovered(lambda.Body),
            IrStringInterpolation interp => interp.Parts.All(p =>
                p is not IrInterpolationExpression expr || IsCoveredExpression(expr.Expression)
            ),
            IrIsPatternExpression isPattern => IsCoveredExpression(isPattern.Expression)
                && IsCoveredPattern(isPattern.Pattern),
            IrSwitchExpression sw => IsCoveredExpression(sw.Scrutinee)
                && sw.Arms.All(a =>
                    IsCoveredPattern(a.Pattern)
                    && (a.WhenClause is null || IsCoveredExpression(a.WhenClause))
                    && IsCoveredExpression(a.Result)
                ),
            IrWithExpression w => IsCoveredExpression(w.Source)
                && w.Assignments.All(a => IsCoveredExpression(a.Value)),
            IrTemplateExpression tpl => (tpl.Receiver is null || IsCoveredExpression(tpl.Receiver))
                && tpl.Arguments.All(IsCoveredExpression),
            IrArrayLiteral arr => arr.Elements.All(IsCoveredExpression),
            IrYieldExpression ye => ye.Value is null || IsCoveredExpression(ye.Value),
            IrOptionalChain chain => IsCoveredExpression(chain.Target),
            _ => false,
        };

    private static bool IsCoveredPattern(IrPattern pattern) =>
        pattern switch
        {
            IrConstantPattern constant => IsCoveredExpression(constant.Value),
            // Type/var/discard patterns are trivially covered at the shape level; the
            // TS bridge emits a TODO comment for the designator-binding case but the
            // probe treats them as covered because the failure mode is a visible
            // comment, not invalid code.
            IrTypePattern or IrVarPattern or IrDiscardPattern => true,
            IrPropertyPattern pp => pp.Subpatterns.All(s => IsCoveredPattern(s.Pattern)),
            // Relational / logical patterns are covered at the pattern level —
            // the bridge lowers them to `value < const` / `&&` / `||` / `!`.
            // Probe-gate them to true only for value checks; expression-scope
            // patterns via `is` work, but wider integration with the probe is
            // kept conservative the same way IrSwitchExpression is.
            IrRelationalPattern r => IsCoveredExpression(r.Value),
            IrLogicalPattern l => IsCoveredPattern(l.Left)
                && (l.Right is null || IsCoveredPattern(l.Right)),
            IrListPattern list => list.Elements.All(IsCoveredPattern)
                && (list.SlicePattern is null || IsCoveredPattern(list.SlicePattern)),
            IrPositionalPattern pos => pos.DesignatorName is null
                && pos.Elements.All(IsCoveredPattern),
            _ => false,
        };
}
