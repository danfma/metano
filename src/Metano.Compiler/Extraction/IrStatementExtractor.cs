using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.Extraction;

/// <summary>
/// Extracts Roslyn <see cref="StatementSyntax"/> nodes into semantic
/// <see cref="IrStatement"/>s. Handles the foundational subset:
/// blocks, expression statements, return, variable declarations, if/else.
/// Unsupported kinds produce <see cref="IrUnsupportedStatement"/> placeholders
/// so callers can detect partial support and fall back to legacy emission.
/// </summary>
public sealed class IrStatementExtractor(
    SemanticModel semanticModel,
    IrTypeOriginResolver? originResolver = null,
    Metano.Annotations.TargetLanguage? target = null
)
{
    private readonly SemanticModel _semantic = semanticModel;
    private readonly IrTypeOriginResolver? _originResolver = originResolver;
    private readonly IrExpressionExtractor _expressions = new(
        semanticModel,
        originResolver,
        target
    );

    /// <summary>
    /// Extracts a method/constructor/property body from a block or
    /// expression-bodied arrow. Returns the statements that should appear
    /// inside the target-language body (empty for bodiless declarations).
    /// </summary>
    public IReadOnlyList<IrStatement> ExtractBody(
        BlockSyntax? block,
        ArrowExpressionClauseSyntax? arrow,
        bool isVoid = false
    )
    {
        if (arrow is not null)
        {
            var expr = _expressions.Extract(arrow.Expression);
            return isVoid ? [new IrExpressionStatement(expr)] : [new IrReturnStatement(expr)];
        }

        if (block is null)
            return [];

        return ExtractStatements(block.Statements);
    }

    /// <summary>
    /// Like <see cref="ExtractBody"/> but takes a flat list of statements
    /// directly — useful for sources that aren't wrapped in a block (e.g.,
    /// C# 9 top-level statements live as siblings on the
    /// <see cref="CompilationUnitSyntax"/>). The same TryGetValue
    /// expansion and effective-const promotion passes apply, since both
    /// only need a contiguous statement sequence.
    /// </summary>
    public IReadOnlyList<IrStatement> ExtractStatements(IReadOnlyList<StatementSyntax> source)
    {
        var statements = new List<IrStatement>(source.Count);
        foreach (var stmt in source)
        {
            if (TryExpandTryGetValue(stmt, statements))
                continue;
            statements.Add(ExtractStatement(stmt));
        }
        return PromoteEffectiveConsts(statements);
    }

    /// <summary>
    /// Detects <c>if (dict.TryGetValue(key, out var x)) { … }</c> and expands
    /// it into two IR statements:
    /// <list type="number">
    ///   <item><c>const x = dict.get(key);</c></item>
    ///   <item><c>if (x !== undefined) { … } [else { … }]</c></item>
    /// </list>
    /// Matching C# semantics — `TryGetValue` returns true when the key is
    /// present. At the TS level we rely on `Map.get` returning `undefined`
    /// on a miss. Returns true when the expansion happened; callers skip
    /// the ordinary statement extraction for that node.
    /// </summary>
    private bool TryExpandTryGetValue(StatementSyntax stmt, List<IrStatement> sink)
    {
        if (stmt is not IfStatementSyntax ifStmt)
            return false;
        if (ifStmt.Condition is not InvocationExpressionSyntax invocation)
            return false;
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            return false;
        var symbol = _semantic.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
        if (symbol is null || symbol.Name != "TryGetValue")
            return false;
        var receiverType = _semantic.GetTypeInfo(memberAccess.Expression).Type;
        if (receiverType is not INamedTypeSymbol named || !IsDictionaryLikeReceiver(named))
            return false;
        if (invocation.ArgumentList.Arguments.Count != 2)
            return false;
        var outArg = invocation.ArgumentList.Arguments[1];
        if (!outArg.RefOrOutKeyword.IsKind(SyntaxKind.OutKeyword))
            return false;
        if (outArg.Expression is not DeclarationExpressionSyntax declExpr)
            return false;
        if (declExpr.Designation is not SingleVariableDesignationSyntax designation)
            return false;

        var varName = designation.Identifier.ValueText;
        var receiver = _expressions.Extract(memberAccess.Expression);
        var key = _expressions.Extract(invocation.ArgumentList.Arguments[0].Expression);

        sink.Add(
            new IrVariableDeclaration(
                varName,
                Type: null,
                Initializer: new IrCallExpression(
                    new IrMemberAccess(receiver, "get"),
                    [new IrArgument(key)]
                ),
                IsConst: true
            )
        );

        var condition = new IrBinaryExpression(
            new IrIdentifier(varName),
            IrBinaryOp.NotEqual,
            new IrIdentifier("undefined")
        );
        var thenBody = FlattenToBlock(ExtractStatement(ifStmt.Statement));
        var elseBody = ifStmt.Else?.Statement is not null
            ? FlattenToBlock(ExtractStatement(ifStmt.Else.Statement))
            : null;
        sink.Add(new IrIfStatement(condition, thenBody, elseBody));
        return true;
    }

    private static bool IsDictionaryLikeReceiver(INamedTypeSymbol type)
    {
        var n = type.OriginalDefinition.ToDisplayString();
        return n.StartsWith("System.Collections.Generic.Dictionary")
            || n.StartsWith("System.Collections.Generic.IDictionary")
            || n.StartsWith("System.Collections.Generic.IReadOnlyDictionary")
            || n.StartsWith("System.Collections.Concurrent.ConcurrentDictionary");
    }

    /// <summary>
    /// Post-processing pass that promotes <see cref="IrVariableDeclaration"/>s
    /// to <c>IsConst = true</c> when the declared local is never reassigned
    /// inside the method body. Mirrors the legacy <c>ExpressionTransformer</c>
    /// behavior of emitting <c>const</c> for <c>var</c>-declared locals that
    /// are effectively immutable — otherwise every extraction would downgrade
    /// them to <c>let</c>, cluttering the output and diverging from the
    /// pinned TS goldens.
    /// <para>
    /// The analysis is conservative and IR-only: it collects the names of
    /// every <see cref="IrIdentifier"/> that appears as the target of an
    /// assignment (simple or compound) or a prefix/postfix increment /
    /// decrement, then marks any declaration whose name is absent from that
    /// set as const. <see cref="IrMemberAccess"/> writes (<c>this.x = …</c>)
    /// and indexer writes do not count because they don't rebind a local.
    /// </para>
    /// </summary>
    private static IReadOnlyList<IrStatement> PromoteEffectiveConsts(
        IReadOnlyList<IrStatement> statements
    )
    {
        var reassigned = new HashSet<string>(StringComparer.Ordinal);
        foreach (var stmt in statements)
            CollectReassignedLocals(stmt, reassigned);

        return statements.Select(s => RewriteDeclarations(s, reassigned)).ToList();
    }

    private static void CollectReassignedLocals(IrStatement stmt, HashSet<string> acc)
    {
        switch (stmt)
        {
            case IrExpressionStatement es:
                CollectReassignedLocals(es.Expression, acc);
                break;
            case IrReturnStatement ret when ret.Value is not null:
                CollectReassignedLocals(ret.Value, acc);
                break;
            case IrVariableDeclaration vd when vd.Initializer is not null:
                CollectReassignedLocals(vd.Initializer, acc);
                break;
            case IrIfStatement ifs:
                CollectReassignedLocals(ifs.Condition, acc);
                foreach (var s in ifs.Then)
                    CollectReassignedLocals(s, acc);
                if (ifs.Else is not null)
                    foreach (var s in ifs.Else)
                        CollectReassignedLocals(s, acc);
                break;
            case IrBlockStatement block:
                foreach (var s in block.Statements)
                    CollectReassignedLocals(s, acc);
                break;
            case IrThrowStatement th:
                CollectReassignedLocals(th.Expression, acc);
                break;
            case IrForEachStatement fe:
                CollectReassignedLocals(fe.Collection, acc);
                foreach (var s in fe.Body)
                    CollectReassignedLocals(s, acc);
                break;
            case IrWhileStatement ws:
                CollectReassignedLocals(ws.Condition, acc);
                foreach (var s in ws.Body)
                    CollectReassignedLocals(s, acc);
                break;
            case IrDoWhileStatement dw:
                CollectReassignedLocals(dw.Condition, acc);
                foreach (var s in dw.Body)
                    CollectReassignedLocals(s, acc);
                break;
            case IrTryStatement ts:
                foreach (var s in ts.Body)
                    CollectReassignedLocals(s, acc);
                if (ts.Catches is not null)
                    foreach (var c in ts.Catches)
                    foreach (var s in c.Body)
                        CollectReassignedLocals(s, acc);
                if (ts.Finally is not null)
                    foreach (var s in ts.Finally)
                        CollectReassignedLocals(s, acc);
                break;
            case IrSwitchStatement sw:
                CollectReassignedLocals(sw.Expression, acc);
                foreach (var c in sw.Cases)
                {
                    foreach (var lbl in c.Labels)
                        CollectReassignedLocals(lbl, acc);
                    foreach (var s in c.Body)
                        CollectReassignedLocals(s, acc);
                }
                break;
        }
    }

    private static void CollectReassignedLocals(IrExpression expr, HashSet<string> acc)
    {
        switch (expr)
        {
            case IrBinaryExpression { Operator: var op } bin when IsAssignmentOp(op):
                if (bin.Left is IrIdentifier id)
                    acc.Add(id.Name);
                // Still walk the right-hand side — a nested assignment could
                // target another local (e.g., `x = (y = 1)`).
                CollectReassignedLocals(bin.Left, acc);
                CollectReassignedLocals(bin.Right, acc);
                break;
            case IrBinaryExpression bin:
                CollectReassignedLocals(bin.Left, acc);
                CollectReassignedLocals(bin.Right, acc);
                break;
            case IrUnaryExpression un
                when un.Operator is IrUnaryOp.Increment or IrUnaryOp.Decrement:
                if (un.Operand is IrIdentifier uid)
                    acc.Add(uid.Name);
                CollectReassignedLocals(un.Operand, acc);
                break;
            case IrUnaryExpression un:
                CollectReassignedLocals(un.Operand, acc);
                break;
            case IrMemberAccess ma:
                CollectReassignedLocals(ma.Target, acc);
                break;
            case IrElementAccess ea:
                CollectReassignedLocals(ea.Target, acc);
                CollectReassignedLocals(ea.Index, acc);
                break;
            case IrCallExpression call:
                CollectReassignedLocals(call.Target, acc);
                foreach (var arg in call.Arguments)
                    CollectReassignedLocals(arg.Value, acc);
                break;
            case IrNewExpression ne:
                foreach (var arg in ne.Arguments)
                    CollectReassignedLocals(arg.Value, acc);
                break;
            case IrConditionalExpression cond:
                CollectReassignedLocals(cond.Condition, acc);
                CollectReassignedLocals(cond.WhenTrue, acc);
                CollectReassignedLocals(cond.WhenFalse, acc);
                break;
            case IrAwaitExpression aw:
                CollectReassignedLocals(aw.Expression, acc);
                break;
            case IrThrowExpression th:
                CollectReassignedLocals(th.Expression, acc);
                break;
            case IrCastExpression cast:
                CollectReassignedLocals(cast.Expression, acc);
                break;
            case IrLambdaExpression lambda:
                foreach (var s in lambda.Body)
                    CollectReassignedLocals(s, acc);
                break;
            case IrStringInterpolation interp:
                foreach (var part in interp.Parts)
                    if (part is IrInterpolationExpression ipe)
                        CollectReassignedLocals(ipe.Expression, acc);
                break;
            case IrIsPatternExpression isPattern:
                CollectReassignedLocals(isPattern.Expression, acc);
                break;
        }
    }

    private static bool IsAssignmentOp(IrBinaryOp op) =>
        op
            is IrBinaryOp.Assign
                or IrBinaryOp.AddAssign
                or IrBinaryOp.SubtractAssign
                or IrBinaryOp.MultiplyAssign
                or IrBinaryOp.DivideAssign
                or IrBinaryOp.ModuloAssign
                or IrBinaryOp.BitwiseAndAssign
                or IrBinaryOp.BitwiseOrAssign
                or IrBinaryOp.BitwiseXorAssign
                or IrBinaryOp.LeftShiftAssign
                or IrBinaryOp.RightShiftAssign
                or IrBinaryOp.NullCoalescingAssign;

    private static IrStatement RewriteDeclarations(
        IrStatement stmt,
        IReadOnlySet<string> reassigned
    ) =>
        stmt switch
        {
            IrVariableDeclaration vd when !vd.IsConst && !reassigned.Contains(vd.Name) => vd with
            {
                IsConst = true,
            },
            IrIfStatement ifs => ifs with
            {
                Then = ifs.Then.Select(s => RewriteDeclarations(s, reassigned)).ToList(),
                Else = ifs.Else?.Select(s => RewriteDeclarations(s, reassigned)).ToList(),
            },
            IrBlockStatement block => block with
            {
                Statements = block
                    .Statements.Select(s => RewriteDeclarations(s, reassigned))
                    .ToList(),
            },
            IrForEachStatement fe => fe with
            {
                Body = fe.Body.Select(s => RewriteDeclarations(s, reassigned)).ToList(),
            },
            IrWhileStatement ws => ws with
            {
                Body = ws.Body.Select(s => RewriteDeclarations(s, reassigned)).ToList(),
            },
            IrDoWhileStatement dw => dw with
            {
                Body = dw.Body.Select(s => RewriteDeclarations(s, reassigned)).ToList(),
            },
            IrTryStatement ts => ts with
            {
                Body = ts.Body.Select(s => RewriteDeclarations(s, reassigned)).ToList(),
                Catches = ts
                    .Catches?.Select(c =>
                        c with
                        {
                            Body = c.Body.Select(s => RewriteDeclarations(s, reassigned)).ToList(),
                        }
                    )
                    .ToList(),
                Finally = ts.Finally?.Select(s => RewriteDeclarations(s, reassigned)).ToList(),
            },
            IrSwitchStatement sw => sw with
            {
                Cases = sw
                    .Cases.Select(c =>
                        c with
                        {
                            Body = c.Body.Select(s => RewriteDeclarations(s, reassigned)).ToList(),
                        }
                    )
                    .ToList(),
            },
            _ => stmt,
        };

    public IrStatement ExtractStatement(StatementSyntax statement) =>
        statement switch
        {
            BlockSyntax block => new IrBlockStatement(
                block.Statements.Select(ExtractStatement).ToList()
            ),
            ExpressionStatementSyntax expr => new IrExpressionStatement(
                _expressions.Extract(expr.Expression)
            ),
            ReturnStatementSyntax ret => new IrReturnStatement(
                ret.Expression is not null ? _expressions.Extract(ret.Expression) : null
            ),
            LocalDeclarationStatementSyntax local => ExtractLocalDeclaration(local),
            IfStatementSyntax ifs => ExtractIf(ifs),
            ThrowStatementSyntax th => new IrThrowStatement(
                th.Expression is not null
                    ? _expressions.Extract(th.Expression)
                    : new IrLiteral(null, IrLiteralKind.Null)
            ),
            ForEachStatementSyntax fe => ExtractForEach(fe),
            ForStatementSyntax fs => ExtractFor(fs),
            WhileStatementSyntax wh => new IrWhileStatement(
                _expressions.Extract(wh.Condition),
                FlattenToBlock(ExtractStatement(wh.Statement))
            ),
            DoStatementSyntax ds => new IrDoWhileStatement(
                FlattenToBlock(ExtractStatement(ds.Statement)),
                _expressions.Extract(ds.Condition)
            ),
            TryStatementSyntax ts => ExtractTry(ts),
            SwitchStatementSyntax sw => ExtractSwitch(sw),
            BreakStatementSyntax => new IrBreakStatement(),
            ContinueStatementSyntax => new IrContinueStatement(),
            YieldStatementSyntax ys
                when ys.IsKind(SyntaxKind.YieldReturnStatement) && ys.Expression is not null =>
                new IrExpressionStatement(
                    new IrYieldExpression(_expressions.Extract(ys.Expression))
                ),
            YieldStatementSyntax ys when ys.IsKind(SyntaxKind.YieldBreakStatement) =>
                new IrYieldBreakStatement(),
            _ => new IrUnsupportedStatement(statement.Kind().ToString()),
        };

    /// <summary>
    /// Lowers a C# <c>for (init; cond; inc) { body }</c> to
    /// <see cref="IrForStatement"/>. Multiple declarators in the initializer
    /// (e.g. <c>int i = 0, j = 1</c>) surface as the first one only — the
    /// legacy backend has the same limitation.
    /// </summary>
    private IrStatement ExtractFor(ForStatementSyntax fs)
    {
        IrStatement? init = null;
        if (fs.Declaration is not null && fs.Declaration.Variables.Count > 0)
        {
            var v = fs.Declaration.Variables[0];
            init = new IrVariableDeclaration(
                v.Identifier.ValueText,
                Type: null,
                Initializer: v.Initializer?.Value is null
                    ? null
                    : _expressions.Extract(v.Initializer.Value),
                IsConst: false
            );
        }
        else if (fs.Initializers.Count > 0)
            init = new IrExpressionStatement(_expressions.Extract(fs.Initializers[0]));

        var cond = fs.Condition is null ? null : _expressions.Extract(fs.Condition);
        var inc = fs.Incrementors.Count > 0 ? _expressions.Extract(fs.Incrementors[0]) : null;
        var body = FlattenToBlock(ExtractStatement(fs.Statement));
        return new IrForStatement(init, cond, inc, body);
    }

    private IrStatement ExtractForEach(ForEachStatementSyntax fe)
    {
        var variable = fe.Identifier.ValueText;
        var typeSymbol = _semantic.GetTypeInfo(fe.Type).Type;
        var variableType =
            typeSymbol is not null
            && fe.Type is not IdentifierNameSyntax { Identifier.ValueText: "var" }
                ? IrTypeRefMapper.Map(typeSymbol, _originResolver)
                : null;
        var collection = _expressions.Extract(fe.Expression);
        var body = FlattenToBlock(ExtractStatement(fe.Statement));
        return new IrForEachStatement(variable, variableType, collection, body);
    }

    private IrStatement ExtractTry(TryStatementSyntax ts)
    {
        var body = ts.Block.Statements.Select(ExtractStatement).ToList();
        var catches = ts.Catches.Count > 0 ? ts.Catches.Select(ExtractCatch).ToList() : null;
        var finallyBody = ts.Finally is not null
            ? ts.Finally.Block.Statements.Select(ExtractStatement).ToList()
            : null;
        return new IrTryStatement(body, catches, finallyBody);
    }

    private IrCatchClause ExtractCatch(CatchClauseSyntax cc)
    {
        IrTypeRef? exType = null;
        string? varName = null;
        if (cc.Declaration is not null)
        {
            var sym = _semantic.GetTypeInfo(cc.Declaration.Type).Type;
            if (sym is not null)
                exType = IrTypeRefMapper.Map(sym, _originResolver);
            if (cc.Declaration.Identifier.ValueText.Length > 0)
                varName = cc.Declaration.Identifier.ValueText;
        }
        var body = cc.Block.Statements.Select(ExtractStatement).ToList();
        return new IrCatchClause(exType, varName, body);
    }

    private IrStatement ExtractSwitch(SwitchStatementSyntax sw)
    {
        var subject = _expressions.Extract(sw.Expression);
        var cases = sw.Sections.Select(ExtractSwitchSection).ToList();
        return new IrSwitchStatement(subject, cases);
    }

    private IrSwitchCase ExtractSwitchSection(SwitchSectionSyntax section)
    {
        var labels = new List<IrExpression>();
        foreach (var label in section.Labels)
        {
            switch (label)
            {
                case CaseSwitchLabelSyntax cs:
                    labels.Add(_expressions.Extract(cs.Value));
                    break;
                case DefaultSwitchLabelSyntax:
                    // Empty label list signals "default" per the IR contract.
                    break;
            }
        }
        var body = section.Statements.Select(ExtractStatement).ToList();
        return new IrSwitchCase(labels, body);
    }

    private IrStatement ExtractLocalDeclaration(LocalDeclarationStatementSyntax local)
    {
        // Only the first variable — multi-declarator locals (`int a, b;`) are rare
        // and not worth the complexity at this stage.
        var variable = local.Declaration.Variables[0];
        var name = variable.Identifier.ValueText;

        IrTypeRef? type = null;
        if (local.Declaration.Type is not IdentifierNameSyntax { Identifier.ValueText: "var" })
        {
            var typeSymbol = _semantic.GetTypeInfo(local.Declaration.Type).Type;
            if (typeSymbol is not null)
                type = IrTypeRefMapper.Map(typeSymbol, _originResolver);
        }

        var initializer = variable.Initializer is not null
            ? _expressions.Extract(variable.Initializer.Value)
            : null;

        return new IrVariableDeclaration(name, type, initializer, IsConst: local.IsConst);
    }

    private IrStatement ExtractIf(IfStatementSyntax ifs)
    {
        var condition = _expressions.Extract(ifs.Condition);
        var then = FlattenToBlock(ExtractStatement(ifs.Statement));
        var elseBranch = ifs.Else is not null
            ? FlattenToBlock(ExtractStatement(ifs.Else.Statement))
            : null;
        return new IrIfStatement(condition, then, elseBranch);
    }

    /// <summary>
    /// Ensures a branch statement always surfaces as a list — if it's already a block
    /// we unwrap it; otherwise we wrap the single statement.
    /// </summary>
    private static IReadOnlyList<IrStatement> FlattenToBlock(IrStatement statement) =>
        statement is IrBlockStatement block ? block.Statements : [statement];
}

/// <summary>
/// Placeholder for statements not yet covered by the extractor. See
/// <see cref="IrUnsupportedExpression"/> for the expression analog.
/// </summary>
public sealed record IrUnsupportedStatement(string Kind) : IrStatement;
