using MetaSharp.Compiler.Diagnostics;
using MetaSharp.TypeScript.AST;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MetaSharp.Transformation;

/// <summary>
/// Handles C# statements and lowers them into TypeScript statements. The router
/// (<see cref="Transform"/>) covers <c>return</c>, <c>yield return</c> / <c>yield break</c>,
/// <c>if</c> / <c>else</c>, <c>throw</c>, expression statements, local variable
/// declarations, and (via the parent's <see cref="SwitchHandler"/>) <c>switch</c>.
///
/// Block statements are also handled here: <see cref="TransformBody"/> turns either an
/// arrow expression body or a block body into a flat list of TypeScript statements,
/// honouring the void/non-void distinction for arrow bodies.
///
/// Holds a reference to the parent <see cref="ExpressionTransformer"/> for recursive
/// expression transformation, switch routing, and diagnostic reporting.
/// </summary>
public sealed class StatementHandler(ExpressionTransformer parent)
{
    private readonly ExpressionTransformer _parent = parent;

    public TsStatement Transform(StatementSyntax statement)
    {
        return statement switch
        {
            ReturnStatementSyntax ret => new TsReturnStatement(
                ret.Expression is not null ? _parent.TransformExpression(ret.Expression) : null
            ),

            YieldStatementSyntax yieldReturn
                when yieldReturn.IsKind(SyntaxKind.YieldReturnStatement)
                    && yieldReturn.Expression is not null =>
                new TsYieldStatement(_parent.TransformExpression(yieldReturn.Expression)),

            YieldStatementSyntax yieldBreak
                when yieldBreak.IsKind(SyntaxKind.YieldBreakStatement) =>
                new TsYieldBreakStatement(),

            IfStatementSyntax ifStmt => TransformIf(ifStmt),

            ThrowStatementSyntax throwStmt => new TsThrowStatement(
                _parent.TransformExpression(throwStmt.Expression!)
            ),

            ExpressionStatementSyntax exprStmt => new TsExpressionStatement(
                _parent.TransformExpression(exprStmt.Expression)
            ),

            LocalDeclarationStatementSyntax localDecl => TransformLocalDeclaration(localDecl),

            SwitchStatementSyntax switchStmt => _parent.Switches.TransformSwitchStatement(switchStmt),

            BlockSyntax block =>
            // Flatten single-statement blocks
            block.Statements.Count == 1
                ? Transform(block.Statements[0])
                : throw new NotSupportedException(
                    "Multi-statement blocks should be handled by the caller"
                ),

            _ => UnsupportedStatement(statement),
        };
    }

    public IReadOnlyList<TsStatement> TransformBody(
        BlockSyntax? block,
        ArrowExpressionClauseSyntax? arrow,
        bool isVoid = false)
    {
        if (arrow is not null)
        {
            var expr = _parent.TransformExpression(arrow.Expression);
            return isVoid
                ? [new TsExpressionStatement(expr)]
                : [new TsReturnStatement(expr)];
        }

        if (block is not null)
            return block.Statements.Select(Transform).ToList();

        return [];
    }

    private TsIfStatement TransformIf(IfStatementSyntax ifStmt)
    {
        var condition = _parent.TransformExpression(ifStmt.Condition);
        var thenBody = TransformStatementBody(ifStmt.Statement);
        var elseBody = ifStmt.Else?.Statement is not null
            ? TransformStatementBody(ifStmt.Else.Statement)
            : null;

        return new TsIfStatement(condition, thenBody, elseBody);
    }

    private IReadOnlyList<TsStatement> TransformStatementBody(StatementSyntax statement)
    {
        if (statement is BlockSyntax block)
            return block.Statements.Select(Transform).ToList();

        return [Transform(statement)];
    }

    private TsVariableDeclaration TransformLocalDeclaration(LocalDeclarationStatementSyntax decl)
    {
        var variable = decl.Declaration.Variables[0];
        var name = variable.Identifier.Text;
        var init = variable.Initializer?.Value is not null
            ? _parent.TransformExpression(variable.Initializer.Value)
            : new TsIdentifier("undefined");

        // C# `const` / `readonly` → always TS `const`. Otherwise default to `const` and
        // demote to `let` only when the local is mutated later in its enclosing scope
        // (assignment, compound assignment, ++/--, ref/out arg). This produces idiomatic
        // TS where most locals are immutable and the few mutable ones stand out.
        var isConst = decl.IsConst
            || decl.Modifiers.Any(SyntaxKind.ReadOnlyKeyword)
            || !IsLocalMutated(variable);

        return new TsVariableDeclaration(name, init, isConst);
    }

    /// <summary>
    /// Returns true if the local declared by <paramref name="variable"/> is written to
    /// anywhere within its enclosing function/method body — meaning the emitted TS must
    /// use <c>let</c> instead of <c>const</c>. Considers direct assignment, compound
    /// assignment, prefix/postfix increment/decrement, and <c>ref</c>/<c>out</c> args.
    /// </summary>
    private bool IsLocalMutated(VariableDeclaratorSyntax variable)
    {
        if (_parent.Model.GetDeclaredSymbol(variable) is not ILocalSymbol local)
            return true; // conservative: if we can't resolve, assume mutable

        // Search the enclosing executable scope (method body, lambda, accessor, …).
        // Walking the entire enclosing block is sufficient because C# locals can't
        // escape it, and we re-resolve symbols through the SemanticModel so shadowing
        // by an inner scope (different ILocalSymbol) is naturally excluded.
        SyntaxNode? scope = variable.FirstAncestorOrSelf<BlockSyntax>();
        scope ??= variable.FirstAncestorOrSelf<MemberDeclarationSyntax>();
        if (scope is null) return true;

        foreach (var node in scope.DescendantNodes())
        {
            switch (node)
            {
                case AssignmentExpressionSyntax assign
                    when ResolvesTo(assign.Left, local):
                    return true;
                case PrefixUnaryExpressionSyntax pre
                    when (pre.IsKind(SyntaxKind.PreIncrementExpression)
                          || pre.IsKind(SyntaxKind.PreDecrementExpression))
                         && ResolvesTo(pre.Operand, local):
                    return true;
                case PostfixUnaryExpressionSyntax post
                    when (post.IsKind(SyntaxKind.PostIncrementExpression)
                          || post.IsKind(SyntaxKind.PostDecrementExpression))
                         && ResolvesTo(post.Operand, local):
                    return true;
                case ArgumentSyntax arg
                    when (arg.RefKindKeyword.IsKind(SyntaxKind.RefKeyword)
                          || arg.RefKindKeyword.IsKind(SyntaxKind.OutKeyword))
                         && ResolvesTo(arg.Expression, local):
                    return true;
            }
        }

        return false;
    }

    private bool ResolvesTo(ExpressionSyntax expr, ILocalSymbol local) =>
        SymbolEqualityComparer.Default.Equals(
            _parent.Model.GetSymbolInfo(expr).Symbol, local);

    private TsStatement UnsupportedStatement(StatementSyntax statement)
    {
        _parent.ReportDiagnostic?.Invoke(new MetaSharpDiagnostic(
            MetaSharpDiagnosticSeverity.Warning,
            DiagnosticCodes.UnsupportedFeature,
            $"Statement '{statement.Kind()}' is not supported by the transpiler.",
            statement.GetLocation()));
        return new TsExpressionStatement(
            new TsCallExpression(
                new TsPropertyAccess(new TsIdentifier("console"), "warn"),
                [new TsStringLiteral($"/* unsupported: {statement.Kind()} */")]
            )
        );
    }
}
