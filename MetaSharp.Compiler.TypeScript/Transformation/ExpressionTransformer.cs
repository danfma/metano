using MetaSharp.Diagnostics;
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

            SwitchStatementSyntax switchStmt => TransformSwitchStatement(switchStmt),

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
            LiteralExpressionSyntax lit => TransformLiteral(lit),
            IdentifierNameSyntax id => TransformIdentifier(id),

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

            MemberAccessExpressionSyntax member => TransformMemberAccess(member),

            InvocationExpressionSyntax invocation => TransformInvocation(invocation),

            ObjectCreationExpressionSyntax creation => TransformObjectCreation(creation),
            ImplicitObjectCreationExpressionSyntax implicitCreation =>
                TransformImplicitObjectCreation(implicitCreation),

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

            WithExpressionSyntax withExpr => TransformWithExpression(withExpr),

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

            SwitchExpressionSyntax switchExpr => TransformSwitchExpression(switchExpr),

            IsPatternExpressionSyntax isPattern => TransformIsPattern(isPattern),

            // Lambda expressions
            SimpleLambdaExpressionSyntax simpleLambda => TransformSimpleLambda(simpleLambda),
            ParenthesizedLambdaExpressionSyntax parenLambda => TransformParenthesizedLambda(parenLambda),

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
            GenericNameSyntax genericName => TransformGenericName(genericName),

            // C# 12 collection expression: [] → []
            CollectionExpressionSyntax collExpr => TransformCollectionExpression(collExpr),

            _ => Unsupported(expression, $"Expression '{expression.Kind()}' is not supported by the transpiler."),
        };
    }

    private TsExpression TransformIdentifier(IdentifierNameSyntax id)
    {
        var symbol = model.GetSymbolInfo(id).Symbol;

        // Type references → keep PascalCase (e.g., IssueStatus, Guid, UserId)
        if (symbol is INamedTypeSymbol or ITypeSymbol)
            return new TsIdentifier(id.Identifier.Text);

        var name = SymbolHelper.ToCamelCase(id.Identifier.Text);

        if (symbol is not null && symbol.ContainingType is not null)
        {
            // Instance members → this.name
            if (symbol is IPropertySymbol or IFieldSymbol && !symbol.IsStatic)
            {
                if (SelfParameterName is not null)
                    return new TsPropertyAccess(new TsIdentifier(SelfParameterName), name);
            }

            // Instance method → this.name
            if (symbol is IMethodSymbol { IsStatic: false, MethodKind: MethodKind.Ordinary })
            {
                if (SelfParameterName is not null)
                    return new TsPropertyAccess(new TsIdentifier(SelfParameterName), name);
            }

            // Static method/property of the same class → ClassName.name
            if (symbol.IsStatic && symbol is IMethodSymbol or IPropertySymbol or IFieldSymbol)
            {
                return new TsPropertyAccess(new TsIdentifier(symbol.ContainingType.Name), name);
            }
        }

        return new TsIdentifier(name);
    }

    private TsExpression TransformLiteral(LiteralExpressionSyntax lit)
    {
        return lit.Kind() switch
        {
            SyntaxKind.StringLiteralExpression => new TsStringLiteral(lit.Token.ValueText),
            SyntaxKind.TrueLiteralExpression => new TsLiteral("true"),
            SyntaxKind.FalseLiteralExpression => new TsLiteral("false"),
            SyntaxKind.NullLiteralExpression => new TsLiteral("null"),
            SyntaxKind.DefaultLiteralExpression => new TsLiteral("null"),
            // Numeric: strip suffixes (m, L, f, d)
            _ => new TsLiteral(lit.Token.ValueText),
        };
    }

    private TsExpression TransformMemberAccess(MemberAccessExpressionSyntax member)
    {
        // Check for BCL mappings
        var symbol = model.GetSymbolInfo(member).Symbol;
        if (symbol is not null)
        {
            var mapped = BclMapper.TryMap(symbol, member, this);
            if (mapped is not null)
                return mapped;
        }

        var obj = TransformExpression(member.Expression);

        // Enum members and constants → keep PascalCase
        var memberName = symbol is IFieldSymbol { ContainingType.TypeKind: TypeKind.Enum }
            ? member.Name.Identifier.Text
            : SymbolHelper.ToCamelCase(member.Name.Identifier.Text);

        return new TsPropertyAccess(obj, memberName);
    }

    private TsExpression TransformInvocation(InvocationExpressionSyntax invocation)
    {
        // Check for BCL method mappings
        var symbol = model.GetSymbolInfo(invocation).Symbol;
        if (symbol is IMethodSymbol methodSymbol)
        {
            // [Emit] — inline JS expression with $0, $1 placeholders
            var emit = SymbolHelper.GetEmit(methodSymbol);
            if (emit is not null)
            {
                var emitArgs = invocation.ArgumentList.Arguments
                    .Select(a => TransformExpression(a.Expression))
                    .ToList();
                return ExpandEmit(emit, emitArgs);
            }

            var mapped = BclMapper.TryMapMethod(methodSymbol, invocation, this);
            if (mapped is not null)
                return mapped;
        }

        var callee = TransformExpression(invocation.Expression);
        var args = invocation
            .ArgumentList.Arguments.Select(a => TransformExpression(a.Expression))
            .ToList();

        return new TsCallExpression(callee, args);
    }

    private TsExpression TransformObjectCreation(ObjectCreationExpressionSyntax creation)
    {
        var typeInfo = model.GetTypeInfo(creation);
        var type = typeInfo.Type;

        // Inline wrapper structs are emitted as companion objects, not classes:
        // new UserId(v) -> UserId.create(v)
        if (type is INamedTypeSymbol inlineWrapperType && SymbolHelper.HasInlineWrapper(inlineWrapperType))
        {
            var args = ResolveArguments(creation.ArgumentList, creation);
            var tsTypeName = SymbolHelper.GetNameOverride(inlineWrapperType) ?? inlineWrapperType.Name;
            return new TsCallExpression(
                new TsPropertyAccess(new TsIdentifier(tsTypeName), "create"),
                args
            );
        }

        // Record struct/record → new Type(args)
        if (type is INamedTypeSymbol { IsRecord: true } recordType)
            return CreateNewFromArgs(recordType, creation.ArgumentList);

        // Exception → new ErrorSubclass(...) or new Error(...)
        if (IsExceptionType(type))
        {
            var args = ResolveArguments(creation.ArgumentList, creation);
            var errorName = type is INamedTypeSymbol named && SymbolHelper.IsTranspilable(named, AssemblyWideTranspile, CurrentAssembly)
                ? named.Name
                : "Error";
            return new TsNewExpression(new TsIdentifier(errorName), args);
        }

        // Default: new Type(args) — resolve named arguments to positional
        var ctorArgs = ResolveArguments(creation.ArgumentList, creation);
        var typeName = type is INamedTypeSymbol nt ? BuildQualifiedTypeName(nt) : (type?.Name ?? "Object");
        return new TsNewExpression(new TsIdentifier(typeName), ctorArgs);
    }

    /// <summary>
    /// Builds the TS-side qualified name for a type. Nested types become `Outer.Inner`.
    /// </summary>
    private static string BuildQualifiedTypeName(INamedTypeSymbol type)
    {
        if (type.ContainingType is null) return type.Name;
        var parts = new List<string> { type.Name };
        var current = type.ContainingType;
        while (current is not null)
        {
            parts.Insert(0, current.Name);
            current = current.ContainingType;
        }
        return string.Join(".", parts);
    }

    /// <summary>
    /// Resolves arguments (including named arguments) to positional order,
    /// filling in default values for skipped parameters.
    /// </summary>
    private List<TsExpression> ResolveArguments(ArgumentListSyntax? argumentList, ExpressionSyntax callSite)
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

    private TsExpression TransformImplicitObjectCreation(
        ImplicitObjectCreationExpressionSyntax creation
    )
    {
        var typeInfo = model.GetTypeInfo(creation);
        var type = typeInfo.ConvertedType;

        if (type is INamedTypeSymbol inlineWrapperType && SymbolHelper.HasInlineWrapper(inlineWrapperType))
        {
            var inlineArgs = creation
                .ArgumentList.Arguments.Select(a => TransformExpression(a.Expression))
                .ToList();
            var tsTypeName = SymbolHelper.GetNameOverride(inlineWrapperType) ?? inlineWrapperType.Name;
            return new TsCallExpression(
                new TsPropertyAccess(new TsIdentifier(tsTypeName), "create"),
                inlineArgs
            );
        }

        if (type is INamedTypeSymbol { IsRecord: true } recordType)
            return CreateNewFromArgs(recordType, creation.ArgumentList);

        var args = creation
            .ArgumentList.Arguments.Select(a => TransformExpression(a.Expression))
            .ToList();

        return new TsNewExpression(new TsIdentifier(type?.Name ?? "Object"), args);
    }

    private TsNewExpression CreateNewFromArgs(
        INamedTypeSymbol recordType,
        ArgumentListSyntax? argumentList
    )
    {
        // Use ResolveArguments for named argument support
        if (argumentList is not null)
        {
            // Find the syntax node that triggered this (for symbol resolution)
            var parentExpr = argumentList.Parent as ExpressionSyntax;
            if (parentExpr is not null)
            {
                var args = ResolveArguments(argumentList, parentExpr);
                return new TsNewExpression(new TsIdentifier(recordType.Name), args);
            }
        }

        var simpleArgs = argumentList?.Arguments.Select(a => TransformExpression(a.Expression)).ToList() ?? [];
        return new TsNewExpression(new TsIdentifier(recordType.Name), simpleArgs);
    }

    private TsExpression TransformWithExpression(WithExpressionSyntax withExpr)
    {
        // record with { X = expr } → source.with({ x: expr })
        var source = TransformExpression(withExpr.Expression);
        var properties = new List<TsObjectProperty>();

        foreach (var assignment in withExpr.Initializer.Expressions)
        {
            if (assignment is AssignmentExpressionSyntax assign)
            {
                var name = SymbolHelper.ToCamelCase(assign.Left.ToString());
                var value = TransformExpression(assign.Right);
                properties.Add(new TsObjectProperty(name, value));
            }
        }

        return new TsCallExpression(
            new TsPropertyAccess(source, "with"),
            [new TsObjectLiteral(properties)]
        );
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
                    SymbolHelper.ToCamelCase(memberBinding.Name.Identifier.Text)
                ),

            // x?.Method() → x?.method()
            InvocationExpressionSyntax { Expression: MemberBindingExpressionSyntax binding } invocation =>
                new TsCallExpression(
                    new TsPropertyAccess(
                        new TsIdentifier(GetExpressionText(obj) + "?"),
                        SymbolHelper.ToCamelCase(binding.Name.Identifier.Text)
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
    // ─── Switch ──────────────────────────────────────────────

    private TsSwitchStatement TransformSwitchStatement(SwitchStatementSyntax switchStmt)
    {
        var discriminant = TransformExpression(switchStmt.Expression);
        var cases = new List<TsSwitchCase>();

        foreach (var section in switchStmt.Sections)
        {
            var body = section.Statements.Select(TransformStatement).ToList();

            foreach (var label in section.Labels)
            {
                switch (label)
                {
                    case CaseSwitchLabelSyntax caseLabel:
                        cases.Add(new TsSwitchCase(TransformExpression(caseLabel.Value), body));
                        break;
                    case DefaultSwitchLabelSyntax:
                        cases.Add(new TsSwitchCase(null, body));
                        break;
                    case CasePatternSwitchLabelSyntax patternLabel:
                        // Pattern-based case → convert pattern to condition and use if-like logic
                        // For now, fall through to default
                        cases.Add(new TsSwitchCase(null, body));
                        break;
                }
            }
        }

        return new TsSwitchStatement(discriminant, cases);
    }

    private TsExpression TransformSwitchExpression(SwitchExpressionSyntax switchExpr)
    {
        var governing = TransformExpression(switchExpr.GoverningExpression);
        var arms = switchExpr.Arms.ToList();

        // Build a ternary chain: cond1 ? val1 : cond2 ? val2 : default
        return BuildTernaryChain(governing, arms, 0);
    }

    private TsExpression BuildTernaryChain(TsExpression governing, List<SwitchExpressionArmSyntax> arms, int index)
    {
        if (index >= arms.Count)
            return new TsIdentifier("undefined");

        var arm = arms[index];
        var value = TransformExpression(arm.Expression);

        // Discard pattern (_) → this is the default/else
        if (arm.Pattern is DiscardPatternSyntax)
            return value;

        var condition = TransformPatternToCondition(governing, arm.Pattern);

        // Add when clause if present
        if (arm.WhenClause is not null)
        {
            var whenExpr = TransformExpression(arm.WhenClause.Condition);
            condition = new TsBinaryExpression(condition, "&&", whenExpr);
        }

        var rest = BuildTernaryChain(governing, arms, index + 1);
        return new TsConditionalExpression(condition, value, rest);
    }

    // ─── Is Pattern ─────────────────────────────────────────

    private TsExpression TransformIsPattern(IsPatternExpressionSyntax isPattern)
    {
        var expr = TransformExpression(isPattern.Expression);
        return TransformPatternToCondition(expr, isPattern.Pattern);
    }

    private TsExpression TransformPatternToCondition(TsExpression expr, PatternSyntax pattern)
    {
        return pattern switch
        {
            // x is null → x === null
            ConstantPatternSyntax { Expression: LiteralExpressionSyntax literal }
                when literal.IsKind(SyntaxKind.NullLiteralExpression) =>
                new TsBinaryExpression(expr, "===", new TsLiteral("null")),

            // x is "value" or x is 42
            ConstantPatternSyntax constant =>
                new TsBinaryExpression(expr, "===", TransformExpression(constant.Expression)),

            // x is not pattern → !(condition)
            UnaryPatternSyntax { OperatorToken.Text: "not" } unary =>
                new TsUnaryExpression("!", new TsParenthesized(TransformPatternToCondition(expr, unary.Pattern))),

            // x is Type → x instanceof Type
            DeclarationPatternSyntax declaration =>
                TransformTypePattern(expr, declaration.Type),

            // x is Type (without variable)
            TypePatternSyntax typePattern =>
                TransformTypePattern(expr, typePattern.Type),

            // x is > 0
            RelationalPatternSyntax relational =>
                new TsBinaryExpression(expr, MapBinaryOperator(relational.OperatorToken.Text),
                    TransformExpression(relational.Expression)),

            // x is >= 0 and < 100
            BinaryPatternSyntax binary =>
                new TsBinaryExpression(
                    TransformPatternToCondition(expr, binary.Left),
                    binary.OperatorToken.Text == "and" ? "&&" : "||",
                    TransformPatternToCondition(expr, binary.Right)
                ),

            // x is { Prop: value }
            RecursivePatternSyntax recursive when recursive.PropertyPatternClause is not null =>
                TransformPropertyPattern(expr, recursive),

            // Discard _
            DiscardPatternSyntax => new TsLiteral("true"),

            _ => new TsLiteral($"true /* unsupported pattern: {pattern.Kind()} */")
        };
    }

    private TsExpression TransformTypePattern(TsExpression expr, TypeSyntax typeSyntax)
    {
        var typeInfo = model.GetTypeInfo(typeSyntax);
        var type = typeInfo.Type;

        if (type is null)
            return new TsBinaryExpression(expr, "instanceof", new TsIdentifier(typeSyntax.ToString()));

        // Primitive type checks → typeof
        return type.SpecialType switch
        {
            SpecialType.System_Int32 or SpecialType.System_Int64 or SpecialType.System_Double
                or SpecialType.System_Single or SpecialType.System_Decimal =>
                new TsBinaryExpression(new TsUnaryExpression("typeof ", expr), "===",
                    new TsStringLiteral("number")),

            SpecialType.System_String =>
                new TsBinaryExpression(new TsUnaryExpression("typeof ", expr), "===",
                    new TsStringLiteral("string")),

            SpecialType.System_Boolean =>
                new TsBinaryExpression(new TsUnaryExpression("typeof ", expr), "===",
                    new TsStringLiteral("boolean")),

            // Class/struct → instanceof
            _ => new TsBinaryExpression(expr, "instanceof", new TsIdentifier(type.Name))
        };
    }

    private TsExpression TransformPropertyPattern(TsExpression expr, RecursivePatternSyntax recursive)
    {
        TsExpression? result = null;

        foreach (var subpattern in recursive.PropertyPatternClause!.Subpatterns)
        {
            var propName = SymbolHelper.ToCamelCase(subpattern.NameColon!.Name.Identifier.Text);
            var propAccess = new TsPropertyAccess(expr, propName);
            var condition = TransformPatternToCondition(propAccess, subpattern.Pattern);

            result = result is null ? condition : new TsBinaryExpression(result, "&&", condition);
        }

        // Add type check if recursive pattern has a type
        if (recursive.Type is not null)
        {
            var typeCheck = TransformTypePattern(expr, recursive.Type);
            result = result is null ? typeCheck : new TsBinaryExpression(typeCheck, "&&", result);
        }

        return result ?? new TsLiteral("true");
    }

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

    private TsExpression TransformGenericName(GenericNameSyntax genericName)
    {
        var symbol = model.GetSymbolInfo(genericName).Symbol;

        // If it resolves to a type, keep PascalCase
        if (symbol is INamedTypeSymbol)
            return new TsIdentifier(genericName.Identifier.Text);

        // Check if the semantic model can resolve to a type via SymbolInfo
        var typeInfo = model.GetTypeInfo(genericName);
        if (typeInfo.Type is not null)
            return new TsIdentifier(typeInfo.Type.Name);

        // Fallback — use the identifier text as-is (PascalCase for types)
        return new TsIdentifier(genericName.Identifier.Text);
    }

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

    // ─── Lambda expressions ─────────────────────────────────

    private TsArrowFunction TransformSimpleLambda(SimpleLambdaExpressionSyntax lambda)
    {
        var param = TransformLambdaParameter(lambda.Parameter);
        var body = TransformLambdaBody(lambda.Body);
        var isAsync = lambda.AsyncKeyword.IsKind(SyntaxKind.AsyncKeyword);
        return new TsArrowFunction([param], body, isAsync);
    }

    private TsArrowFunction TransformParenthesizedLambda(ParenthesizedLambdaExpressionSyntax lambda)
    {
        var parameters = lambda.ParameterList.Parameters
            .Select(TransformLambdaParameter)
            .ToList();
        var body = TransformLambdaBody(lambda.Body);
        var isAsync = lambda.AsyncKeyword.IsKind(SyntaxKind.AsyncKeyword);
        return new TsArrowFunction(parameters, body, isAsync);
    }

    private TsParameter TransformLambdaParameter(ParameterSyntax param)
    {
        var name = SymbolHelper.ToCamelCase(param.Identifier.Text);

        // Try to resolve the type from the semantic model
        var symbol = model.GetDeclaredSymbol(param);
        TsType type;
        if (symbol is IParameterSymbol paramSymbol)
        {
            type = TypeMapper.Map(paramSymbol.Type);
        }
        else if (param.Type is not null)
        {
            var typeInfo = model.GetTypeInfo(param.Type);
            type = typeInfo.Type is not null ? TypeMapper.Map(typeInfo.Type) : new TsAnyType();
        }
        else
        {
            type = new TsAnyType();
        }

        return new TsParameter(name, type);
    }

    private IReadOnlyList<TsStatement> TransformLambdaBody(CSharpSyntaxNode body)
    {
        return body switch
        {
            BlockSyntax block => block.Statements.Select(TransformStatement).ToList(),
            ExpressionSyntax expr => [new TsReturnStatement(TransformExpression(expr))],
            _ => [new TsReturnStatement(new TsIdentifier("undefined"))],
        };
    }

    // ─── Emit ───────────────────────────────────────────────

    private static TsExpression ExpandEmit(string template, IReadOnlyList<TsExpression> args)
    {
        var result = template;
        for (var i = 0; i < args.Count; i++)
        {
            var argText = args[i] switch
            {
                TsIdentifier id => id.Name,
                TsStringLiteral str => $"\"{str.Value}\"",
                TsLiteral lit => lit.Raw,
                TsPropertyAccess access => $"{ExprToString(access.Object)}.{access.Property}",
                _ => $"/* arg{i} */"
            };
            result = result.Replace($"${i}", argText);
        }

        return new TsLiteral(result);
    }

    private static string ExprToString(TsExpression expr) => expr switch
    {
        TsIdentifier id => id.Name,
        TsPropertyAccess access => $"{ExprToString(access.Object)}.{access.Property}",
        _ => "unknown"
    };

    private static bool IsExceptionType(ITypeSymbol? type)
    {
        var current = type;
        while (current is not null)
        {
            if (current.ToDisplayString() == "System.Exception")
                return true;
            current = current.BaseType;
        }

        return false;
    }
}
