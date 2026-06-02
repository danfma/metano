namespace Metano.Annotations.TypeScript;

/// <summary>
/// Marks an erased interface that models a JavaScript callable value. Calls to the
/// interface's <c>Invoke(...)</c> method(s) lower to direct invocation of the
/// receiver — <c>recv.Invoke(a)</c> becomes <c>recv(a)</c> — for any arity, and
/// overloaded <c>Invoke</c> is supported (each overload differs only in its
/// argument list and maps identically to a direct call). This replaces the
/// repetitive per-method <c>[Emit("$0($1)")]</c> template with a single
/// declarative marker; an interface (unlike a delegate) can also express the
/// overloaded call signatures.
/// <para>
/// The attribute <b>implies</b> that no declaration is emitted for the interface
/// (it is a callable facade with no TypeScript type of its own), so pairing it
/// with <c>[External]</c> is redundant — harmless if present, but unnecessary.
/// </para>
/// <para>
/// This attribute is TypeScript-specific — other targets (Dart, Kotlin) treat it
/// as a no-op. It lives in the <see cref="Metano.Annotations.TypeScript"/>
/// namespace so a cross-target project opting into
/// <c>using Metano.Annotations;</c> does not accidentally see TS-only knobs.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Interface, Inherited = false)]
public sealed class JsCallableAttribute : Attribute;
