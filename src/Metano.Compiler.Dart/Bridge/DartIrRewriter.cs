using Metano.Compiler.IR;
using Metano.Compiler.Mappings;

namespace Metano.Dart.Bridge;

/// <summary>
/// Walks an IR statement/expression tree and rewrites every call site or
/// member access whose origin matches a declarative Dart mapping. The walker
/// is shape-preserving — it copies records via <c>with</c> only when something
/// downstream changed, so unmapped subtrees flow through with reference equality
/// intact.
/// <para>
/// Lives between IR extraction and the bridge-to-DartAST conversion: the bridge
/// stores the rewritten body in <see cref="AST.DartMethodSignature"/>,
/// <see cref="AST.DartGetter"/>, etc., and the printer renders the rewritten IR
/// without needing any mapper context of its own.
/// </para>
/// </summary>
public sealed class DartIrRewriter
{
    private readonly DeclarativeMappingRegistry _registry;

    public DartIrRewriter(DeclarativeMappingRegistry registry)
    {
        _registry = registry;
    }

    public IReadOnlyList<IrStatement>? Rewrite(IReadOnlyList<IrStatement>? body)
    {
        if (body is null)
            return null;
        return body.Select(RewriteStatement).ToList();
    }

    private IrStatement RewriteStatement(IrStatement stmt) =>
        stmt switch
        {
            IrReturnStatement r => r.Value is null
                ? r
                : r with
                {
                    Value = RewriteExpression(r.Value),
                },
            IrExpressionStatement e => e with { Expression = RewriteExpression(e.Expression) },
            IrVariableDeclaration v => v.Initializer is null
                ? v
                : v with
                {
                    Initializer = RewriteExpression(v.Initializer),
                },
            IrIfStatement i => i with
            {
                Condition = RewriteExpression(i.Condition),
                Then = i.Then.Select(RewriteStatement).ToList(),
                Else = i.Else?.Select(RewriteStatement).ToList(),
            },
            IrThrowStatement t => t with { Expression = RewriteExpression(t.Expression) },
            IrBlockStatement b => b with
            {
                Statements = b.Statements.Select(RewriteStatement).ToList(),
            },
            IrForEachStatement f => f with
            {
                Collection = RewriteExpression(f.Collection),
                Body = f.Body.Select(RewriteStatement).ToList(),
            },
            IrForStatement f => f with
            {
                Initializer = f.Initializer is null ? null : RewriteStatement(f.Initializer),
                Condition = f.Condition is null ? null : RewriteExpression(f.Condition),
                Increment = f.Increment is null ? null : RewriteExpression(f.Increment),
                Body = f.Body.Select(RewriteStatement).ToList(),
            },
            IrWhileStatement w => w with
            {
                Condition = RewriteExpression(w.Condition),
                Body = w.Body.Select(RewriteStatement).ToList(),
            },
            IrDoWhileStatement d => d with
            {
                Body = d.Body.Select(RewriteStatement).ToList(),
                Condition = RewriteExpression(d.Condition),
            },
            IrTryStatement t => t with
            {
                Body = t.Body.Select(RewriteStatement).ToList(),
                Catches = t.Catches?.Select(RewriteCatch).ToList(),
                Finally = t.Finally?.Select(RewriteStatement).ToList(),
            },
            IrSwitchStatement s => s with
            {
                Expression = RewriteExpression(s.Expression),
                Cases = s.Cases.Select(RewriteSwitchCase).ToList(),
            },
            _ => stmt,
        };

    private IrCatchClause RewriteCatch(IrCatchClause c) =>
        c with
        {
            Body = c.Body.Select(RewriteStatement).ToList(),
        };

    private IrSwitchCase RewriteSwitchCase(IrSwitchCase c) =>
        c with
        {
            Labels = c.Labels.Select(RewriteExpression).ToList(),
            Body = c.Body.Select(RewriteStatement).ToList(),
        };

    private IrExpression RewriteExpression(IrExpression expr) =>
        expr switch
        {
            IrCallExpression call => RewriteCall(call),
            IrMemberAccess access => RewriteMemberAccess(access),
            IrBinaryExpression b => b with
            {
                Left = RewriteExpression(b.Left),
                Right = RewriteExpression(b.Right),
            },
            IrUnaryExpression u => u with { Operand = RewriteExpression(u.Operand) },
            IrConditionalExpression c => c with
            {
                Condition = RewriteExpression(c.Condition),
                WhenTrue = RewriteExpression(c.WhenTrue),
                WhenFalse = RewriteExpression(c.WhenFalse),
            },
            IrCastExpression c => c with { Expression = RewriteExpression(c.Expression) },
            IrTypeCheck t => t with { Expression = RewriteExpression(t.Expression) },
            IrAwaitExpression a => a with { Expression = RewriteExpression(a.Expression) },
            IrYieldExpression y => y.Value is null
                ? y
                : y with
                {
                    Value = RewriteExpression(y.Value),
                },
            IrNewExpression n => n with
            {
                Arguments = n.Arguments.Select(RewriteArgument).ToList(),
            },
            IrElementAccess e => e with
            {
                Target = RewriteExpression(e.Target),
                Index = RewriteExpression(e.Index),
            },
            IrOptionalChain o => o with { Target = RewriteExpression(o.Target) },
            IrLambdaExpression l => l with { Body = l.Body.Select(RewriteStatement).ToList() },
            IrArrayLiteral a => a with { Elements = a.Elements.Select(RewriteExpression).ToList() },
            IrObjectLiteral o => o with
            {
                Properties = o
                    .Properties.Select(p => (p.Name, Value: RewriteExpression(p.Value)))
                    .ToList(),
            },
            IrSpreadExpression s => s with { Expression = RewriteExpression(s.Expression) },
            IrStringInterpolation si => si with
            {
                Parts = si.Parts.Select(RewriteInterpolationPart).ToList(),
            },
            IrIsPatternExpression ip => ip with { Expression = RewriteExpression(ip.Expression) },
            IrSwitchExpression sw => sw with
            {
                Scrutinee = RewriteExpression(sw.Scrutinee),
                Arms = sw.Arms.Select(RewriteSwitchArm).ToList(),
            },
            IrWithExpression w => w with
            {
                Source = RewriteExpression(w.Source),
                Assignments = w
                    .Assignments.Select(a => a with { Value = RewriteExpression(a.Value) })
                    .ToList(),
            },
            IrThrowExpression t => t with { Expression = RewriteExpression(t.Expression) },
            IrRuntimeHelperCall rh => rh with
            {
                Arguments = rh.Arguments.Select(RewriteExpression).ToList(),
            },
            IrTemplateExpression te => te with
            {
                Receiver = te.Receiver is null ? null : RewriteExpression(te.Receiver),
                Arguments = te.Arguments.Select(RewriteExpression).ToList(),
            },
            _ => expr,
        };

    private IrInterpolationPart RewriteInterpolationPart(IrInterpolationPart part) =>
        part switch
        {
            IrInterpolationExpression ie => ie with
            {
                Expression = RewriteExpression(ie.Expression),
            },
            _ => part,
        };

    private IrSwitchArm RewriteSwitchArm(IrSwitchArm arm) =>
        arm with
        {
            WhenClause = arm.WhenClause is null ? null : RewriteExpression(arm.WhenClause),
            Result = RewriteExpression(arm.Result),
        };

    private IrExpression RewriteCall(IrCallExpression call)
    {
        var rewrittenTarget = RewriteExpression(call.Target);
        var rewrittenArgs = call.Arguments.Select(RewriteArgument).ToList();
        var rewrittenCall = call with { Target = rewrittenTarget, Arguments = rewrittenArgs };

        var mapped = IrToDartBclMapper.TryMapCall(rewrittenCall, _registry);
        return mapped ?? rewrittenCall;
    }

    private IrExpression RewriteMemberAccess(IrMemberAccess access)
    {
        var rewrittenTarget = RewriteExpression(access.Target);
        var rewritten = access with { Target = rewrittenTarget };

        var mapped = IrToDartBclMapper.TryMapMemberAccess(rewritten, _registry);
        return mapped ?? rewritten;
    }

    private IrArgument RewriteArgument(IrArgument arg) =>
        arg with
        {
            Value = RewriteExpression(arg.Value),
        };
}
