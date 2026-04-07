using MetaSharp.Compiler;
using MetaSharp.Compiler.Diagnostics;
using MetaSharp.TypeScript;
using MetaSharp.TypeScript.AST;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MetaSharp.Transformation;

/// <summary>
/// Transforms C# expressions and statements into TypeScript AST nodes.
/// </summary>
public sealed class ExpressionTransformer(SemanticModel model)
{
    /// <summary>
    /// When set, indicates we're inside an instance method and bare member references
    /// should be qualified with this identifier (e.g., "amount" for Amount.Doubled()).
    /// </summary>
    public string? SelfParameterName { get; set; }
    public bool AssemblyWideTranspile { get; set; }
    public IAssemblySymbol? CurrentAssembly { get; set; }
    public Action<MetaSharpDiagnostic>? ReportDiagnostic { get; set; }

    /// <summary>
    /// The Roslyn semantic model the expression transformer was created with.
    /// Exposed so extracted handlers (e.g., <see cref="PatternMatchingHandler"/>) can run
    /// their own type lookups against the same model.
    /// </summary>
    internal SemanticModel Model => model;

    private PatternMatchingHandler? _patterns;
    private PatternMatchingHandler Patterns => _patterns ??= new PatternMatchingHandler(this);

    private SwitchHandler? _switches;
    private SwitchHandler Switches => _switches ??= new SwitchHandler(this, Patterns);

    private LambdaHandler? _lambdas;
    private LambdaHandler Lambdas => _lambdas ??= new LambdaHandler(this);

    private ObjectCreationHandler? _objectCreation;
    private ObjectCreationHandler ObjectCreation => _objectCreation ??= new ObjectCreationHandler(this);

    private IdentifierHandler? _identifiers;
    private IdentifierHandler Identifiers => _identifiers ??= new IdentifierHandler(this);

    private GenericNameHandler? _genericNames;
    private GenericNameHandler GenericNames => _genericNames ??= new GenericNameHandler(this);

    private MemberAccessHandler? _memberAccess;
    private MemberAccessHandler MemberAccess => _memberAccess ??= new MemberAccessHandler(this);

    private InvocationHandler? _invocations;
    private InvocationHandler Invocations => _invocations ??= new InvocationHandler(this);

    private InterpolatedStringHandler? _interpolatedStrings;
    private InterpolatedStringHandler InterpolatedStrings => _interpolatedStrings ??= new InterpolatedStringHandler(this);

    private OptionalChainingHandler? _optionalChaining;
    private OptionalChainingHandler OptionalChaining => _optionalChaining ??= new OptionalChainingHandler(this);

    private CollectionExpressionHandler? _collectionExpressions;
    private CollectionExpressionHandler CollectionExpressions => _collectionExpressions ??= new CollectionExpressionHandler(this);

    private OperatorHandler? _operators;
    private OperatorHandler Operators => _operators ??= new OperatorHandler(this);

    private TsExpression Unsupported(SyntaxNode node, string message)
    {
        ReportDiagnostic?.Invoke(new MetaSharpDiagnostic(
            MetaSharpDiagnosticSeverity.Warning,
            DiagnosticCodes.UnsupportedFeature,
            message,
            node.GetLocation()));
        return new TsIdentifier($"/* unsupported: {node.Kind()} */");
    }

    // ─── Statements ─────────────────────────────────────────

    public TsStatement TransformStatement(StatementSyntax statement)
    {
        return statement switch
        {
            ReturnStatementSyntax ret => new TsReturnStatement(
                ret.Expression is not null ? TransformExpression(ret.Expression) : null
            ),

            YieldStatementSyntax yieldReturn
                when yieldReturn.IsKind(SyntaxKind.YieldReturnStatement)
                    && yieldReturn.Expression is not null =>
                new TsYieldStatement(TransformExpression(yieldReturn.Expression)),

            YieldStatementSyntax yieldBreak
                when yieldBreak.IsKind(SyntaxKind.YieldBreakStatement) =>
                new TsYieldBreakStatement(),

            IfStatementSyntax ifStmt => TransformIf(ifStmt),

            ThrowStatementSyntax throwStmt => new TsThrowStatement(
                TransformExpression(throwStmt.Expression!)
            ),

            ExpressionStatementSyntax exprStmt => new TsExpressionStatement(
                TransformExpression(exprStmt.Expression)
            ),

            LocalDeclarationStatementSyntax localDecl => TransformLocalDeclaration(localDecl),

            SwitchStatementSyntax switchStmt => Switches.TransformSwitchStatement(switchStmt),

            BlockSyntax block =>
            // Flatten single-statement blocks
            block.Statements.Count == 1
                ? TransformStatement(block.Statements[0])
                : throw new NotSupportedException(
                    "Multi-statement blocks should be handled by the caller"
                ),

            _ => UnsupportedStatement(statement),
        };
    }

    private TsStatement UnsupportedStatement(StatementSyntax statement)
    {
        ReportDiagnostic?.Invoke(new MetaSharpDiagnostic(
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

    public IReadOnlyList<TsStatement> TransformBody(
        BlockSyntax? block,
        ArrowExpressionClauseSyntax? arrow,
        bool isVoid = false
    )
    {
        if (arrow is not null)
        {
            var expr = TransformExpression(arrow.Expression);
            return isVoid
                ? [new TsExpressionStatement(expr)]
                : [new TsReturnStatement(expr)];
        }

        if (block is not null)
            return block.Statements.Select(TransformStatement).ToList();

        return [];
    }

    private TsIfStatement TransformIf(IfStatementSyntax ifStmt)
    {
        var condition = TransformExpression(ifStmt.Condition);
        var thenBody = TransformStatementBody(ifStmt.Statement);
        var elseBody = ifStmt.Else?.Statement is not null
            ? TransformStatementBody(ifStmt.Else.Statement)
            : null;

        return new TsIfStatement(condition, thenBody, elseBody);
    }

    private IReadOnlyList<TsStatement> TransformStatementBody(StatementSyntax statement)
    {
        if (statement is BlockSyntax block)
            return block.Statements.Select(TransformStatement).ToList();

        return [TransformStatement(statement)];
    }

    private TsVariableDeclaration TransformLocalDeclaration(LocalDeclarationStatementSyntax decl)
    {
        var variable = decl.Declaration.Variables[0];
        var name = variable.Identifier.Text;
        var init = variable.Initializer?.Value is not null
            ? TransformExpression(variable.Initializer.Value)
            : new TsIdentifier("undefined");

        return new TsVariableDeclaration(
            name,
            init,
            decl.IsConst || decl.Modifiers.Any(SyntaxKind.ReadOnlyKeyword)
        );
    }

    // ─── Expressions ────────────────────────────────────────

    public TsExpression TransformExpression(ExpressionSyntax expression)
    {
        return expression switch
        {
            LiteralExpressionSyntax lit => LiteralHandler.Transform(lit),
            IdentifierNameSyntax id => Identifiers.Transform(id),

            BinaryExpressionSyntax bin => Operators.TransformBinary(bin),

            MemberAccessExpressionSyntax member => MemberAccess.Transform(member),

            InvocationExpressionSyntax invocation => Invocations.Transform(invocation),

            ObjectCreationExpressionSyntax creation => ObjectCreation.TransformObjectCreation(creation),
            ImplicitObjectCreationExpressionSyntax implicitCreation =>
                ObjectCreation.TransformImplicitObjectCreation(implicitCreation),

            InterpolatedStringExpressionSyntax interp => InterpolatedStrings.Transform(interp),

            ParenthesizedExpressionSyntax paren => new TsParenthesized(
                TransformExpression(paren.Expression)
            ),

            ConditionalExpressionSyntax cond => new TsConditionalExpression(
                TransformExpression(cond.Condition),
                TransformExpression(cond.WhenTrue),
                TransformExpression(cond.WhenFalse)
            ),

            CastExpressionSyntax cast => TransformExpression(cast.Expression),

            WithExpressionSyntax withExpr => ObjectCreation.TransformWithExpression(withExpr),

            ThrowExpressionSyntax throwExpr =>
            // In TS, throw is a statement, but we can wrap it in an IIFE for expression context
            new TsCallExpression(
                new TsArrowFunction(
                    [],
                    [new TsThrowStatement(TransformExpression(throwExpr.Expression))]
                ),
                []
            ),

            AwaitExpressionSyntax awaitExpr => new TsAwaitExpression(
                TransformExpression(awaitExpr.Expression)
            ),

            // this → this
            ThisExpressionSyntax => new TsIdentifier("this"),

            PrefixUnaryExpressionSyntax prefix => Operators.TransformPrefixUnary(prefix),

            // x?.Prop → x?.prop
            ConditionalAccessExpressionSyntax condAccess =>
                OptionalChaining.Transform(condAccess),

            SwitchExpressionSyntax switchExpr => Switches.TransformSwitchExpression(switchExpr),

            IsPatternExpressionSyntax isPattern => Patterns.TransformIsPattern(isPattern),

            // Lambda expressions
            SimpleLambdaExpressionSyntax simpleLambda => Lambdas.TransformSimpleLambda(simpleLambda),
            ParenthesizedLambdaExpressionSyntax parenLambda => Lambdas.TransformParenthesizedLambda(parenLambda),

            AssignmentExpressionSyntax assign => Operators.TransformAssignment(assign),

            // Element access: arr[index] → arr[index]
            ElementAccessExpressionSyntax elemAccess => new TsElementAccess(
                TransformExpression(elemAccess.Expression),
                TransformExpression(elemAccess.ArgumentList.Arguments[0].Expression)
            ),

            // Generic type name as expression: OperationResult<Issue> → OperationResult
            GenericNameSyntax genericName => GenericNames.Transform(genericName),

            // C# 12 collection expression: [] → []
            CollectionExpressionSyntax collExpr => CollectionExpressions.Transform(collExpr),

            _ => Unsupported(expression, $"Expression '{expression.Kind()}' is not supported by the transpiler."),
        };
    }


    /// <summary>
    /// Resolves arguments (including named arguments) to positional order,
    /// filling in default values for skipped parameters.
    /// </summary>
    internal List<TsExpression> ResolveArguments(ArgumentListSyntax? argumentList, ExpressionSyntax callSite)
    {
        if (argumentList is null || argumentList.Arguments.Count == 0)
            return [];

        // Check if any argument is named
        var hasNamedArgs = argumentList.Arguments.Any(a => a.NameColon is not null);
        if (!hasNamedArgs)
        {
            // All positional — simple case
            return argumentList.Arguments.Select(a => TransformExpression(a.Expression)).ToList();
        }

        // Resolve the constructor/method symbol to get parameter order
        var symbolInfo = model.GetSymbolInfo(callSite);
        if (symbolInfo.Symbol is not IMethodSymbol methodSymbol)
        {
            // Fallback: just transform as-is
            return argumentList.Arguments.Select(a => TransformExpression(a.Expression)).ToList();
        }

        var parameters = methodSymbol.Parameters;
        var result = new TsExpression[parameters.Length];

        // Fill defaults
        for (var i = 0; i < parameters.Length; i++)
        {
            if (parameters[i].HasExplicitDefaultValue)
            {
                result[i] = parameters[i].ExplicitDefaultValue switch
                {
                    null => new TsLiteral("null"),
                    bool b => new TsLiteral(b ? "true" : "false"),
                    string s => new TsStringLiteral(s),
                    int n => new TsLiteral(n.ToString()),
                    _ => new TsLiteral(parameters[i].ExplicitDefaultValue?.ToString() ?? "undefined")
                };
            }
            else
            {
                result[i] = new TsIdentifier("undefined");
            }
        }

        // Place positional arguments first
        var positionalIndex = 0;
        foreach (var arg in argumentList.Arguments)
        {
            if (arg.NameColon is not null)
            {
                // Named argument — find the parameter index
                var paramName = arg.NameColon.Name.Identifier.Text;
                for (var i = 0; i < parameters.Length; i++)
                {
                    if (parameters[i].Name == paramName)
                    {
                        result[i] = TransformExpression(arg.Expression);
                        break;
                    }
                }
            }
            else
            {
                // Positional
                result[positionalIndex] = TransformExpression(arg.Expression);
                positionalIndex++;
            }
        }

        // Trim trailing defaults
        var lastNonDefault = result.Length - 1;
        while (lastNonDefault >= 0 && result[lastNonDefault] is TsLiteral or TsIdentifier { Name: "undefined" })
            lastNonDefault--;

        // Actually, we need to keep all args up to the last explicitly provided one
        // Find the last index that was explicitly provided
        var lastProvided = -1;
        for (var i = 0; i < parameters.Length; i++)
        {
            if (argumentList.Arguments.Any(a =>
                (a.NameColon is not null && a.NameColon.Name.Identifier.Text == parameters[i].Name)
                || (a.NameColon is null && argumentList.Arguments.IndexOf(a) == i)))
            {
                lastProvided = i;
            }
        }

        return result.Take(lastProvided + 1).ToList();
    }
}
