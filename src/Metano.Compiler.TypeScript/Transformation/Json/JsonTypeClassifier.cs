using Metano.Compiler;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.TypeScript.Transformation.Json;

/// <summary>
/// Classifies a C# type into the <see cref="JsonPropertyKind"/> its runtime
/// <c>TypeDescriptor</c> carries, and names the types a container descriptor
/// nests. Shared by property specs, value roots and the contract closure, so
/// a type gets the same descriptor wherever it appears (FR-030).
/// </summary>
internal static class JsonTypeClassifier
{
    /// <summary>
    /// The descriptor kind of <paramref name="type"/>. Every type that is not
    /// a primitive, a well-known struct, a collection, an enum or a branded
    /// type is an object contract (<see cref="JsonPropertyKind.Ref"/>).
    /// </summary>
    public static JsonPropertyKind Classify(ITypeSymbol type)
    {
        if (IsNullable(type))
            return JsonPropertyKind.Nullable;

        if (IsPrimitive(type))
            return JsonPropertyKind.Primitive;

        if (type is INamedTypeSymbol named)
            return ClassifyNamed(named);

        if (type is IArrayTypeSymbol)
            return JsonPropertyKind.Array;

        return JsonPropertyKind.Ref;
    }

    /// <summary>
    /// The types a container descriptor nests: nullable inner, array /
    /// hash-set element, map key and value — the same accessors
    /// <see cref="JsonTypeDescriptorBuilder"/> builds the nested descriptors from.
    /// </summary>
    public static IEnumerable<ITypeSymbol> ChildTypesOf(ITypeSymbol type, JsonPropertyKind kind) =>
        kind switch
        {
            JsonPropertyKind.Nullable => [UnwrapNullable(type)],
            JsonPropertyKind.Array => [ElementTypeOf(type)],
            JsonPropertyKind.HashSet => [SetElementTypeOf(type)],
            JsonPropertyKind.Map => [KeyTypeOf(type), ValueTypeOf(type)],
            _ => [],
        };

    /// <summary>The element of a set (<c>HashSet&lt;T&gt;</c> → <c>T</c>).</summary>
    public static ITypeSymbol SetElementTypeOf(ITypeSymbol type) =>
        ((INamedTypeSymbol)type).TypeArguments[0];

    /// <summary>The key of a dictionary (<c>Dictionary&lt;K,V&gt;</c> → <c>K</c>).</summary>
    public static ITypeSymbol KeyTypeOf(ITypeSymbol type) =>
        ((INamedTypeSymbol)type).TypeArguments[0];

    /// <summary>The value of a dictionary (<c>Dictionary&lt;K,V&gt;</c> → <c>V</c>).</summary>
    public static ITypeSymbol ValueTypeOf(ITypeSymbol type) =>
        ((INamedTypeSymbol)type).TypeArguments[1];

    /// <summary>
    /// <c>Nullable&lt;T&gt;</c> or an annotated reference type <c>T?</c>: the
    /// descriptor is <c>nullable</c> and a property spec is <c>optional</c>.
    /// </summary>
    public static bool IsNullable(ITypeSymbol type) =>
        type.IsNullableValueType() || type.NullableAnnotation == NullableAnnotation.Annotated;

    /// <summary><c>T</c> for <c>Nullable&lt;T&gt;</c> or an annotated
    /// reference type <c>T?</c>; the type itself otherwise.</summary>
    public static ITypeSymbol UnwrapNullable(ITypeSymbol type)
    {
        if (type.IsNullableValueType())
            return ((INamedTypeSymbol)type).TypeArguments[0];

        if (type.NullableAnnotation == NullableAnnotation.Annotated)
            return type.WithNullableAnnotation(NullableAnnotation.NotAnnotated);

        return type;
    }

    /// <summary>The element of an array or of a single-argument generic
    /// collection.</summary>
    public static ITypeSymbol ElementTypeOf(ITypeSymbol type)
    {
        if (type is IArrayTypeSymbol array)
            return array.ElementType;

        if (type is INamedTypeSymbol { TypeArguments.Length: > 0 } named)
            return named.TypeArguments[0];

        return type;
    }

    /// <summary>
    /// The CLR name of a C# numeric type that maps to a TS <c>number</c>
    /// (<c>System.Int32</c>, <c>System.Double</c>, …), as System.Text.Json
    /// names it in errors; <c>null</c> for any other type.
    /// </summary>
    public static string? NumberTypeNameOf(ITypeSymbol type) =>
        type.SpecialType
            is SpecialType.System_Byte
                or SpecialType.System_SByte
                or SpecialType.System_Int16
                or SpecialType.System_UInt16
                or SpecialType.System_Int32
                or SpecialType.System_UInt32
                or SpecialType.System_Int64
                or SpecialType.System_UInt64
                or SpecialType.System_Single
                or SpecialType.System_Double
            ? $"System.{type.MetadataName}"
            : null;

    private static bool IsPrimitive(ITypeSymbol type) =>
        type.SpecialType
            is SpecialType.System_String
                or SpecialType.System_Int16
                or SpecialType.System_Int32
                or SpecialType.System_Int64
                or SpecialType.System_UInt16
                or SpecialType.System_UInt32
                or SpecialType.System_UInt64
                or SpecialType.System_Single
                or SpecialType.System_Double
                or SpecialType.System_Byte
                or SpecialType.System_SByte
                or SpecialType.System_Boolean;

    private static JsonPropertyKind ClassifyNamed(INamedTypeSymbol named)
    {
        var fullName = named.ToDisplayString();

        // Guid → UUID branded type. At runtime it's a string (JSON wire:
        // primitive string), but the deserialize side wraps via UUID.create.
        if (fullName is "System.Guid")
            return JsonPropertyKind.Branded;

        // Uri → primitive string (no special handling needed).
        if (fullName is "System.Uri")
            return JsonPropertyKind.Primitive;

        if (fullName is "System.Decimal" || named.SpecialType == SpecialType.System_Decimal)
            return JsonPropertyKind.Decimal;

        if (IsTemporal(fullName))
            return JsonPropertyKind.Temporal;

        if (named.IsDictionaryLike() && named.TypeArguments.Length >= 2)
            return JsonPropertyKind.Map;

        if (named.IsSetLike() && named.TypeArguments.Length > 0)
            return JsonPropertyKind.HashSet;

        if (IsSequence(named, fullName))
            return JsonPropertyKind.Array;

        if (named.TypeKind == TypeKind.Enum)
            return SymbolHelper.HasAttribute(named, "StringEnum")
                ? JsonPropertyKind.Enum
                : JsonPropertyKind.NumericEnum;

        // [Branded] (and its predecessor [InlineWrapper]) → branded
        if (SymbolHelper.HasBranded(named))
            return JsonPropertyKind.Branded;

        return JsonPropertyKind.Ref;
    }

    private static bool IsTemporal(string fullName) =>
        fullName
            is "System.DateTime"
                or "System.DateTimeOffset"
                or "System.DateOnly"
                or "System.TimeOnly"
                or "System.TimeSpan";

    /// <summary>
    /// Collection-like / IEnumerable / IReadOnlyCollection → array. Since
    /// #129 these no longer share the <c>IsCollectionLike</c> predicate (they
    /// lower to TS <c>Iterable&lt;T&gt;</c> at the type level), but JSON-wise
    /// they still serialize as arrays.
    /// </summary>
    private static bool IsSequence(INamedTypeSymbol named, string fullName) =>
        (
            named.IsCollectionLike()
            || fullName.StartsWith("System.Collections.Generic.IEnumerable")
            || fullName.StartsWith("System.Collections.Generic.IReadOnlyCollection")
        )
        && named.TypeArguments.Length > 0;
}
