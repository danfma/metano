namespace Metano.Annotations.TypeScript;

/// <summary>
/// Declares that the annotated symbol is provided by the target
/// runtime — no declaration is emitted for it. Applies to classes,
/// methods, properties, and fields.
/// <para>
/// Class-level use (<c>[External] class Document</c>) emits no
/// <c>.ts</c> file for the class, and call-site access keeps the
/// class-qualified form (<c>Document.foo</c> stays <c>Document.foo</c>).
/// Use this shape when the runtime publishes a namespace-like object
/// (<c>React.createElement</c>, <c>JSON.stringify</c>).
/// </para>
/// <para>
/// Member-level use suppresses the declaration only for that member.
/// Typical pairing: a <c>[Erasable]</c> container with per-member
/// <c>[External]</c> on symbols whose implementation is a runtime
/// global (<c>[Erasable] class Js</c> + <c>[External, Name("document")]
/// public static Document Document</c>).
/// </para>
/// <para>
/// Scope-erasing behavior (flattening <c>ClassName.member</c> to bare
/// <c>member</c>) lives on <see cref="Metano.Annotations.ErasableAttribute"/>.
/// A class that needs both behaviors combines the two attributes.
/// </para>
/// <para>
/// Combining <c>[External]</c> with <c>[Transpile]</c> on the same
/// class raises <c>MS0012 InvalidExternal</c> — the transpiler cannot
/// simultaneously honor "no emission" and "full emission" on the same
/// type.
/// </para>
/// <para>
/// This attribute is TypeScript-specific; other targets (Dart,
/// Kotlin) treat it as a no-op because their runtime-global surface
/// has different conventions. It lives under
/// <see cref="Metano.Annotations.TypeScript"/> so cross-target
/// consumers that only import <c>using Metano.Annotations;</c> do not
/// see TS-only knobs.
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
