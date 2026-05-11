using Metano.Compiler.Diagnostics;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.Extraction;

/// <summary>
/// Walks a Roslyn lambda body and lowers it into the
/// <see cref="IrExprTreeNode"/> shape the runtime
/// <c>QueryableMeta</c> consumes (Phase B / #31).
///
/// <para>
/// <b>Reach today.</b> The diagnostic fires from two trigger
/// detection paths in <see cref="IrExpressionExtractor"/>:
/// <list type="bullet">
///   <item><c>TryCaptureQueryableMeta</c>, gated by
///   <c>IrLinqMapping.TryResolve</c>, covers stages inside a
///   recognised <c>System.Linq.Enumerable</c> /
///   <c>System.Linq.Queryable</c> chain.</item>
///   <item><c>ReportQueryableDiagnosticsForExplicitOptIn</c> (#218)
///   covers <em>any</em> invocation whose lambda argument's
///   matching parameter carries <c>[Queryable]</c> or is typed
///   <c>Expression&lt;Func&lt;…&gt;&gt;</c> — independent of
///   whether the call participates in a LINQ chain. The walker is
///   invoked purely for the MS0024 side-effect; the resulting meta
///   is discarded because the surrounding IR has no slot for it on
///   non-chain calls.</item>
/// </list>
/// </para>
/// <para>
/// Triggered for stages whose receiver is <c>IQueryable&lt;T&gt;</c>,
/// whose method or argument parameter carries
/// <c>[Queryable]</c>, or whose parameter type is
/// <c>System.Linq.Expressions.Expression&lt;Func&lt;…&gt;&gt;</c>. The
/// walker covers the subset listed in <see cref="SupportedKinds"/> —
/// param / capture / literal / member / call / binary / unary /
/// conditional, plus object-creation (<c>new T(args) { … }</c>) and
/// nested lambdas added in #206. Unsupported nodes surface as a
/// <c>null</c> result; the caller drops the queryable meta and the
/// lambda flows as a plain closure. The MS0024 hard error path is
/// reserved for a follow-up.
/// </para>
/// </summary>
internal sealed class IrExpressionTreeExtractor
{
    /// <summary>
    /// Human-readable list of node kinds the walker covers. Kept as
    /// a single source of truth so the MS0024 message and any
    /// future documentation cannot drift from the actual
    /// <see cref="Walk"/> switch.
    /// </summary>
    internal const string SupportedKinds =
        "param, capture, literal, member, call, binary, unary, conditional, new, lambda";

    private readonly SemanticModel _semantic;
    private readonly IrTypeOriginResolver? _originResolver;
    private readonly Metano.Annotations.TargetLanguage? _target;
    private readonly IrExpressionExtractor _valueExtractor;
    private readonly bool _isExplicitOptIn;

    private readonly HashSet<ISymbol> _lambdaParams = new(SymbolEqualityComparer.Default);
    private readonly Dictionary<ISymbol, IrQueryableCapture> _captures = new(
        SymbolEqualityComparer.Default
    );
    private readonly List<IrQueryableCapture> _captureOrder = new();
    private bool _failed;

    public IrExpressionTreeExtractor(
        SemanticModel semanticModel,
        IrTypeOriginResolver? originResolver,
        Metano.Annotations.TargetLanguage? target,
        IrExpressionExtractor valueExtractor,
        bool isExplicitOptIn = false
    )
    {
        _semantic = semanticModel;
        _originResolver = originResolver;
        _target = target;
        _valueExtractor = valueExtractor;
        _isExplicitOptIn = isExplicitOptIn;
    }

    /// <summary>
    /// Lowers <paramref name="lambda"/> into a <see cref="IrQueryableMeta"/>.
    /// Returns null when the body uses syntax outside the MVP subset.
    /// </summary>
    public IrQueryableMeta? TryExtract(LambdaExpressionSyntax lambda)
    {
        foreach (var paramSyntax in EnumerateParameters(lambda))
        {
            if (_semantic.GetDeclaredSymbol(paramSyntax) is IParameterSymbol p)
                _lambdaParams.Add(p);
        }

        if (lambda.Body is not ExpressionSyntax bodyExpr)
            return null;

        var tree = Walk(bodyExpr);
        if (_failed || tree is null)
            return null;

        return new IrQueryableMeta(tree, _captureOrder.Count == 0 ? null : _captureOrder.ToList());
    }

    private static IEnumerable<ParameterSyntax> EnumerateParameters(
        LambdaExpressionSyntax lambda
    ) =>
        lambda switch
        {
            SimpleLambdaExpressionSyntax simple => new[] { simple.Parameter },
            ParenthesizedLambdaExpressionSyntax paren => paren.ParameterList.Parameters,
            _ => Enumerable.Empty<ParameterSyntax>(),
        };

    private IrExprTreeNode? Walk(ExpressionSyntax node)
    {
        if (_failed)
            return null;

        switch (node)
        {
            case ParenthesizedExpressionSyntax paren:
                return Walk(paren.Expression);

            case LiteralExpressionSyntax lit:
                return new IrExprLiteral(_semantic.GetConstantValue(lit).Value, MapType(lit));

            case IdentifierNameSyntax id:
                return WalkIdentifier(id);

            case MemberAccessExpressionSyntax member:
                return WalkMemberAccess(member);

            case InvocationExpressionSyntax inv:
                return WalkInvocation(inv);

            case BinaryExpressionSyntax bin:
                return WalkBinary(bin);

            case PrefixUnaryExpressionSyntax unary:
                return WalkUnary(unary);

            case ConditionalExpressionSyntax cond:
                return WalkConditional(cond);

            case ObjectCreationExpressionSyntax objCreation:
                return WalkObjectCreation(objCreation);

            case LambdaExpressionSyntax nestedLambda:
                return WalkNestedLambda(nestedLambda);

            default:
                Bail(node, node.Kind().ToString());
                return null;
        }
    }

    /// <summary>
    /// Marks the walker as failed at <paramref name="offending"/>.
    /// When the caller opted in <em>explicitly</em>
    /// (<c>[Queryable]</c> on the method / parameter, or an
    /// <c>Expression&lt;Func&lt;…&gt;&gt;</c> parameter), publish an
    /// MS0024 diagnostic so the user learns the body fell outside
    /// the MVP subset instead of silently losing the queryable
    /// meta. Implicit triggers (<c>IQueryable&lt;T&gt;</c> receiver
    /// alone) keep the silent bail.
    /// <para>
    /// Only the <em>first</em> unsupported node is reported per
    /// lambda — subsequent bails short-circuit on <c>_failed</c>
    /// without re-entering the diagnostic path.
    /// </para>
    /// </summary>
    private void Bail(SyntaxNode offending, string unsupportedKind)
    {
        if (_failed)
            return;
        _failed = true;
        if (!_isExplicitOptIn)
            return;
        QueryableExtractionDiagnostics.Report(
            new MetanoDiagnostic(
                MetanoDiagnosticSeverity.Error,
                DiagnosticCodes.UnsupportedQueryableBody,
                FormatUnsupportedBodyMessage(unsupportedKind),
                offending.GetLocation()
            )
        );
    }

    private static string FormatUnsupportedBodyMessage(string unsupportedKind) =>
        $"Queryable lambda body uses unsupported syntax '{unsupportedKind}'. "
        + $"The expression-tree walker supports: {SupportedKinds}. "
        + "Either refactor the body into that subset, or remove the [Queryable] "
        + "attribute / change the parameter type away from Expression<Func<…>> so "
        + "the closure runs without a captured tree.";

    private IrExprTreeNode? WalkUnary(PrefixUnaryExpressionSyntax unary)
    {
        var operand = Walk(unary.Operand);
        if (operand is null)
            return null;
        return new IrExprUnary(unary.OperatorToken.ValueText, operand);
    }

    private IrExprTreeNode? WalkConditional(ConditionalExpressionSyntax cond)
    {
        var condition = Walk(cond.Condition);
        var whenTrue = Walk(cond.WhenTrue);
        var whenFalse = Walk(cond.WhenFalse);
        if (condition is null || whenTrue is null || whenFalse is null)
            return null;
        return new IrExprConditional(condition, whenTrue, whenFalse);
    }

    private IrExprTreeNode? WalkObjectCreation(ObjectCreationExpressionSyntax node)
    {
        var args = new List<IrExprTreeNode>(node.ArgumentList?.Arguments.Count ?? 0);
        if (node.ArgumentList is not null)
        {
            foreach (var arg in node.ArgumentList.Arguments)
            {
                var lowered = Walk(arg.Expression);
                if (lowered is null)
                    return null;
                args.Add(lowered);
            }
        }

        List<IrExprNewInitializer>? initializers = null;
        if (node.Initializer is not null)
        {
            initializers = new List<IrExprNewInitializer>(node.Initializer.Expressions.Count);
            foreach (var entry in node.Initializer.Expressions)
            {
                // Only support `Member = value`; other initializer forms
                // (collection / indexed / dictionary) escape the MVP
                // shape — surface as unsupported so the lambda falls
                // back to the closure path.
                if (entry is not AssignmentExpressionSyntax assignment)
                {
                    Bail(entry, entry.Kind().ToString());
                    return null;
                }
                if (assignment.Left is not IdentifierNameSyntax memberName)
                {
                    Bail(assignment.Left, assignment.Left.Kind().ToString());
                    return null;
                }
                var value = Walk(assignment.Right);
                if (value is null)
                    return null;
                initializers.Add(new IrExprNewInitializer(memberName.Identifier.ValueText, value));
            }
        }

        return new IrExprNew(MapType(node), args, initializers);
    }

    private IrExprTreeNode? WalkNestedLambda(LambdaExpressionSyntax lambda)
    {
        // Nested lambda body must itself be an expression — statement
        // bodies fall outside the MVP shape and bail like any other
        // unsupported node would.
        if (lambda.Body is not ExpressionSyntax bodyExpr)
        {
            Bail(lambda, lambda.Kind().ToString());
            return null;
        }

        var paramSymbols = new List<IParameterSymbol>();
        var paramNodes = new List<IrExprLambdaParam>();
        foreach (var paramSyntax in EnumerateParameters(lambda))
        {
            if (_semantic.GetDeclaredSymbol(paramSyntax) is not IParameterSymbol symbol)
            {
                Bail(paramSyntax, paramSyntax.Kind().ToString());
                return null;
            }
            paramSymbols.Add(symbol);
            paramNodes.Add(new IrExprLambdaParam(symbol.Name, MapTypeSymbol(symbol.Type)));
        }

        // Bind inner params before walking the body so identifier
        // resolution treats them as `param` nodes rather than
        // closure-captured locals. Roll back on exit so a sibling
        // nested lambda cannot leak the binding.
        foreach (var symbol in paramSymbols)
            _lambdaParams.Add(symbol);

        try
        {
            var body = Walk(bodyExpr);
            if (body is null)
                return null;
            return new IrExprLambda(paramNodes, body);
        }
        finally
        {
            foreach (var symbol in paramSymbols)
                _lambdaParams.Remove(symbol);
        }
    }

    private IrExprTreeNode? WalkIdentifier(IdentifierNameSyntax id)
    {
        var symbol = _semantic.GetSymbolInfo(id).Symbol;
        if (symbol is null)
        {
            Bail(id, "UnresolvedIdentifier");
            return null;
        }

        if (symbol is IParameterSymbol p && _lambdaParams.Contains(p))
            return new IrExprParam(p.Name, MapType(id));

        if (TryFoldConstant(id, out var folded))
            return folded;

        return CaptureSymbol(id, symbol);
    }

    private IrExprTreeNode? WalkMemberAccess(MemberAccessExpressionSyntax member)
    {
        if (TryFoldConstant(member, out var folded))
            return folded;

        var target = Walk(member.Expression);
        if (target is null)
            return null;
        return new IrExprMember(target, member.Name.Identifier.ValueText);
    }

    private IrExprTreeNode? WalkInvocation(InvocationExpressionSyntax inv)
    {
        IrExprTreeNode? target = null;
        string method;

        switch (inv.Expression)
        {
            case MemberAccessExpressionSyntax memberAccess:
                target = Walk(memberAccess.Expression);
                if (target is null)
                    return null;
                method = memberAccess.Name.Identifier.ValueText;
                break;
            case IdentifierNameSyntax idName:
                method = idName.Identifier.ValueText;
                break;
            default:
                Bail(inv, inv.Expression.Kind().ToString());
                return null;
        }

        var args = new List<IrExprTreeNode>(inv.ArgumentList.Arguments.Count);
        foreach (var arg in inv.ArgumentList.Arguments)
        {
            var a = Walk(arg.Expression);
            if (a is null)
                return null;
            args.Add(a);
        }

        return new IrExprCall(target, method, args);
    }

    private IrExprTreeNode? WalkBinary(BinaryExpressionSyntax bin)
    {
        var left = Walk(bin.Left);
        var right = Walk(bin.Right);
        if (left is null || right is null)
            return null;
        return new IrExprBinary(bin.OperatorToken.ValueText, left, right);
    }

    private bool TryFoldConstant(ExpressionSyntax expr, out IrExprTreeNode? folded)
    {
        var constant = _semantic.GetConstantValue(expr);
        if (constant.HasValue)
        {
            folded = new IrExprLiteral(constant.Value, MapType(expr));
            return true;
        }
        folded = null;
        return false;
    }

    private IrExprTreeNode CaptureSymbol(ExpressionSyntax syntax, ISymbol symbol)
    {
        var key = symbol.OriginalDefinition;
        if (!_captures.TryGetValue(key, out var existing))
        {
            var typeRef = MapType(syntax);
            var value = _valueExtractor.Extract(syntax);
            existing = new IrQueryableCapture(symbol.Name, value, typeRef);
            _captures[key] = existing;
            _captureOrder.Add(existing);
        }
        return new IrExprCapture(existing.Name, existing.Type);
    }

    /// <summary>
    /// Resolves the type of <paramref name="expr"/> for tree emission.
    /// Prefers <c>ConvertedType</c> so contextual conversions (e.g.
    /// <c>1</c> binding to a <c>long</c> parameter) keep the actual
    /// expression type the provider sees, falling back to the static
    /// <c>Type</c> when no conversion is in play.
    /// </summary>
    private IrTypeRef? MapType(ExpressionSyntax expr)
    {
        var info = _semantic.GetTypeInfo(expr);
        var t = info.ConvertedType ?? info.Type;
        return t is null ? null : IrTypeRefMapper.Map(t, _originResolver, _target);
    }

    /// <summary>
    /// Maps a Roslyn <see cref="ITypeSymbol"/> directly (no syntax node
    /// — used for lambda parameter types resolved off the declared
    /// symbol). Returns <c>null</c> when Roslyn could not bind a type,
    /// matching the syntax-flavoured <see cref="MapType"/> path.
    /// </summary>
    private IrTypeRef? MapTypeSymbol(ITypeSymbol? type) =>
        type is null ? null : IrTypeRefMapper.Map(type, _originResolver, _target);
}
