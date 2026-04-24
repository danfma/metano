namespace Metano.Annotations;

/// <summary>
/// Marks a <c>static class</c> whose scope vanishes at the call site:
/// static member access drops the enclosing class name
/// (<c>Constants.Pi</c> → <c>Pi</c>), and the class itself emits no
/// <c>.ts</c> file. Intended for compile-time container types whose
/// members are either runtime-provided, template-emitted
/// (<c>[Emit]</c>), or constant literals the consumer resolves at use
/// site.
/// <para>
/// In the initial slice, every member on an <c>[Erasable]</c> class
/// must already resolve without emitting a declaration (literal
/// return, <c>[Emit]</c> template, or member that will become
/// <c>[External]</c> / <c>[Inline]</c> once those attributes ship).
/// The broader member-emission contract (plain bodies projected as
/// top-level exports, <c>[Inline]</c> expansion, MS0015 member-level
/// validation) lands in follow-up slices alongside those attributes.
/// </para>
/// <para>
/// Applies only to <c>static class</c>. Non-static targets and
/// combinations with <c>[Transpile]</c> raise
/// <c>MS0015 InvalidErasable</c>.
/// </para>
/// <para>
/// <c>[Erasable]</c> lives in <see cref="Metano.Annotations"/> because
/// the semantic (scope erasure at call site) is meaningful for every
/// target; per-target lowering is defined by each backend.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ErasableAttribute : Attribute;
