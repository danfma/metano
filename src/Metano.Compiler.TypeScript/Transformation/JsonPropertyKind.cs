namespace Metano.Compiler.TypeScript.Transformation;

/// <summary>
/// Dispatch tag for the JSON context's type-descriptor builder
/// (<c>Json.JsonSpecBuilder</c>), assigned by <c>Json.JsonTypeClassifier</c>.
/// Each value maps to a single arm of <c>BuildTypeDescriptor</c>; the wire
/// string surfaced inside the generated <c>TypeDescriptor.kind</c> field lives
/// in <c>JsonSpecBuilder.GetWireKind</c> so the on-disk shape and the C#
/// discriminator stay one source-of-truth apart. <see cref="Ref"/> is an
/// object contract (a <c>TypeSpec</c>); every other kind of root is a value
/// root (a <c>ValueSpec</c>).
/// </summary>
internal enum JsonPropertyKind
{
    Primitive,
    Nullable,
    Branded,
    Decimal,
    Temporal,
    Map,
    HashSet,
    Array,
    Enum,
    NumericEnum,
    Ref,
}
