namespace Metano.Annotations;

/// <summary>
/// Marks the first parameter of a <c>delegate</c> declaration (or an
/// inlinable method) as the JavaScript <c>this</c> receiver. The
/// decorated parameter is dropped from the emitted TypeScript
/// parameter list and re-introduced as the function type's
/// synthetic <c>this</c> annotation:
/// <code>
/// public delegate void MouseEventListener([This] Element self, MouseEvent @event);
/// </code>
/// lowers to
/// <code>
/// export type MouseEventListener = (this: Element, event: MouseEvent) =&gt; void;
/// </code>
/// Lambdas, anonymous methods, and named methods bound to a
/// <c>[This]</c>-bearing delegate must emit with the <c>function</c>
/// keyword so the runtime can rebind <c>this</c> at dispatch time
/// (arrow functions inherit <c>this</c> from the enclosing scope,
/// which would defeat the pattern).
/// <para>
/// Applies only to the first parameter. <c>ref</c> / <c>out</c> /
/// <c>params</c> modifiers are rejected with <c>MS0018</c>, as is
/// any attempt to attach the attribute to a parameter past index 0.
/// </para>
/// <para>
/// Backends that have no JS-style <c>this</c> rebinding (Dart,
/// Kotlin today) treat the attribute as a no-op and keep the
/// parameter as a regular positional argument in the emitted
/// signature.
/// </para>
/// <para>
/// <c>[This]</c> lives in <see cref="Metano.Annotations"/> because
/// the intent (first parameter acts as the call-site receiver) is
/// meaningful for every backend, even when only the TypeScript
/// target realizes it today.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Parameter, Inherited = false)]
public sealed class ThisAttribute : Attribute;
