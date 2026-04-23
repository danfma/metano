namespace Metano.Annotations.TypeScript;

/// <summary>
/// Marks one property on a <c>[GenerateGuard]</c> type as the
/// discriminator field. The generated <c>isT</c> guard narrows on this
/// field first — typically a <c>[StringEnum]</c> whose values
/// uniquely identify the type variant — and short-circuits
/// <c>false</c> when the discriminant doesn't match. The full shape
/// check still runs when the discriminant matches so reserved fields
/// keep being validated.
/// <para>
/// Example: a <c>Shape</c> hierarchy with <c>Kind = Circle | Square</c>
/// tags the base with <c>[Discriminator("Kind")]</c>. The derived
/// <c>Circle</c>'s generated guard then checks
/// <c>v.kind === "Circle"</c> before walking radius/color fields —
/// rejecting any object whose kind points at a different variant
/// without traversing the rest of the shape.
/// </para>
/// <para>
/// This attribute is TypeScript-specific — Dart and other targets have
/// no equivalent narrowing convention and treat it as a no-op. It
/// therefore lives in the <see cref="Metano.Annotations.TypeScript"/>
/// namespace so a cross-target project opting into
/// <c>using Metano.Annotations;</c> does not accidentally see TS-only
/// knobs.
/// </para>
/// <para>
/// The referenced field must exist on the annotated type, must carry
/// <c>[StringEnum]</c>, and must not be nullable. Any other shape
/// raises <c>MS0011</c> at extraction time.
/// </para>
/// </summary>
/// <param name="fieldName">Name of the C# property (original casing)
/// that acts as the discriminant. The guard emits against the
/// camelCased TS field name automatically.</param>
[AttributeUsage(
    AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface,
    Inherited = false
)]
public sealed class DiscriminatorAttribute(string fieldName) : Attribute
{
    public string FieldName { get; } = fieldName;
}
