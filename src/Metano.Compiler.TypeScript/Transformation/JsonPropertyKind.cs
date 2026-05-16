namespace Metano.Compiler.TypeScript.Transformation;

/// <summary>
/// Dispatch tag for the <see cref="JsonSerializerContextTransformer"/>
/// type-descriptor builder. Each value maps to a single arm of
/// <c>BuildTypeDescriptor</c>; the wire string surfaced inside the
/// generated <c>TypeDescriptor.kind</c> field lives in
/// <c>JsonSerializerContextTransformer.GetWireKind</c> so the on-disk
/// shape and the C# discriminator stay one source-of-truth apart.
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
