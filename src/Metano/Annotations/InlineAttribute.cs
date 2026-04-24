namespace Metano.Annotations;

/// <summary>
/// Marks a <c>static readonly</c> field or a <c>static</c> property
/// with an expression-bodied getter for use-site inlining: every
/// reference to the member is replaced by the member's initializer
/// (or getter expression) before lowering. The declaration itself
/// does not emit a top-level <c>export const</c>; the value lives
/// exclusively at the call sites.
/// <para>
/// Enables catalog-style APIs whose entries must erase into their
/// literal form at the call site. Combined with <c>[Erasable]</c> on
/// the containing class and <c>[PlainObject]</c> / <c>[Branded]</c>
/// on the initializer's type, a call like
/// <c>HtmlElementType.Div</c> lowers directly to the literal shape
/// the runtime expects, matching the TypeScript overload-on-literal
/// pattern without a helper indirection.
/// </para>
/// <para>
/// Applies to:
/// </para>
/// <list type="bullet">
///   <item><c>static readonly</c> fields with an initializer.</item>
///   <item><c>static</c> properties with an expression-bodied getter
///   (<c>public static T Div =&gt; new("div");</c>).</item>
/// </list>
/// <para>
/// Invalid shapes (instance fields, mutable fields, methods, or
/// properties with block-bodied accessors) raise
/// <c>MS0016 InvalidInline</c>. Recursion through <c>[Inline]</c>
/// members is detected by the extractor and raises the same code
/// with a cycle message so a self-referential catalog does not
/// trigger unbounded expansion.
/// </para>
/// <para>
/// <c>[Inline]</c> lives in <see cref="Metano.Annotations"/> because
/// the semantic (use-site substitution) is meaningful for every
/// target; each backend decides how to realize the substitution in
/// its own AST.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, Inherited = false)]
public sealed class InlineAttribute : Attribute;
