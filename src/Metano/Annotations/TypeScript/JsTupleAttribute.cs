namespace Metano.Annotations.TypeScript;

/// <summary>
/// Marks a positional record as a JS array-tuple — the array-shape sibling of
/// <c>[PlainObject]</c> (which is the object-shape form). The record's positional
/// members map, in declaration order, to the slots of a JavaScript array tuple.
/// <para>
/// Emission:
/// <list type="bullet">
///   <item><b>Standalone</b> — emits a tuple type alias
///     <c>export type Name&lt;…&gt; = [T0, T1, …]</c>; the named type stays usable
///     in annotations.</item>
///   <item><b>Combined with <c>[Import]</c></b> — the declaration is erased; the
///     type's annotation resolves to the imported library tuple (e.g. a
///     <c>Signal&lt;T&gt;</c> from <c>solid-js</c>).</item>
/// </list>
/// In both cases the record is shape-only: no class wrapper and no synthesized
/// <c>equals</c> / <c>hashCode</c> / <c>with</c> members are emitted, exactly as
/// <c>[PlainObject]</c> emits an interface with no class body.
/// </para>
/// <para>
/// Positional member access on a <c>[JsTuple]</c>-typed value lowers to array
/// index access (<c>value.Item0</c> → <c>value[0]</c>), following the member's
/// positional declaration order.
/// </para>
/// <para>
/// This attribute is TypeScript-specific — other targets (Dart, Kotlin) treat it
/// as a no-op. It lives in the <see cref="Metano.Annotations.TypeScript"/>
/// namespace so a cross-target project opting into
/// <c>using Metano.Annotations;</c> does not accidentally see TS-only knobs.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class JsTupleAttribute : Attribute;
