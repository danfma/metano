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
/// Triggered for stages whose receiver is <c>IQueryable&lt;T&gt;</c>,
/// whose method or argument parameter carries
/// <c>[Queryable]</c>, or whose parameter type is
/// <c>System.Linq.Expressions.Expression&lt;Func&lt;…&gt;&gt;</c>. The
/// walker emits the MVP subset only — param / capture / literal /
/// member / call / binary / unary / conditional. Unsupported nodes
/// surface as a <c>null</c> result; the caller drops the queryable
/// meta and the lambda flows as a plain closure. The MS0024 hard
/// error path is reserved for a follow-up.
/// </para>
/// </summary>
internal sealed class IrExpressionTreeExtractor
{
    private readonly SemanticModel _semantic;
    private readonly IrTypeOriginResolver? _originResolver;
    private readonly Metano.Annotations.TargetLanguage? _target;
    private readonly IrExpressionExtractor _valueExtractor;

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
        IrExpressionExtractor valueExtractor
    )
    {
        _semantic = semanticModel;
        _originResolver = originResolver;
        _target = target;
        _valueExtractor = valueExtractor;
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

            default:
                _failed = true;
                return null;
        }
    }

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

    private IrExprTreeNode? WalkIdentifier(IdentifierNameSyntax id)
    {
        var symbol = _semantic.GetSymbolInfo(id).Symbol;
        if (symbol is null)
        {
            _failed = true;
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
                _failed = true;
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

    private IrTypeRef? MapType(ExpressionSyntax expr)
    {
        var t = _semantic.GetTypeInfo(expr).Type;
        return t is null ? null : IrTypeRefMapper.Map(t, _originResolver, _target);
    }
}
