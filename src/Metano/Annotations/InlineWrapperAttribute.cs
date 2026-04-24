namespace Metano.Annotations;

/// <summary>
/// Marks a value-like struct to transpile as a branded primitive
/// companion object in TypeScript.
/// <para>
/// Superseded by <see cref="BrandedAttribute"/>. Both attributes
/// carry identical semantics for the duration of the attribute-family
/// stack (ADR-0015); existing callers keep working so this attribute
/// stays valid. New code should prefer <c>[Branded]</c> because the
/// name describes the observable output instead of the lowering
/// mechanism.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Struct)]
public sealed class InlineWrapperAttribute : Attribute;
