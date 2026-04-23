namespace Metano.Annotations.TypeScript;

/// <summary>
/// Marks a <c>static class</c> as a stub for runtime-available globals
/// in the target JavaScript environment. The class itself emits no
/// <c>.ts</c> file. Every static member access on the class lowers to
/// a bare identifier reference (the enclosing class name is dropped)
/// so <c>Js.Document</c> in C# becomes <c>document</c> in TypeScript.
/// Matches the mental model of Kotlin/JS's <c>external object</c>.
/// <para>
/// Use for JavaScript globals that live on the host runtime rather
/// than in an importable module: <c>document</c>, <c>window</c>,
/// <c>console</c>, <c>JSON</c>, <c>Math</c>, and similar. Member
/// <c>[Name(target, …)]</c> overrides drive the emitted identifier;
/// without an override the camelCased C# member name is used.
/// </para>
/// <para>
/// Applies only to <c>static class</c>. Non-static targets, and the
/// combination with <c>[Transpile]</c>, raise <c>MS0013</c> at
/// extraction time — the transpiler cannot simultaneously honor
/// "no emission" and "full emission" on the same type.
/// </para>
/// <para>
/// This attribute is TypeScript-specific — other targets (Dart,
/// Kotlin) treat it as a no-op because their runtime-global surface
/// has different conventions. It therefore lives in the
/// <see cref="Metano.Annotations.TypeScript"/> namespace so a
/// cross-target project opting into <c>using Metano.Annotations;</c>
/// does not accidentally see TS-only knobs.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ExternalAttribute : Attribute;
