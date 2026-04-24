namespace Metano.Annotations;

/// <summary>
/// Marks a value-like <c>struct</c> or <c>record struct</c> to lower
/// as a branded primitive companion in TypeScript: the wrapper type
/// emits as <c>string &amp; { readonly __brand: "Name" }</c> (or the
/// matching primitive) plus a <c>namespace Name</c> holding a
/// <c>create(value)</c> factory and any user-declared static helpers.
/// At runtime the brand is erased — the value moves as the underlying
/// primitive — while the TS type system still distinguishes the
/// wrapper from other shapes that share the same primitive form.
/// <para>
/// Supersedes <see cref="InlineWrapperAttribute"/>. The two attributes
/// carry identical semantics for the duration of the stack; existing
/// callers keep working and will migrate in a follow-up cleanup.
/// "Branded" describes the observable TS output (branded primitive)
/// rather than the mechanism (inline + wrap), matching the naming
/// family defined in ADR-0015.
/// </para>
/// <para>
/// Typical use: domain identifiers (<c>UserId</c>, <c>OrderId</c>),
/// units that should not mix accidentally (<c>Meters</c>,
/// <c>Seconds</c>), or any value that is structurally a primitive
/// but semantically a distinct type at the C# and TS boundaries.
/// </para>
/// <para>
/// Applies to <c>struct</c> / <c>record struct</c> with a single
/// positional parameter whose type is one of the TypeScript primitive
/// forms (<c>string</c>, <c>int</c>/<c>long</c>/<c>double</c>,
/// <c>bool</c>, <c>BigInteger</c>). Other shapes fall through to the
/// regular class/record emission path — no brand is produced.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Struct)]
public sealed class BrandedAttribute : Attribute;
