using Metano.Compiler;
using Metano.Compiler.Diagnostics;
using Metano.TypeScript;
using Metano.TypeScript.AST;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Transformation;

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
    public Action<MetanoDiagnostic>? ReportDiagnostic { get; set; }

    /// <summary>
    /// Declarative <c>[MapMethod]</c>/<c>[MapProperty]</c> entries collected from the
    /// referenced assemblies. Consulted by <see cref="BclMapper"/> before falling back to
    /// its hardcoded lowering rules. Defaults to <see cref="DeclarativeMappingRegistry.Empty"/>
    /// when the transformer is created outside of a full <see cref="TypeScriptTransformContext"/>
    /// (e.g., in unit tests that exercise it directly).
    /// </summary>
    public DeclarativeMappingRegistry DeclarativeMappings { get; set; } =
        DeclarativeMappingRegistry.Empty;

    /// <summary>
    /// The Roslyn semantic model the expression transformer was created with.
    /// Exposed so extracted handlers (e.g., <see cref="PatternMatchingHandler"/>) can run
    /// their own type lookups against the same model.
    /// </summary>
    internal SemanticModel Model => model;

    private PatternMatchingHandler? _patterns;
    private PatternMatchingHandler Patterns => _patterns ??= new PatternMatchingHandler(this);

    private SwitchHandler? _switches;
    internal SwitchHandler Switches => _switches ??= new SwitchHandler(this, Patterns);

    private LambdaHandler? _lambdas;
    private LambdaHandler Lambdas => _lambdas ??= new LambdaHandler(this);

    private ObjectCreationHandler? _objectCreation;
    private ObjectCreationHandler ObjectCreation =>
        _objectCreation ??= new ObjectCreationHandler(this);

    private IdentifierHandler? _identifiers;
    private IdentifierHandler Identifiers => _identifiers ??= new IdentifierHandler(this);

    private GenericNameHandler? _genericNames;
    private GenericNameHandler GenericNames => _genericNames ??= new GenericNameHandler(this);

    private MemberAccessHandler? _memberAccess;
    private MemberAccessHandler MemberAccess => _memberAccess ??= new MemberAccessHandler(this);

    private InvocationHandler? _invocations;
    private InvocationHandler Invocations => _invocations ??= new InvocationHandler(this);

    private InterpolatedStringHandler? _interpolatedStrings;
    private InterpolatedStringHandler InterpolatedStrings =>
        _interpolatedStrings ??= new InterpolatedStringHandler(this);

    private OptionalChainingHandler? _optionalChaining;
    private OptionalChainingHandler OptionalChaining =>
        _optionalChaining ??= new OptionalChainingHandler(this);

    private CollectionExpressionHandler? _collectionExpressions;
    private CollectionExpressionHandler CollectionExpressions =>
        _collectionExpressions ??= new CollectionExpressionHandler(this);

    private OperatorHandler? _operators;
    private OperatorHandler Operators => _operators ??= new OperatorHandler(this);

    private StatementHandler? _statements;
    private StatementHandler Statements => _statements ??= new StatementHandler(this);

    private ThrowExpressionHandler? _throwExpressions;
    private ThrowExpressionHandler ThrowExpressions =>
        _throwExpressions ??= new ThrowExpressionHandler(this);

    private ArgumentResolver? _argumentResolver;
    internal ArgumentResolver ArgumentResolver => _argumentResolver ??= new ArgumentResolver(this);

    private TsExpression Unsupported(SyntaxNode node, string message)
    {
        ReportDiagnostic?.Invoke(
            new MetanoDiagnostic(
                MetanoDiagnosticSeverity.Warning,
                DiagnosticCodes.UnsupportedFeature,
                message,
                node.GetLocation()
            )
        );
        return new TsIdentifier($"/* unsupported: {node.Kind()} */");
    }

    // ─── Statements ─────────────────────────────────────────

    public TsStatement TransformStatement(StatementSyntax statement) =>
        Statements.Transform(statement);

    public IReadOnlyList<TsStatement> TransformBody(
        BlockSyntax? block,
        ArrowExpressionClauseSyntax? arrow,
        bool isVoid = false
    ) => Statements.TransformBody(block, arrow, isVoid);

    // ─── Expressions ────────────────────────────────────────

    public TsExpression TransformExpression(ExpressionSyntax expression)
    {
        return expression switch
        {
            LiteralExpressionSyntax lit => LiteralHandler.Transform(lit, Model),
            IdentifierNameSyntax id => Identifiers.Transform(id),

            BinaryExpressionSyntax bin => Operators.TransformBinary(bin),

            MemberAccessExpressionSyntax member => MemberAccess.Transform(member),

            InvocationExpressionSyntax invocation => Invocations.Transform(invocation),

            ObjectCreationExpressionSyntax creation => ObjectCreation.TransformObjectCreation(
                creation
            ),
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

            CastExpressionSyntax cast => TransformCast(cast),

            WithExpressionSyntax withExpr => ObjectCreation.TransformWithExpression(withExpr),

            ThrowExpressionSyntax throwExpr => ThrowExpressions.Transform(throwExpr),

            AwaitExpressionSyntax awaitExpr => new TsAwaitExpression(
                TransformExpression(awaitExpr.Expression)
            ),

            // this → this
            ThisExpressionSyntax => new TsIdentifier("this"),

            PrefixUnaryExpressionSyntax prefix => Operators.TransformPrefixUnary(prefix),
            PostfixUnaryExpressionSyntax postfix => Operators.TransformPostfixUnary(postfix),

            // x?.Prop → x?.prop
            ConditionalAccessExpressionSyntax condAccess => OptionalChaining.Transform(condAccess),

            SwitchExpressionSyntax switchExpr => Switches.TransformSwitchExpression(switchExpr),

            IsPatternExpressionSyntax isPattern => Patterns.TransformIsPattern(isPattern),

            // Lambda expressions
            SimpleLambdaExpressionSyntax simpleLambda => Lambdas.TransformSimpleLambda(
                simpleLambda
            ),
            ParenthesizedLambdaExpressionSyntax parenLambda => Lambdas.TransformParenthesizedLambda(
                parenLambda
            ),

            AssignmentExpressionSyntax assign => Operators.TransformAssignment(assign),

            // Element access: arr[index] → arr[index] for arrays/lists; for
            // Dictionary<K,V> (which lowers to JS Map), the indexer GET becomes
            // `receiver.get(key)` since JS Map doesn't expose bracket access.
            ElementAccessExpressionSyntax elemAccess => TransformElementAccess(elemAccess),

            // Generic type name as expression: OperationResult<Issue> → OperationResult
            GenericNameSyntax genericName => GenericNames.Transform(genericName),

            // C# 12 collection expression: [] → []
            CollectionExpressionSyntax collExpr => CollectionExpressions.Transform(collExpr),

            _ => Unsupported(
                expression,
                $"Expression '{expression.Kind()}' is not supported by the transpiler."
            ),
        };
    }

    /// <summary>
    /// Lowers a C# explicit cast expression. Most casts are erased (JS types are the
    /// same at runtime), but numeric type conversions that change representation need
    /// explicit code:
    /// <list type="bullet">
    ///   <item><c>(decimal)bigIntVar</c> → <c>new Decimal(bigIntVar.toString())</c></item>
    ///   <item><c>(BigInteger)decimalExpr</c> → <c>BigInt(expr.toFixed(0))</c></item>
    ///   <item><c>(int)decimalVar</c> / <c>(long)decimalVar</c> → <c>decimalVar.toNumber()</c></item>
    /// </list>
    /// </summary>
    private TsExpression TransformCast(CastExpressionSyntax cast)
    {
        var inner = TransformExpression(cast.Expression);
        var sourceInfo = Model.GetTypeInfo(cast.Expression);
        var sourceType = sourceInfo.Type;
        var targetType = Model.GetTypeInfo(cast).Type;

        if (sourceType is null || targetType is null)
            return inner;

        var sourceFull = sourceType.ToDisplayString();
        var targetFull = targetType.ToDisplayString();

        // BigInteger → decimal: new Decimal(value.toString())
        if (sourceFull == "System.Numerics.BigInteger" && targetFull == "decimal")
        {
            return new TsNewExpression(
                new TsIdentifier("Decimal"),
                [new TsCallExpression(new TsPropertyAccess(inner, "toString"), [])]
            );
        }

        // decimal → BigInteger: BigInt(value.toFixed(0))
        if (sourceFull == "decimal" && targetFull == "System.Numerics.BigInteger")
        {
            return new TsCallExpression(
                new TsIdentifier("BigInt"),
                [new TsCallExpression(new TsPropertyAccess(inner, "toFixed"), [new TsLiteral("0")])]
            );
        }

        // decimal → int/long/short/byte (any integer): value.toNumber()
        if (
            sourceFull == "decimal"
            && targetType.SpecialType
                is SpecialType.System_Int32
                    or SpecialType.System_Int64
                    or SpecialType.System_Int16
                    or SpecialType.System_Byte
                    or SpecialType.System_UInt32
                    or SpecialType.System_UInt64
        )
        {
            return new TsCallExpression(new TsPropertyAccess(inner, "toNumber"), []);
        }

        // int/long → BigInteger: BigInt(value)
        if (
            targetFull == "System.Numerics.BigInteger"
            && sourceType.SpecialType
                is SpecialType.System_Int32
                    or SpecialType.System_Int64
                    or SpecialType.System_Decimal
        )
        {
            return new TsCallExpression(new TsIdentifier("BigInt"), [inner]);
        }

        // Default: erase the cast (same-width numeric, reference casts, etc.)
        return inner;
    }

    /// <summary>
    /// Lowers a C# element access expression. Arrays / lists keep the bracket form
    /// (<c>arr[i]</c> stays valid in JS). Dictionary-family receivers — which lower
    /// to JS Map at the type level — get rewritten to <c>map.get(key)</c> since JS
    /// Map doesn't expose bracket access. Assignment to a dictionary indexer is
    /// handled separately in <see cref="OperatorHandler.TransformAssignment"/>.
    /// </summary>
    private TsExpression TransformElementAccess(ElementAccessExpressionSyntax elemAccess)
    {
        var receiverType = Model.GetTypeInfo(elemAccess.Expression).Type;
        var receiver = TransformExpression(elemAccess.Expression);
        var key = TransformExpression(elemAccess.ArgumentList.Arguments[0].Expression);

        if (IsDictionaryLike(receiverType))
            return new TsCallExpression(new TsPropertyAccess(receiver, "get"), [key]);

        return new TsElementAccess(receiver, key);
    }

    /// <summary>
    /// Returns true if <paramref name="type"/> is one of the dictionary types that
    /// lowers to JS Map (<c>Dictionary&lt;,&gt;</c>, <c>IDictionary&lt;,&gt;</c>,
    /// <c>IReadOnlyDictionary&lt;,&gt;</c>). Used by both element-access and
    /// assignment handlers to decide between bracket and method-call lowering.
    /// </summary>
    internal static bool IsDictionaryLike(ITypeSymbol? type)
    {
        if (type is not INamedTypeSymbol named)
            return false;
        var name = named.OriginalDefinition.ToDisplayString();
        return name
            is "System.Collections.Generic.Dictionary<TKey, TValue>"
                or "System.Collections.Generic.IDictionary<TKey, TValue>"
                or "System.Collections.Generic.IReadOnlyDictionary<TKey, TValue>";
    }
}
