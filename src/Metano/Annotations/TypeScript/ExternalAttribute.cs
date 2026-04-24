namespace Metano.Annotations.TypeScript;

/// <summary>
/// Declares that the annotated symbol is provided by the target
/// runtime — no declaration is emitted for it. The attribute now
/// accepts class, method, property, and field targets so the family
/// can grow into per-member declaration-suppression without a source
/// break; current lowering behavior still keys off the class-level
/// form only.
/// <para>
/// Today a class-level <c>[External]</c> static class emits no
/// <c>.ts</c> file and its static member access flattens to the bare
/// identifier so <c>Js.Document</c> on the C# side becomes
/// <c>document</c> in TypeScript. This matches the shipped behavior
/// in #94. Scope-erasure-without-runtime semantics — the compile-time
/// sugar container — live on
/// <see cref="Metano.Annotations.ErasableAttribute"/>; classes that
/// need both attach the two attributes.
/// </para>
/// <para>
/// Member-level use (method, property, field) is accepted by the
/// attribute surface so callers can already annotate ambient symbols
/// inside a container. The per-member declaration-suppression pass
/// and the split from the class-level flatten ship in a follow-up
/// slice alongside the <c>[NoEmit]</c> redefinition and the DOM
/// binding migration.
/// </para>
/// <para>
/// Applies only to <c>static class</c> at the class level. Non-static
/// targets, and the combination with <c>[Transpile]</c>, raise
/// <c>MS0012 InvalidExternal</c> — the transpiler cannot
/// simultaneously honor "no emission" and "full emission" on the same
/// type.
/// </para>
/// <para>
/// This attribute is TypeScript-specific — other targets (Dart,
/// Kotlin) treat it as a no-op because their runtime-global surface
/// has different conventions. It lives in the
/// <see cref="Metano.Annotations.TypeScript"/> namespace so a
/// cross-target project opting into <c>using Metano.Annotations;</c>
/// does not accidentally see TS-only knobs.
/// </para>
/// </summary>
[AttributeUsage(
    AttributeTargets.Class
        | AttributeTargets.Method
        | AttributeTargets.Property
        | AttributeTargets.Field,
    Inherited = false
)]
public sealed class ExternalAttribute : Attribute;
