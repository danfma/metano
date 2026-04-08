namespace MetaSharp.TypeScript.AST;

/// <summary>
/// A JS expression built from a textual template plus the TypeScript AST nodes (and
/// generic type-argument names) to splice into placeholder positions. Used by the BCL
/// mapping pipeline (declarative <c>[MapMethod]</c>/<c>[MapProperty]</c> with a
/// <c>JsTemplate</c>) and by <c>[Emit]</c> on user methods.
///
/// Placeholders inside <see cref="Template"/>:
/// <list type="bullet">
///   <item><c>$this</c> — replaced with <see cref="Receiver"/> (if non-null)</item>
///   <item><c>$0</c>, <c>$1</c>, … — replaced with the corresponding entry of <see cref="Arguments"/></item>
///   <item><c>$T0</c>, <c>$T1</c>, … — replaced verbatim with the corresponding entry of
///   <see cref="TypeArgumentNames"/>; used to embed the call site's generic method type
///   arguments inside the lowered expression (e.g., <c>Enum.Parse&lt;Status&gt;</c>
///   → <c>Status[…]</c>)</item>
/// </list>
///
/// The literal chunks of the template are emitted verbatim by the printer; the value
/// placeholder chunks (<c>$this</c>, <c>$N</c>) are emitted by recursively calling
/// <c>PrintExpression</c> on the substituted node, so any TypeScript expression —
/// including nested calls, arrow functions, and binary operators — round-trips correctly
/// without forcing the template author to think about textual rendering. The type-name
/// placeholders (<c>$T<n></c>) are written as plain text since they're already
/// identifiers.
/// </summary>
public sealed record TsTemplate(
    string Template,
    TsExpression? Receiver,
    IReadOnlyList<TsExpression> Arguments,
    IReadOnlyList<string> TypeArgumentNames) : TsExpression
{
    public TsTemplate(string template, TsExpression? receiver, IReadOnlyList<TsExpression> arguments)
        : this(template, receiver, arguments, []) { }
}
