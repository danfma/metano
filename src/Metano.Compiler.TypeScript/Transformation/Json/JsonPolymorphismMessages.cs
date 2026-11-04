namespace Metano.Compiler.TypeScript.Transformation.Json;

/// <summary>
/// The texts of <c>MS0032</c> / <c>MS0033</c> (spec 009
/// contracts/diagnostics.md). Type names are passed already displayed; a
/// generic base is named by its definition (<c>GBase&lt;T&gt;</c>) so every
/// construction reports the same text.
/// </summary>
internal static class JsonPolymorphismMessages
{
    public static string SealedBase(string baseType) =>
        $"'{baseType}' does not support polymorphism: polymorphic types cannot be sealed types "
        + "(as in System.Text.Json).";

    public static string NoRegistration(string baseType) =>
        $"Polymorphic configuration for type '{baseType}' should specify at least one derived "
        + "type (as in System.Text.Json).";

    public static string ReservedDiscriminator(string discriminator, string baseType) =>
        $"The discriminator '{discriminator}' of '{baseType}' is reserved: '$id', '$ref' and "
        + "'$values' cannot be used as custom type discriminator property names (as in "
        + "System.Text.Json).";

    public static string GenericRegistration(string registered) =>
        $"Generic derived type '{registered}' is not supported.";

    public static string NotAssignable(string registered, string owner) =>
        $"'{registered}' registered by [JsonDerivedType] on '{owner}' is not assignable to "
        + $"'{owner}'.";

    public static string NotAssignableToConstruction(
        string registered,
        string owner,
        string construction
    ) =>
        $"'{registered}' registered on '{owner}' is not assignable to '{construction}', which "
        + "this context reaches.";

    public static string NotTranspiled(string registered, string owner) =>
        $"'{registered}' registered by [JsonDerivedType] on '{owner}' is not transpiled, so it "
        + "has no TypeScript contract.";

    public static string AbstractWithoutNearestAncestor(string registered, string owner) =>
        $"'{registered}' is abstract; abstract derived types require UnknownDerivedTypeHandling "
        + $"= FallBackToNearestAncestor on '{owner}' (as in System.Text.Json).";

    public static string RepeatedType(string registered, string owner) =>
        $"'{registered}' is registered twice on '{owner}'.";

    public static string RepeatedTag(string tag, string owner) =>
        $"Type discriminator '{tag}' is registered twice on '{owner}'.";

    public static string DiscriminatorConflict(string member, string jsonName, string owner) =>
        $"'{member}' serializes as '{jsonName}', which conflicts with the discriminator of "
        + $"'{owner}'.";

    public static string IntegerLikeName(string jsonName, string member) =>
        $"JSON property '{jsonName}' of '{member}' is integer-like; JavaScript would order it "
        + "before the discriminator.";

    /// <summary>
    /// The read side differs: a concrete base reads the untagged value as
    /// itself, an abstract one cannot read it at all.
    /// </summary>
    public static string UntaggedRegistration(string registered, string owner, bool isAbstractBase)
    {
        var readBack = isAbstractBase
            ? $"cannot be deserialized through the abstract '{owner}'"
            : $"deserializes as '{owner}'";

        return $"'{registered}' is registered on '{owner}' without a type discriminator; it "
            + $"serializes without a tag and {readBack} (as in System.Text.Json).";
    }

    public static string NestedPolymorphicRegistration(string registered, string owner) =>
        $"'{registered}' declares its own [JsonDerivedType]s; as in System.Text.Json, they apply "
        + $"only through '{registered}' and are not re-dispatched through '{owner}'.";

    public static string StructuralHierarchyNotSupported(string type) =>
        "Polymorphism for interface and [PlainObject] types is not supported yet; "
        + $"'{type}' is emitted without a discriminator.";
}
