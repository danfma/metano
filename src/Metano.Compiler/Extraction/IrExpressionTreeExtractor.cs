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
/// <para>
/// <b>Constant folding (#208).</b> Any expression that Roslyn proves
/// constant via <see cref="SemanticModel.GetConstantValue"/> collapses
/// to <see cref="IrExprLiteral"/> instead of a runtime capture. That
/// covers literal tokens, <c>const</c> fields, and <c>const</c>
/// locals natively. On top of that, <see cref="TryFoldConstant"/>
/// peeks at <c>static readonly</c> field declarations and folds the
/// reference when the initializer itself is a Roslyn-constant
/// expression — Roslyn does not fold <c>static readonly</c> reads on
/// its own. Binary and unary nodes finally re-fold post-walk when
/// every operand collapsed to a literal, so combinations like
/// <c>MIN_AGE + 1</c> (where <c>MIN_AGE</c> is a folded
/// <c>static readonly</c>) come out as a single literal node. The
/// walker stays conservative: regular instance <c>readonly</c>
/// fields, mutable static fields, and any field whose initializer
/// references something else stay as runtime captures.
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

        // Hoist pure repeated subtrees into a single $0/$1/… binding
        // wrapper so providers and the wire payload don't carry the
        // same member chain twice (#209).
        tree = IrExprTreeHoister.Hoist(tree);

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
        var op = unary.OperatorToken.ValueText;
        if (operand is IrExprLiteral lit && TryFoldUnary(op, lit.Value, out var foldedValue))
            return new IrExprLiteral(foldedValue, MapType(unary));
        return new IrExprUnary(op, operand);
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
        var op = bin.OperatorToken.ValueText;
        if (
            left is IrExprLiteral leftLit
            && right is IrExprLiteral rightLit
            && TryFoldBinary(op, leftLit.Value, rightLit.Value, out var foldedValue)
        )
            return new IrExprLiteral(foldedValue, MapType(bin));
        return new IrExprBinary(op, left, right);
    }

    /// <summary>
    /// Folds <paramref name="expr"/> to a literal when its value is
    /// known at compile time. Two paths:
    /// <list type="number">
    ///   <item>Roslyn's own constant evaluator
    ///   (<see cref="SemanticModel.GetConstantValue"/>) covers literal
    ///   tokens, <c>const</c> fields, and <c>const</c> locals.</item>
    ///   <item>For <c>static readonly</c> fields Roslyn declines to
    ///   fold the reference itself, so we peek at the declarator and
    ///   re-ask its semantic model whether the initializer is
    ///   constant. Anything more elaborate than a literal /
    ///   <c>const</c> reference stays a runtime capture — we never
    ///   execute user code at compile time.</item>
    /// </list>
    /// Regular (non-static) <c>readonly</c> instance fields and any
    /// mutable field stay as runtime captures by construction.
    /// </summary>
    private bool TryFoldConstant(ExpressionSyntax expr, out IrExprTreeNode? folded)
    {
        var constant = _semantic.GetConstantValue(expr);
        if (constant.HasValue)
        {
            folded = new IrExprLiteral(constant.Value, MapType(expr));
            return true;
        }
        if (TryFoldStaticReadonlyField(expr, out folded))
            return true;
        folded = null;
        return false;
    }

    /// <summary>
    /// Folds a <c>static readonly</c> field reference when its
    /// initializer is itself a Roslyn-constant expression. Mirrors the
    /// <c>[Constant]</c> validator's policy in
    /// <c>CSharpSourceFrontend.IsFieldInitializerConstant</c>: only
    /// initializers Roslyn can fold are accepted, so we never run user
    /// code (<c>Compute()</c>, factory calls, …) to populate a literal.
    /// </summary>
    private bool TryFoldStaticReadonlyField(ExpressionSyntax expr, out IrExprTreeNode? folded)
    {
        folded = null;
        if (!IsFoldableStaticReadonlyField(expr, out var field))
            return false;
        foreach (var reference in field.DeclaringSyntaxReferences)
        {
            if (TryFoldFieldDeclarator(reference, expr, out folded))
                return true;
        }
        return false;
    }

    private bool IsFoldableStaticReadonlyField(ExpressionSyntax expr, out IFieldSymbol field)
    {
        field = (_semantic.GetSymbolInfo(expr).Symbol as IFieldSymbol)!;
        if (field is null)
            return false;
        return field is { IsStatic: true, IsReadOnly: true, IsConst: false };
    }

    private bool TryFoldFieldDeclarator(
        SyntaxReference reference,
        ExpressionSyntax expr,
        out IrExprTreeNode? folded
    )
    {
        folded = null;
        if (reference.GetSyntax() is not VariableDeclaratorSyntax declarator)
            return false;
        if (declarator.Initializer?.Value is not { } initializer)
            return false;
        var model = _semantic.Compilation.GetSemanticModel(declarator.SyntaxTree);
        var constant = model.GetConstantValue(initializer);
        if (!constant.HasValue)
            return false;
        folded = new IrExprLiteral(constant.Value, MapType(expr));
        return true;
    }

    /// <summary>
    /// Folds a unary expression whose operand already collapsed to an
    /// <see cref="IrExprLiteral"/>. Only the operators the walker
    /// emits are supported (<c>-</c>, <c>+</c>, <c>!</c>, <c>~</c>);
    /// anything outside that set bails to a runtime
    /// <see cref="IrExprUnary"/> node.
    /// </summary>
    private static bool TryFoldUnary(string op, object? value, out object? result)
    {
        result = null;
        switch (op)
        {
            case "-":
                return TryNegate(value, out result);
            case "+":
                if (value is sbyte or short or int or long or float or double or decimal)
                {
                    result = value;
                    return true;
                }
                return false;
            case "!":
                if (value is bool b)
                {
                    result = !b;
                    return true;
                }
                return false;
            case "~":
                return TryBitwiseNot(value, out result);
            default:
                return false;
        }
    }

    private static bool TryNegate(object? value, out object? result)
    {
        result = value switch
        {
            int i => -i,
            long l => -l,
            short s => -(int)s,
            sbyte sb => -(int)sb,
            float f => -f,
            double d => -d,
            decimal m => -m,
            _ => null,
        };
        return result is not null;
    }

    private static bool TryBitwiseNot(object? value, out object? result)
    {
        result = value switch
        {
            int i => ~i,
            long l => ~l,
            uint u => ~u,
            ulong ul => ~ul,
            _ => null,
        };
        return result is not null;
    }

    /// <summary>
    /// Folds a binary expression whose operands already collapsed to
    /// <see cref="IrExprLiteral"/>. Stays conservative: handles
    /// arithmetic (<c>+ - * / %</c>) over matching numeric primitives,
    /// boolean short-circuit (<c>&amp;&amp;</c>, <c>||</c>), and
    /// equality / ordering comparisons. Mixed-type promotions or
    /// operators outside this set fall through to a runtime
    /// <see cref="IrExprBinary"/> node — keeping the runtime tree
    /// identical to the pre-fold shape.
    /// </summary>
    private static bool TryFoldBinary(string op, object? left, object? right, out object? result)
    {
        result = null;
        // Boolean short-circuit folding is independent of the numeric
        // promotion rules below.
        if (left is bool lb && right is bool rb)
        {
            switch (op)
            {
                case "&&":
                case "&":
                    result = lb && rb;
                    return true;
                case "||":
                case "|":
                    result = lb || rb;
                    return true;
                case "==":
                    result = lb == rb;
                    return true;
                case "!=":
                    result = lb != rb;
                    return true;
            }
            return false;
        }

        // Limit numeric folding to operands that share a single
        // primitive type. Roslyn already folded literal pairs, so the
        // remaining cases come from `static readonly` reads — those
        // are typed by their declarations, which is enough to fold
        // without re-implementing the C# promotion lattice.
        if (left is null || right is null || left.GetType() != right.GetType())
            return false;

        switch (left)
        {
            case int li:
                return TryFoldIntArith(op, li, (int)right!, out result);
            case long ll:
                return TryFoldLongArith(op, ll, (long)right!, out result);
            case double ld:
                return TryFoldDoubleArith(op, ld, (double)right!, out result);
            case float lf:
                return TryFoldFloatArith(op, lf, (float)right!, out result);
            case decimal lm:
                return TryFoldDecimalArith(op, lm, (decimal)right!, out result);
            case string ls when right is string rs:
                if (op == "+")
                {
                    result = ls + rs;
                    return true;
                }
                if (op == "==")
                {
                    result = ls == rs;
                    return true;
                }
                if (op == "!=")
                {
                    result = ls != rs;
                    return true;
                }
                return false;
            default:
                return false;
        }
    }

    private static bool TryFoldIntArith(string op, int l, int r, out object? result)
    {
        result = op switch
        {
            "+" => (object)(l + r),
            "-" => l - r,
            "*" => l * r,
            "/" when r != 0 => l / r,
            "%" when r != 0 => l % r,
            "==" => l == r,
            "!=" => l != r,
            "<" => l < r,
            "<=" => l <= r,
            ">" => l > r,
            ">=" => l >= r,
            "&" => l & r,
            "|" => l | r,
            "^" => l ^ r,
            _ => null,
        };
        return result is not null;
    }

    private static bool TryFoldLongArith(string op, long l, long r, out object? result)
    {
        result = op switch
        {
            "+" => (object)(l + r),
            "-" => l - r,
            "*" => l * r,
            "/" when r != 0 => l / r,
            "%" when r != 0 => l % r,
            "==" => l == r,
            "!=" => l != r,
            "<" => l < r,
            "<=" => l <= r,
            ">" => l > r,
            ">=" => l >= r,
            "&" => l & r,
            "|" => l | r,
            "^" => l ^ r,
            _ => null,
        };
        return result is not null;
    }

    private static bool TryFoldDoubleArith(string op, double l, double r, out object? result)
    {
        result = op switch
        {
            "+" => (object)(l + r),
            "-" => l - r,
            "*" => l * r,
            "/" => l / r,
            "%" => l % r,
            "==" => l == r,
            "!=" => l != r,
            "<" => l < r,
            "<=" => l <= r,
            ">" => l > r,
            ">=" => l >= r,
            _ => null,
        };
        return result is not null;
    }

    private static bool TryFoldFloatArith(string op, float l, float r, out object? result)
    {
        result = op switch
        {
            "+" => (object)(l + r),
            "-" => l - r,
            "*" => l * r,
            "/" => l / r,
            "%" => l % r,
            "==" => l == r,
            "!=" => l != r,
            "<" => l < r,
            "<=" => l <= r,
            ">" => l > r,
            ">=" => l >= r,
            _ => null,
        };
        return result is not null;
    }

    private static bool TryFoldDecimalArith(string op, decimal l, decimal r, out object? result)
    {
        // decimal arithmetic always throws on overflow (no unchecked
        // decimal in C#). Bail on overflow so the build keeps moving
        // and the runtime closure takes the value path.
        try
        {
            result = op switch
            {
                "+" => (object)(l + r),
                "-" => l - r,
                "*" => l * r,
                "/" when r != 0 => l / r,
                "%" when r != 0 => l % r,
                "==" => l == r,
                "!=" => l != r,
                "<" => l < r,
                "<=" => l <= r,
                ">" => l > r,
                ">=" => l >= r,
                _ => null,
            };
            return result is not null;
        }
        catch (OverflowException)
        {
            result = null;
            return false;
        }
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
