using MetaSharp.TypeScript.AST;

namespace MetaSharp.Transformation;

/// <summary>
/// Builds a <see cref="TsTemplate"/> AST node from a JavaScript expression template plus
/// a set of transformed arguments. The template's literal text and the substitution AST
/// nodes both end up in the resulting node, and the printer expands them at emit time —
/// recursively rendering each substituted node via <c>PrintExpression</c>, so any
/// TypeScript expression (nested calls, arrow functions, binary operators, …) round-trips
/// correctly without the template author having to worry about textual rendering of the
/// substitution payload.
///
/// Two flavors of placeholder are supported in the template string:
///
/// <list type="bullet">
///   <item>
///     <c>$this</c> — the optional instance receiver. Only meaningful when
///     <paramref name="receiver"/> is non-null (i.e., expanding a mapping for an instance
///     member).
///   </item>
///   <item>
///     <c>$0</c>, <c>$1</c>, … — the C# explicit arguments in order. Same convention as
///     <see cref="EmitAttribute"/>.
///   </item>
/// </list>
///
/// Used by both <see cref="InvocationHandler"/> (for <c>[Emit]</c>) and
/// <see cref="BclMapper"/> (for declarative <c>[MapMethod]</c>/<c>[MapProperty]</c>
/// mappings whose form is a template instead of a simple rename).
/// </summary>
public static class JsTemplateExpander
{
    public static TsExpression Expand(
        string template,
        TsExpression? receiver,
        IReadOnlyList<TsExpression> args) =>
        new TsTemplate(template, receiver, args);
}
