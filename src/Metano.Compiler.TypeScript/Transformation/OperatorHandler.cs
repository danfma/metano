using Metano.TypeScript.AST;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Transformation;

/// <summary>
/// Lowers C# operator expressions into TypeScript equivalents:
/// <list type="bullet">
///   <item>
///     <see cref="BinaryExpressionSyntax"/> — covers both ordinary binary operators and
///     the legacy <c>x is Type</c> form (lowered to <c>x instanceof Type</c>; the modern
///     pattern-matching <c>x is Type t</c> is handled by <see cref="PatternMatchingHandler"/>
///     instead).
///   </item>
///   <item><see cref="AssignmentExpressionSyntax"/> — <c>=</c>, compound assignments, and
///   <c>??=</c>.</item>
///   <item><see cref="PrefixUnaryExpressionSyntax"/> — <c>-x</c>, <c>!x</c>, <c>++x</c>, …</item>
/// </list>
///
/// The C# → TS operator name table is small (only <c>==</c>/<c>!=</c> become
/// <c>===</c>/<c>!==</c>; everything else passes through), so it lives as private static
/// helpers on this handler.
///
/// <para>
/// <strong>Decimal special-case:</strong> when both operands are <c>System.Decimal</c>,
/// the binary operator is rewritten as a method call on the decimal.js
/// <c>Decimal</c> instance (e.g., <c>a + b</c> → <c>a.plus(b)</c>). This is necessary
/// because decimal.js is a class wrapper, not a primitive — the JS arithmetic operators
/// don't work on it. Mixed-type operands (e.g., <c>decimalVar + 1</c>) are detected via
/// <see cref="TypeInfo.ConvertedType"/> so the implicit C# numeric conversion is honored.
/// </para>
/// </summary>
public sealed class OperatorHandler(ExpressionTransformer parent)
{
    private readonly ExpressionTransformer _parent = parent;

    public TsExpression TransformBinary(BinaryExpressionSyntax bin)
    {
        // x is Type (old-style, before pattern matching) → x instanceof Type
        if (bin.OperatorToken.Text == "is")
        {
            return new TsBinaryExpression(
                _parent.TransformExpression(bin.Left),
                "instanceof",
                new TsIdentifier(bin.Right.ToString()) // keep PascalCase for type name
            );
        }

        // Decimal arithmetic / comparison: lower to method call on decimal.js Decimal.
        if (
            IsDecimalOperand(bin.Left)
            && IsDecimalOperand(bin.Right)
            && TryMapDecimalBinary(bin.OperatorToken.Text) is { } method
        )
        {
            var left = _parent.TransformExpression(bin.Left);
            var right = _parent.TransformExpression(bin.Right);
            // Comparison operators with negation: `a != b` → `!a.eq(b)`. The negation
            // is encoded by prefixing the method name with "!" — we strip and wrap.
            var negate = method.StartsWith('!');
            var methodName = negate ? method[1..] : method;
            var call = new TsCallExpression(new TsPropertyAccess(left, methodName), [right]);
            return negate ? new TsUnaryExpression("!", call) : call;
        }

        return new TsBinaryExpression(
            _parent.TransformExpression(bin.Left),
            MapBinaryOperator(bin.OperatorToken.Text),
            _parent.TransformExpression(bin.Right)
        );
    }

    public TsExpression TransformAssignment(AssignmentExpressionSyntax assign)
    {
        // Dictionary indexer SET: `dict[key] = value` → `dict.set(key, value)` since
        // JS Map doesn't support bracket assignment. Only fires for the simple `=`
        // operator — compound forms (`dict[k] += 1`) on a dictionary aren't legal in
        // C# anyway.
        if (
            assign.OperatorToken.Text == "="
            && assign.Left is ElementAccessExpressionSyntax elemAccess
            && ExpressionTransformer.IsDictionaryLike(
                _parent.Model.GetTypeInfo(elemAccess.Expression).Type
            )
        )
        {
            var receiver = _parent.TransformExpression(elemAccess.Expression);
            var key = _parent.TransformExpression(elemAccess.ArgumentList.Arguments[0].Expression);
            var value = _parent.TransformExpression(assign.Right);
            return new TsCallExpression(new TsPropertyAccess(receiver, "set"), [key, value]);
        }

        if (assign.OperatorToken.Text is "+=" or "-=")
        {
            var leftSymbol = _parent.Model.GetSymbolInfo(assign.Left).Symbol;
            if (leftSymbol is IEventSymbol evt)
            {
                var eventName = TypeScriptNaming.ToCamelCaseMember(evt.Name);
                var suffix = assign.OperatorToken.Text == "+=" ? "$add" : "$remove";
                var receiver = assign.Left is MemberAccessExpressionSyntax memberAccess
                    ? _parent.TransformExpression(memberAccess.Expression)
                    : new TsIdentifier("this");
                return new TsCallExpression(
                    new TsPropertyAccess(receiver, $"{eventName}{suffix}"),
                    [_parent.TransformExpression(assign.Right)]
                );
            }
        }

        // User-defined compound assignment: x += y → x = x.$add(y) when the compound
        // operator resolves to a user-defined operator method.
        if (TryLowerCompoundOperatorAssignment(assign) is { } lowered)
            return lowered;

        return new TsBinaryExpression(
            _parent.TransformExpression(assign.Left),
            MapAssignmentOperator(assign.OperatorToken.Text),
            _parent.TransformExpression(assign.Right)
        );
    }

    public TsExpression TransformPrefixUnary(PrefixUnaryExpressionSyntax prefix)
    {
        // Decimal negation: -x → x.neg()
        if (prefix.OperatorToken.Text == "-" && IsDecimalOperand(prefix.Operand))
        {
            return new TsCallExpression(
                new TsPropertyAccess(_parent.TransformExpression(prefix.Operand), "neg"),
                []
            );
        }

        return new TsUnaryExpression(
            prefix.OperatorToken.Text,
            _parent.TransformExpression(prefix.Operand)
        );
    }

    /// <summary>
    /// Postfix increment / decrement: <c>x++</c>, <c>x--</c>. JS uses the same syntax,
    /// so the transform is essentially identity except we need a dedicated AST node
    /// since <see cref="TsUnaryExpression"/> always renders the operator on the left.
    /// </summary>
    public TsExpression TransformPostfixUnary(PostfixUnaryExpressionSyntax postfix) =>
        new TsPostfixUnaryExpression(
            _parent.TransformExpression(postfix.Operand),
            postfix.OperatorToken.Text
        );

    private bool IsDecimalOperand(ExpressionSyntax expr)
    {
        var info = _parent.Model.GetTypeInfo(expr);
        // ConvertedType honors the implicit C# numeric conversion (e.g., int → decimal
        // in `decimalVar + 1`); fall back to Type when the converted form is unknown.
        var t = info.ConvertedType ?? info.Type;
        return t?.SpecialType == SpecialType.System_Decimal;
    }

    /// <summary>
    /// Maps a C# binary operator token to the corresponding decimal.js method name.
    /// Comparison forms that need a logical negation are encoded with a leading "!"
    /// (e.g., <c>!=</c> → <c>!eq</c>); the caller wraps the call site accordingly.
    /// Returns null when the operator has no decimal equivalent (logical &amp;&amp;/||,
    /// bitwise &amp;/|/^, etc. — those don't apply to monetary types in practice).
    /// </summary>
    private static string? TryMapDecimalBinary(string op) =>
        op switch
        {
            "+" => "plus",
            "-" => "minus",
            "*" => "times",
            "/" => "div",
            "%" => "mod",
            "==" => "eq",
            "!=" => "!eq",
            "<" => "lt",
            ">" => "gt",
            "<=" => "lte",
            ">=" => "gte",
            _ => null,
        };

    private static string MapBinaryOperator(string op) =>
        op switch
        {
            "==" => "===",
            "!=" => "!==",
            _ => op,
        };

    /// <summary>
    /// Lowers a compound assignment (<c>x += y</c>, <c>x -= y</c>, <c>x *= y</c>, etc.)
    /// when the underlying operator is a user-defined operator method. The C# semantic
    /// model resolves compound assignments to their operator method — if it's user-defined,
    /// we rewrite to <c>x = x.$add(y)</c>.
    /// </summary>
    private TsExpression? TryLowerCompoundOperatorAssignment(AssignmentExpressionSyntax assign)
    {
        // The semantic model exposes the operator method for compound assignments
        // via GetSymbolInfo on the assignment expression itself.
        var symbolInfo = _parent.Model.GetSymbolInfo(assign);
        if (
            symbolInfo.Symbol is not IMethodSymbol { MethodKind: MethodKind.UserDefinedOperator } op
        )
            return null;

        var opToken = assign.OperatorToken.Text.TrimEnd('='); // "+=" → "+"
        var opName = MapCompoundOperatorToken(opToken);
        if (opName is null)
            return null;

        var left = _parent.TransformExpression(assign.Left);
        var right = _parent.TransformExpression(assign.Right);

        // x = x.$add(y) — reuse `left` to avoid evaluating the LHS twice.
        return new TsBinaryExpression(
            left,
            "=",
            new TsCallExpression(new TsPropertyAccess(left, $"${opName}"), [right])
        );
    }

    private static string? MapCompoundOperatorToken(string token) =>
        token switch
        {
            "+" => "add",
            "-" => "subtract",
            "*" => "multiply",
            "/" => "divide",
            "%" => "modulo",
            _ => null,
        };

    private static string MapAssignmentOperator(string op) =>
        op switch
        {
            "??=" => "??=",
            _ => op,
        };
}
