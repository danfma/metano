using Metano.Compiler;
using Metano.TypeScript.AST;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Transformation;

/// <summary>
/// Handles C# method invocations (<c>foo()</c>, <c>obj.bar(x, y)</c>).
///
/// Three layers of dispatch are tried in order:
/// <list type="number">
///   <item>
///     <c>[Emit("$0.foo($1)")]</c> — when the resolved method symbol carries an
///     <see cref="EmitAttribute"/>, the template is expanded inline at the call site
///     by <see cref="ExpandEmit"/>, replacing <c>$0</c>, <c>$1</c>, … with the transformed
///     argument expressions.
///   </item>
///   <item>
///     BCL method mappings via <see cref="BclMapper.TryMapMethod"/> (e.g., <c>List&lt;T&gt;.Add</c>
///     → <c>arr.push</c>).
///   </item>
///   <item>
///     Default — emit a plain <see cref="TsCallExpression"/> with the transformed callee
///     and arguments.
///   </item>
/// </list>
/// </summary>
public sealed class InvocationHandler(ExpressionTransformer parent)
{
    private readonly ExpressionTransformer _parent = parent;

    public TsExpression Transform(InvocationExpressionSyntax invocation)
    {
        // Check for BCL method mappings
        var symbol = _parent.Model.GetSymbolInfo(invocation).Symbol;
        if (symbol is IMethodSymbol methodSymbol)
        {
            // [Emit] — inline JS expression with $0, $1 placeholders
            var emit = TypeScriptNaming.GetEmit(methodSymbol);
            if (emit is not null)
            {
                var emitArgs = invocation
                    .ArgumentList.Arguments.Select(a => _parent.TransformExpression(a.Expression))
                    .ToList();
                return ExpandEmit(emit, emitArgs);
            }

            // Math.Round/Floor/Ceiling with a decimal argument → decimal.js instance method
            // (e.g., `Math.Round(amount)` → `amount.round()`). The standard Math.round is
            // for number operands; decimal.js has its own round/floor/ceil.
            var mathDecimalResult = TryRewriteMathDecimal(methodSymbol, invocation);
            if (mathDecimalResult is not null)
                return mathDecimalResult;

            var mapped = BclMapper.TryMapMethod(methodSymbol, invocation, _parent);
            if (mapped is not null)
                return mapped;

            // [PlainObject] instance method call: rewrite `obj.Method(args)` to
            // `methodName(obj, args)` since the type has no class wrapper at runtime
            // — methods are emitted as standalone helpers that take the receiver as
            // their first parameter (see RecordClassTransformer.EmitPlainObjectMethods).
            if (
                !methodSymbol.IsStatic
                && methodSymbol.MethodKind == MethodKind.Ordinary
                && methodSymbol.ContainingType is { } container
                && SymbolHelper.HasPlainObject(container)
                && invocation.Expression is MemberAccessExpressionSyntax memberAccess
            )
            {
                var receiver = _parent.TransformExpression(memberAccess.Expression);
                var fnName =
                    SymbolHelper.GetNameOverride(methodSymbol)
                    ?? TypeScriptNaming.ToCamelCase(methodSymbol.Name);
                var helperArgs = new List<TsExpression> { receiver };
                helperArgs.AddRange(
                    invocation.ArgumentList.Arguments.Select(a =>
                        _parent.TransformExpression(a.Expression)
                    )
                );
                return new TsCallExpression(new TsIdentifier(fnName), helperArgs);
            }
        }

        var callee = _parent.TransformExpression(invocation.Expression);
        var args = invocation
            .ArgumentList.Arguments.Select(a => _parent.TransformExpression(a.Expression))
            .ToList();

        return new TsCallExpression(callee, args);
    }

    /// <summary>
    /// Rewrites <c>Math.Round/Floor/Ceiling</c> calls when the first argument is
    /// <c>System.Decimal</c>. The JS <c>Math.round</c> only works on <c>number</c>;
    /// decimal.js instances have their own <c>.round()</c>, <c>.floor()</c>, <c>.ceil()</c>.
    /// </summary>
    private TsExpression? TryRewriteMathDecimal(
        IMethodSymbol method,
        InvocationExpressionSyntax invocation
    )
    {
        if (
            method.ContainingType?.ToDisplayString() != "System.Math"
            || method.Parameters.Length == 0
        )
            return null;

        var firstParamType = _parent
            .Model.GetTypeInfo(invocation.ArgumentList.Arguments[0].Expression)
            .Type;
        if (firstParamType?.SpecialType != SpecialType.System_Decimal)
            return null;

        var jsMethodName = method.Name switch
        {
            "Round" => "round",
            "Floor" => "floor",
            "Ceiling" => "ceil",
            "Abs" => "abs",
            _ => null,
        };

        if (jsMethodName is null)
            return null;

        var arg = _parent.TransformExpression(invocation.ArgumentList.Arguments[0].Expression);
        return new TsCallExpression(new TsPropertyAccess(arg, jsMethodName), []);
    }

    /// <summary>
    /// Expands an [Emit] template, replacing <c>$0</c>, <c>$1</c>, … with the textual
    /// form of each transformed argument. Delegates to <see cref="JsTemplateExpander.Expand"/>
    /// (no <c>$this</c> placeholder, since [Emit] is declared on a method whose parameters
    /// already include any receiver).
    /// </summary>
    private static TsExpression ExpandEmit(string template, IReadOnlyList<TsExpression> args) =>
        JsTemplateExpander.Expand(template, receiver: null, args);
}
