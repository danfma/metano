using Metano.Compiler.Analysis;
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

            return _expressions.LowerOutArgumentCalls(ArrowStatement(expr, isVoid));
        }

        if (block is null)
            return [];

        return ExtractStatements(block.Statements);
    }

    private static IrStatement ArrowStatement(IrExpression expr, bool isVoid) =>
        isVoid ? new IrExpressionStatement(expr) : new IrReturnStatement(expr);

    /// <summary>
    /// Like <see cref="ExtractBody"/> but takes a flat list of statements
    /// directly — useful for sources that aren't wrapped in a block (e.g.,
    /// C# 9 top-level statements live as siblings on the
    /// <see cref="CompilationUnitSyntax"/>). The same <c>out</c>-argument
    /// lowering and effective-const promotion passes apply, since they only
    /// need a contiguous statement sequence.
    /// </summary>
    public IReadOnlyList<IrStatement> ExtractStatements(IReadOnlyList<StatementSyntax> source) =>
        PromoteEffectiveConsts(ExtractStatementSequence(source));

    /// <summary>
    /// Extracts a statement list that forms one C# scope (a block, a switch
    /// section, an embedded statement). Each statement goes through
    /// <see cref="OutArgumentCallLowering"/>, which places the results of its
    /// <c>out</c>-argument calls in this same list — the scope C# gives an
    /// <c>out var</c> declared in an expression statement, a declaration, a
    /// <c>return</c>, an <c>if</c> condition — or, for a loop, inside the loop.
    /// </summary>
    private List<IrStatement> ExtractStatementSequence(IEnumerable<StatementSyntax> source)
    {
        var extracted = source.SelectMany(ExtractStatementInScope).ToList();

        return extracted
            .SelectMany(
                (stmt, index) => _expressions.OutArgumentCalls.Lower(stmt, extracted[(index + 1)..])
            )
            .ToList();
    }

    /// <summary>
    /// The body of an <c>if</c> branch or a loop. A non-block embedded
    /// statement is its own C# scope, so it extracts as a one-statement
    /// sequence (binding any <c>out var</c> it declares inside the branch).
    /// </summary>
    private List<IrStatement> ExtractEmbedded(StatementSyntax statement) =>
        statement is BlockSyntax block
            ? ExtractStatementSequence(block.Statements)
            : ExtractStatementSequence([statement]);

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
    internal static IReadOnlyList<IrStatement> PromoteEffectiveConsts(
        IReadOnlyList<IrStatement> statements
    )
    {
        var reassigned = IrLocalReassignmentCollector.CollectFromStatements(statements);

        return statements.Select(s => RewriteDeclarations(s, reassigned)).ToList();
    }

    private static IrStatement RewriteDeclarations(
        IrStatement stmt,
        IReadOnlySet<string> reassigned
    ) =>
        stmt switch
        {
            IrVariableDeclaration vd
                when !vd.IsConst && vd.Initializer is not null && !reassigned.Contains(vd.Name) =>
                vd with
                {
                    IsConst = true,
                },
            IrTupleDeconstruction td
                when !td.IsConst
                    && !td.Elements.Any(e => e.Name is { } name && reassigned.Contains(name)) =>
                td with
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
            IrForStatement fs => fs with
            {
                Body = fs.Body.Select(s => RewriteDeclarations(s, reassigned)).ToList(),
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

    /// <summary>
    /// The statements one C# statement of a scope extracts to: a local
    /// declaration contributes one declaration per declarator
    /// (<c>int a = 1, b = 2;</c>), every other statement exactly one. A
    /// declaration statement only appears directly in a scope — C# rejects it
    /// as an embedded statement — so it never needs a single-statement form.
    /// </summary>
    private IEnumerable<IrStatement> ExtractStatementInScope(StatementSyntax statement) =>
        statement is LocalDeclarationStatementSyntax local
            ? ExtractDeclarators(local.Declaration, local.IsConst)
            : [ExtractStatement(statement)];

    private IrStatement ExtractStatement(StatementSyntax statement) =>
        statement switch
        {
            BlockSyntax block => new IrBlockStatement(ExtractStatementSequence(block.Statements)),
            // `var (a, b) = expr;` parses as an ExpressionStatement wrapping a
            // SimpleAssignment whose Left is a DeclarationExpression with a
            // parenthesized variable designation. Intercept it BEFORE the generic
            // ExpressionStatement arm so it does not fall through to the
            // single-variable DeclarationExpression path (or IrUnsupportedStatement).
            ExpressionStatementSyntax { Expression: AssignmentExpressionSyntax assign }
                when TryExtractTupleDeconstruction(assign) is { } deconstruction => deconstruction,
            ExpressionStatementSyntax expr => new IrExpressionStatement(
                _expressions.Extract(expr.Expression)
            ),
            ReturnStatementSyntax ret => new IrReturnStatement(
                ret.Expression is not null ? _expressions.Extract(ret.Expression) : null
            ),
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
                ExtractEmbedded(wh.Statement)
            ),
            DoStatementSyntax ds => new IrDoWhileStatement(
                ExtractEmbedded(ds.Statement),
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
    /// <see cref="IrForStatement"/>, keeping every declarator of the
    /// initializer (<c>int i = 0, j = n</c>), every initializer expression
    /// (<c>i = 0, j = n</c>) and every incrementor (<c>i++, j--</c>) in source
    /// order.
    /// </summary>
    private IrStatement ExtractFor(ForStatementSyntax fs)
    {
        var initializers = ExtractForInitializers(fs);
        var condition = fs.Condition is null ? null : _expressions.Extract(fs.Condition);
        var incrementors = fs.Incrementors.Select(_expressions.Extract).ToList();
        var body = ExtractEmbedded(fs.Statement);

        return new IrForStatement(initializers, condition, incrementors, body);
    }

    private IReadOnlyList<IrStatement> ExtractForInitializers(ForStatementSyntax fs) =>
        fs.Declaration is { } declaration
            ? ExtractDeclarators(declaration, isConst: false)
            : fs
                .Initializers.Select(i => new IrExpressionStatement(_expressions.Extract(i)))
                .ToList();

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
        var body = ExtractEmbedded(fe.Statement);
        return new IrForEachStatement(variable, variableType, collection, body);
    }

    private IrStatement ExtractTry(TryStatementSyntax ts)
    {
        var body = ExtractStatementSequence(ts.Block.Statements);
        var catches = ts.Catches.Count > 0 ? ts.Catches.Select(ExtractCatch).ToList() : null;
        var finallyBody = ts.Finally is not null
            ? ExtractStatementSequence(ts.Finally.Block.Statements)
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
        var body = ExtractStatementSequence(cc.Block.Statements);
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
        var labels = section.Labels.Select(_expressions.ExtractCaseLabel).ToList();
        var body = ExtractStatementSequence(section.Statements);

        return new IrSwitchCase(labels, body);
    }

    /// <summary>
    /// One <see cref="IrVariableDeclaration"/> per declarator of
    /// <paramref name="declaration"/>, in source order: <c>int a = 1, b;</c>
    /// declares both <c>a</c> and <c>b</c>, each with the shared declared type
    /// (none for <c>var</c>).
    /// </summary>
    private List<IrStatement> ExtractDeclarators(
        VariableDeclarationSyntax declaration,
        bool isConst
    ) =>
        declaration
            .Variables.Select(variable =>
                (IrStatement)ExtractDeclarator(declaration, variable, isConst)
            )
            .ToList();

    private IrVariableDeclaration ExtractDeclarator(
        VariableDeclarationSyntax declaration,
        VariableDeclaratorSyntax variable,
        bool isConst
    )
    {
        var initializer = variable.Initializer is not null
            ? _expressions.Extract(variable.Initializer.Value)
            : null;

        return new IrVariableDeclaration(
            variable.Identifier.ValueText,
            DeclaredLocalType(declaration, variable),
            initializer,
            IsConst: isConst
        );
    }

    /// <summary>
    /// The declared type of a local, or null for <c>var</c>. The declared
    /// local's type keeps the <c>?</c> annotation of a nullable reference
    /// type, which the type syntax's TypeInfo drops.
    /// </summary>
    private IrTypeRef? DeclaredLocalType(
        VariableDeclarationSyntax declaration,
        VariableDeclaratorSyntax variable
    )
    {
        if (declaration.Type is IdentifierNameSyntax { Identifier.ValueText: "var" })
            return null;

        var typeSymbol =
            (_semantic.GetDeclaredSymbol(variable) as ILocalSymbol)?.Type
            ?? _semantic.GetTypeInfo(declaration.Type).Type;

        return typeSymbol is null ? null : IrTypeRefMapper.Map(typeSymbol, _originResolver);
    }

    /// <summary>
    /// Recognizes a deconstructing variable declaration — <c>var (a, b) = expr;</c>
    /// — and lowers it to an <see cref="IrTupleDeconstruction"/>. Such a statement
    /// is a <see cref="SimpleAssignmentExpression"/> whose <c>Left</c> is a
    /// <see cref="DeclarationExpressionSyntax"/> (type <c>var</c>) carrying a
    /// <see cref="ParenthesizedVariableDesignationSyntax"/>; each inner designation
    /// is either a <see cref="SingleVariableDesignationSyntax"/> (bound name) or a
    /// <see cref="DiscardDesignationSyntax"/> (discard → null element).
    /// <para>
    /// The match is gated on a <em>semantic</em> guard: the right-hand side's
    /// type must be the one shape that actually IS a JS array — an array type or
    /// a <c>[JsTuple]</c> record. A user type with a custom <c>Deconstruct</c>
    /// method, or a native C# <c>ValueTuple</c> (deferred — research D7), would
    /// also parse as this syntax but is NOT a JS array; array-destructuring it
    /// would read <c>undefined</c> at runtime. For those, this method returns
    /// null so the statement surfaces as <see cref="IrUnsupportedStatement"/>
    /// (loud, per Constitution V) rather than mis-lowering silently.
    /// </para>
    /// Returns null for any other assignment shape too, so the caller falls back
    /// to the ordinary expression-statement path. The
    /// <see cref="IrTupleDeconstruction.IsConst"/> flag starts false and is
    /// promoted by <see cref="PromoteEffectiveConsts"/> exactly like a normal
    /// local (const unless a bound name is reassigned).
    /// </summary>
    private IrTupleDeconstruction? TryExtractTupleDeconstruction(AssignmentExpressionSyntax assign)
    {
        if (!assign.IsKind(SyntaxKind.SimpleAssignmentExpression))
            return null;
        if (assign.Left is not DeclarationExpressionSyntax declaration)
            return null;
        if (declaration.Designation is not ParenthesizedVariableDesignationSyntax parenthesized)
            return null;
        if (!IsJsArrayTupleInitializer(assign.Right))
            return null;

        var elements = new List<IrDeconstructionElement>(parenthesized.Variables.Count);
        foreach (var variable in parenthesized.Variables)
        {
            switch (variable)
            {
                case SingleVariableDesignationSyntax single:
                    elements.Add(new IrDeconstructionElement(single.Identifier.ValueText));
                    break;
                case DiscardDesignationSyntax:
                    elements.Add(new IrDeconstructionElement(Name: null));
                    break;
                default:
                    // Nested deconstruction (`(a, (b, c))`) is out of scope — bail
                    // so the statement surfaces as unsupported rather than dropping
                    // a malformed binding.
                    return null;
            }
        }

        var initializer = _expressions.Extract(assign.Right);
        // Default to let; PromoteEffectiveConsts upgrades to const when no bound
        // name is reassigned, mirroring IrVariableDeclaration handling.
        return new IrTupleDeconstruction(elements, initializer, IsConst: false);
    }

    /// <summary>
    /// True when <paramref name="initializer"/>'s semantic type is one of the two
    /// shapes that ARE a JS array — an array type or a <c>[JsTuple]</c> record —
    /// the only receivers for which TS array-destructuring (<c>const [a, b] =
    /// …</c>) is sound. A user type exposing a <c>Deconstruct</c> method and a
    /// native C# <c>ValueTuple</c> both parse as the deconstruction syntax yet
    /// are not JS arrays at runtime; gating here keeps the interception off them
    /// so they fall through to <see cref="IrUnsupportedStatement"/> (native
    /// ValueTuple deconstruction is out of scope — research D7). Uses the
    /// converted type so an implicit conversion to an array-shaped target is
    /// honored, falling back to the expression's own type.
    /// </summary>
    private bool IsJsArrayTupleInitializer(ExpressionSyntax initializer)
    {
        var typeInfo = _semantic.GetTypeInfo(initializer);
        var type = typeInfo.Type ?? typeInfo.ConvertedType;
        return type switch
        {
            IArrayTypeSymbol => true,
            INamedTypeSymbol named => named.HasJsTuple(),
            _ => false,
        };
    }

    private IrStatement ExtractIf(IfStatementSyntax ifs)
    {
        var condition = _expressions.Extract(ifs.Condition);
        var then = ExtractEmbedded(ifs.Statement);
        var elseBranch = ifs.Else is not null ? ExtractEmbedded(ifs.Else.Statement) : null;
        return new IrIfStatement(condition, then, elseBranch);
    }
}

/// <summary>
/// Placeholder for statements not yet covered by the extractor. See
/// <see cref="IrUnsupportedExpression"/> for the expression analog.
/// </summary>
public sealed record IrUnsupportedStatement(string Kind) : IrStatement;
