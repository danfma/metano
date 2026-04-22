namespace Metano.Annotations.TypeScript;

/// <summary>
/// Marks a parameter or property whose generated TypeScript signature
/// should use the optional-presence form (<c>name?: T</c>) instead of
/// the default present-with-nullable-value form
/// (<c>name: T | null = null</c>). The target C# type <b>must</b> already
/// be nullable — the attribute relies on <c>undefined</c> on the JS side
/// collapsing to <c>null</c> on the C# side via the loose-equality
/// null-check convention documented in ADR-0014. A non-nullable C# type
/// cannot safely represent a possibly-absent value and raises
/// <c>MS0010</c> at extraction time.
/// <para>
/// This attribute is TypeScript-specific — Dart and other targets have
/// no separate "absent" vs "null" distinction and treat it as a no-op
/// (the field stays nullable either way). It therefore lives in the
/// <see cref="Metano.Annotations.TypeScript"/> namespace so a
/// cross-target project opting into <c>using Metano.Annotations;</c>
/// does not accidentally see TS-only knobs.
/// </para>
/// <para>
/// Emission matrix (pair with <see cref="System.Nullable{T}"/>
/// nullability on the C# side):
/// <list type="bullet">
///   <item><c>string</c> → <c>name: string</c> (required, non-null).</item>
///   <item><c>string?</c> → <c>name: string | null = null</c> (present,
///     nullable — the default).</item>
///   <item><c>[Optional] string?</c> → <c>name?: string | null</c>
///     (optional presence, nullable).</item>
///   <item><c>[Optional] string</c> → <c>MS0010</c> — the non-nullable
///     combination is rejected.</item>
/// </list>
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property, Inherited = false)]
public sealed class OptionalAttribute : Attribute;
