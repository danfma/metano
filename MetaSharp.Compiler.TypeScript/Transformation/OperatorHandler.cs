using MetaSharp.TypeScript.AST;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MetaSharp.Transformation;

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
        if (IsDecimalOperand(bin.Left) && IsDecimalOperand(bin.Right)
            && TryMapDecimalBinary(bin.OperatorToken.Text) is { } method)
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

    public TsExpression TransformAssignment(AssignmentExpressionSyntax assign) =>
        new TsBinaryExpression(
            _parent.TransformExpression(assign.Left),
            MapAssignmentOperator(assign.OperatorToken.Text),
            _parent.TransformExpression(assign.Right)
        );

    public TsExpression TransformPrefixUnary(PrefixUnaryExpressionSyntax prefix)
    {
        // Decimal negation: -x → x.neg()
        if (prefix.OperatorToken.Text == "-" && IsDecimalOperand(prefix.Operand))
        {
            return new TsCallExpression(
                new TsPropertyAccess(_parent.TransformExpression(prefix.Operand), "neg"),
                []);
        }

        return new TsUnaryExpression(
            prefix.OperatorToken.Text,
            _parent.TransformExpression(prefix.Operand)
        );
    }

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
    private static string? TryMapDecimalBinary(string op) => op switch
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
}
