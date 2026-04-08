namespace MetaSharp.TypeScript.AST;

/// <summary>
/// A JS expression built from a textual template plus a list of TypeScript AST
/// arguments to splice into placeholder positions. Used by the BCL mapping pipeline
/// (declarative <c>[MapMethod]</c>/<c>[MapProperty]</c> with a <c>JsTemplate</c>) and
/// by <c>[Emit]</c> on user methods.
///
/// Placeholders inside <see cref="Template"/>:
/// <list type="bullet">
///   <item><c>$this</c> — replaced with <see cref="Receiver"/> (if non-null)</item>
///   <item><c>$0</c>, <c>$1</c>, … — replaced with the corresponding entry of <see cref="Arguments"/></item>
/// </list>
///
/// The literal chunks of the template are emitted verbatim by the printer; the
/// placeholder chunks are emitted by recursively calling <c>PrintExpression</c> on the
/// substituted node, so any TypeScript expression — including nested calls,
/// arrow functions, and binary operators — round-trips correctly without forcing
/// the template author to think about textual rendering.
/// </summary>
public sealed record TsTemplate(
    string Template,
    TsExpression? Receiver,
    IReadOnlyList<TsExpression> Arguments) : TsExpression;
