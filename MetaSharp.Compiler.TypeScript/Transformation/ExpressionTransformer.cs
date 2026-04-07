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

            // x is Type (old-style, before pattern matching) → x instanceof Type
            BinaryExpressionSyntax { OperatorToken.Text: "is" } isExpr =>
                new TsBinaryExpression(
                    TransformExpression(isExpr.Left),
                    "instanceof",
                    new TsIdentifier(isExpr.Right.ToString()) // keep PascalCase for type name
                ),

            BinaryExpressionSyntax bin => new TsBinaryExpression(
                TransformExpression(bin.Left),
                MapBinaryOperator(bin.OperatorToken.Text),
                TransformExpression(bin.Right)
            ),

            MemberAccessExpressionSyntax member => MemberAccess.Transform(member),

            InvocationExpressionSyntax invocation => Invocations.Transform(invocation),

            ObjectCreationExpressionSyntax creation => ObjectCreation.TransformObjectCreation(creation),
            ImplicitObjectCreationExpressionSyntax implicitCreation =>
                ObjectCreation.TransformImplicitObjectCreation(implicitCreation),

            InterpolatedStringExpressionSyntax interp => TransformInterpolatedString(interp),

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

            PrefixUnaryExpressionSyntax prefix => new TsUnaryExpression(
                prefix.OperatorToken.Text,
                TransformExpression(prefix.Operand)
            ),

            // x?.Prop → x?.prop
            ConditionalAccessExpressionSyntax condAccess =>
                TransformConditionalAccess(condAccess),

            SwitchExpressionSyntax switchExpr => Switches.TransformSwitchExpression(switchExpr),

            IsPatternExpressionSyntax isPattern => Patterns.TransformIsPattern(isPattern),

            // Lambda expressions
            SimpleLambdaExpressionSyntax simpleLambda => Lambdas.TransformSimpleLambda(simpleLambda),
            ParenthesizedLambdaExpressionSyntax parenLambda => Lambdas.TransformParenthesizedLambda(parenLambda),

            // Assignment: x = value → x = value
            AssignmentExpressionSyntax assign => new TsBinaryExpression(
                TransformExpression(assign.Left),
                MapAssignmentOperator(assign.OperatorToken.Text),
                TransformExpression(assign.Right)
            ),

            // Element access: arr[index] → arr[index]
            ElementAccessExpressionSyntax elemAccess => new TsElementAccess(
                TransformExpression(elemAccess.Expression),
                TransformExpression(elemAccess.ArgumentList.Arguments[0].Expression)
            ),

            // Generic type name as expression: OperationResult<Issue> → OperationResult
            GenericNameSyntax genericName => GenericNames.Transform(genericName),

            // C# 12 collection expression: [] → []
            CollectionExpressionSyntax collExpr => TransformCollectionExpression(collExpr),

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

    private TsExpression TransformInterpolatedString(InterpolatedStringExpressionSyntax interp)
    {
        var quasis = new List<string>();
        var expressions = new List<TsExpression>();
        var current = "";

        foreach (var content in interp.Contents)
        {
            switch (content)
            {
                case InterpolatedStringTextSyntax text:
                    current += text.TextToken.ValueText;
                    break;

                case InterpolationSyntax interpolation:
                    quasis.Add(current);
                    current = "";
                    expressions.Add(TransformExpression(interpolation.Expression));
                    break;
            }
        }

        quasis.Add(current);
        return new TsTemplateLiteral(quasis, expressions);
    }



    /// <summary>
    /// Transforms x?.Prop or x?.Method() into TS optional chaining.
    /// </summary>
    private TsExpression TransformConditionalAccess(ConditionalAccessExpressionSyntax condAccess)
    {
        var obj = TransformExpression(condAccess.Expression);

        return condAccess.WhenNotNull switch
        {
            // x?.Prop → x?.prop
            MemberBindingExpressionSyntax memberBinding =>
                new TsPropertyAccess(
                    new TsIdentifier(GetExpressionText(obj) + "?"),
                    TypeScriptNaming.ToCamelCase(memberBinding.Name.Identifier.Text)
                ),

            // x?.Method() → x?.method()
            InvocationExpressionSyntax { Expression: MemberBindingExpressionSyntax binding } invocation =>
                new TsCallExpression(
                    new TsPropertyAccess(
                        new TsIdentifier(GetExpressionText(obj) + "?"),
                        TypeScriptNaming.ToCamelCase(binding.Name.Identifier.Text)
                    ),
                    invocation.ArgumentList.Arguments
                        .Select(a => TransformExpression(a.Expression))
                        .ToList()
                ),

            _ => obj // fallback
        };
    }

    /// <summary>
    /// Gets a simple text representation of an expression for optional chaining composition.
    /// </summary>
    private static string GetExpressionText(TsExpression expr) => expr switch
    {
        TsIdentifier id => id.Name,
        TsPropertyAccess access => GetExpressionText(access.Object) + "." + access.Property,
        _ => "unknown"
    };

    /// <summary>
    /// Expands an [Emit] expression, replacing $0, $1, etc. with the transformed arguments.
    /// Returns a TsLiteral with the expanded raw JS.
    /// </summary>

    private static string MapBinaryOperator(string op) => op switch
    {
        "==" => "===",
        "!=" => "!==",
        _ => op
    };

    private static string MapAssignmentOperator(string op) => op switch
    {
        "??=" => "??=",
        _ => op
    };


    // ─── Collection expressions ─────────────────────────────

    private TsExpression TransformCollectionExpression(CollectionExpressionSyntax collExpr)
    {
        // Check target type to distinguish Set vs Array
        var convertedType = model.GetTypeInfo(collExpr).ConvertedType;
        var isSetType = convertedType is INamedTypeSymbol named
            && named.Name is "HashSet" or "ISet" or "SortedSet";

        if (collExpr.Elements.Count == 0)
            return isSetType
                ? new TsNewExpression(new TsIdentifier("HashSet"), [])
                : new TsLiteral("[]");

        var elements = collExpr.Elements
            .OfType<ExpressionElementSyntax>()
            .Select(e => TransformExpression(e.Expression))
            .ToList();

        if (isSetType)
            return new TsNewExpression(new TsIdentifier("HashSet"), [new TsArrayLiteral(elements)]);

        return new TsCallExpression(
            new TsPropertyAccess(new TsIdentifier("Array"), "of"),
            elements);
    }

}
