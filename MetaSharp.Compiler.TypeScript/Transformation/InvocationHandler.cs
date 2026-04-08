using MetaSharp.TypeScript.AST;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MetaSharp.Transformation;

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
                var emitArgs = invocation.ArgumentList.Arguments
                    .Select(a => _parent.TransformExpression(a.Expression))
                    .ToList();
                return ExpandEmit(emit, emitArgs);
            }

            var mapped = BclMapper.TryMapMethod(methodSymbol, invocation, _parent);
            if (mapped is not null)
                return mapped;
        }

        var callee = _parent.TransformExpression(invocation.Expression);
        var args = invocation
            .ArgumentList.Arguments.Select(a => _parent.TransformExpression(a.Expression))
            .ToList();

        return new TsCallExpression(callee, args);
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
