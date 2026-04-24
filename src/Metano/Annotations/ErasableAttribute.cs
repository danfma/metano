namespace Metano.Annotations;

/// <summary>
/// Marks a <c>static class</c> whose scope vanishes at every call site.
/// The class itself emits no <c>.ts</c> file, and every static member
/// access drops the enclosing class name
/// (<c>HtmlElementType.Div</c> → <c>Div</c>) so the members read as if
/// they were declared at file scope.
/// <para>
/// Members inside an <c>[Erasable]</c> class emit according to their
/// own attributes, not by inheritance from the container:
/// </para>
/// <list type="bullet">
///   <item><description>Plain method body → top-level
///   <c>export function</c> in a file named after the class.</description></item>
///   <item><description><c>[External]</c> member → no declaration
///   emitted; the runtime provides the symbol.</description></item>
///   <item><description><c>[Emit("…")]</c> member → template expansion
///   at each call site, no free-standing declaration.</description></item>
///   <item><description><c>[Inline]</c> field/property → initializer
///   substitutes each access.</description></item>
///   <item><description><c>[Ignore]</c> member → dropped.</description></item>
/// </list>
/// <para>
/// Applies only to <c>static class</c>. Non-static targets raise
/// <c>MS0015 InvalidErasable</c>. Members that cannot satisfy one of
/// the emission contracts above also raise <c>MS0015</c> — the
/// transpiler refuses to guess the intended lowering.
/// </para>
/// <para>
/// <c>[Erasable]</c> lives in <see cref="Metano.Annotations"/> because
/// the semantic (scope erasure at call site) is meaningful for every
/// target, even if the call-site flatten only shows in languages whose
/// natural shape supports top-level declarations.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ErasableAttribute : Attribute;
