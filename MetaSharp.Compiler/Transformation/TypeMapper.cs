using MetaSharp.TypeScript;
using MetaSharp.TypeScript.AST;
using Microsoft.CodeAnalysis;

namespace MetaSharp.Transformation;

/// <summary>
/// Maps C# types to TypeScript types.
/// </summary>
public static class TypeMapper
{
    /// <summary>
    /// Assembly-level BCL export mappings (set by TypeTransformer before transpilation).
    /// Key: C# full type name (e.g. "System.Decimal"), Value: (ExportedName, FromPackage)
    /// </summary>
    [ThreadStatic]
    private static Dictionary<string, (string ExportedName, string FromPackage)>? _bclExportMap;

    public static Dictionary<string, (string ExportedName, string FromPackage)> BclExportMap
    {
        get => _bclExportMap ??= [];
        set => _bclExportMap = value;
    }

    public static TsType Map(ITypeSymbol type)
    {
        // Nullable<T> (value types: int?, bool?, etc.) → T | null
        if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
        {
            var inner = Map(nullable.TypeArguments[0]);
            return MakeNullable(inner);
        }

        // Nullable reference types (string?, Money?, etc.) → T | null
        if (type.NullableAnnotation == NullableAnnotation.Annotated && type.OriginalDefinition.SpecialType != SpecialType.System_Nullable_T)
        {
            var inner = Map(type.WithNullableAnnotation(NullableAnnotation.NotAnnotated));
            return MakeNullable(inner);
        }

        // Check BCL export map first (overrides hardcoded mappings)
        if (type is INamedTypeSymbol bclType && BclExportMap.TryGetValue(bclType.ToDisplayString(), out var bclExport))
        {
            return new TsNamedType(bclExport.ExportedName);
        }

        // Primitives
        switch (type.SpecialType)
        {
            case SpecialType.System_Int16:
            case SpecialType.System_Int32:
            case SpecialType.System_Int64:
            case SpecialType.System_UInt16:
            case SpecialType.System_UInt32:
            case SpecialType.System_UInt64:
            case SpecialType.System_Single:
            case SpecialType.System_Double:
            case SpecialType.System_Decimal:
            case SpecialType.System_Byte:
            case SpecialType.System_SByte:
                return new TsNumberType();

            case SpecialType.System_String:
                return new TsStringType();

            case SpecialType.System_Boolean:
                return new TsBooleanType();

            case SpecialType.System_Void:
                return new TsVoidType();
        }

        // Named types
        if (type is INamedTypeSymbol named)
        {
            var fullName = named.ToDisplayString();

            // Task<T> / ValueTask<T> → Promise<T>
            if (IsTaskLike(named))
            {
                var inner =
                    named.TypeArguments.Length > 0 ? Map(named.TypeArguments[0]) : new TsVoidType();
                return new TsPromiseType(inner);
            }

            // BigInteger → bigint
            if (fullName == "System.Numerics.BigInteger")
                return new TsBigIntType();

            // Temporal date/time types
            if (fullName is "System.DateTime") return new TsNamedType("Temporal.PlainDateTime");
            if (fullName is "System.DateTimeOffset") return new TsNamedType("Temporal.ZonedDateTime");
            if (fullName is "System.DateOnly") return new TsNamedType("Temporal.PlainDate");
            if (fullName is "System.TimeOnly") return new TsNamedType("Temporal.PlainTime");
            if (fullName is "System.TimeSpan") return new TsNamedType("Temporal.Duration");

            // Simple mappings
            if (fullName is "System.Guid") return new TsStringType();
            if (fullName is "System.Uri") return new TsStringType();
            if (fullName is "System.Object") return new TsNamedType("unknown");

            // Dictionary → Map<K, V>
            if (IsDictionaryLike(named) && named.TypeArguments.Length >= 2)
                return new TsNamedType("Map", [Map(named.TypeArguments[0]), Map(named.TypeArguments[1])]);

            // HashSet / ISet → Set<T>
            if (IsSetLike(named) && named.TypeArguments.Length > 0)
                return new TsNamedType("Set", [Map(named.TypeArguments[0])]);

            // KeyValuePair<K,V> → [K, V]
            if (fullName.StartsWith("System.Collections.Generic.KeyValuePair") && named.TypeArguments.Length >= 2)
                return new TsTupleType([Map(named.TypeArguments[0]), Map(named.TypeArguments[1])]);

            // Tuple / ValueTuple → [T1, T2, ...]
            var originalName = named.OriginalDefinition.ToDisplayString();
            if ((originalName.StartsWith("System.Tuple") || originalName.StartsWith("System.ValueTuple")
                || named.IsTupleType)
                && named.TypeArguments.Length > 0)
                return new TsTupleType(named.TypeArguments.Select(Map).ToList());

            // IGrouping<K,V> → Grouping<K,V> (from @meta-sharp/runtime)
            if (fullName.StartsWith("System.Linq.IGrouping") && named.TypeArguments.Length >= 2)
                return new TsNamedType("Grouping", [Map(named.TypeArguments[0]), Map(named.TypeArguments[1])]);

            // Collections → T[]
            if (IsCollectionLike(named) && named.TypeArguments.Length > 0)
                return new TsArrayType(Map(named.TypeArguments[0]));

            // Generic type with type arguments → preserve them
            if (named.TypeArguments.Length > 0)
            {
                var args = named.TypeArguments.Select(Map).ToList();
                return new TsNamedType(named.Name, args);
            }

            // Non-generic named type
            return new TsNamedType(named.Name);
        }

        // Type parameters (T, K, V) — reference to a declared type parameter
        if (type is ITypeParameterSymbol typeParam)
            return new TsNamedType(typeParam.Name);

        // Array types
        if (type is IArrayTypeSymbol array)
            return new TsArrayType(Map(array.ElementType));

        return new TsAnyType();
    }

    /// <summary>
    /// Maps return types for methods/functions that use C# iterator syntax (yield).
    /// For iterator methods, IEnumerable&lt;T&gt;/IEnumerator&lt;T&gt; should become Generator&lt;T&gt;.
    /// Falls back to default mapping for all other types.
    /// </summary>
    public static TsType MapForGeneratorReturn(ITypeSymbol type)
    {
        if (type is INamedTypeSymbol named)
        {
            var fullName = named.ToDisplayString();
            if (
                (fullName.StartsWith("System.Collections.Generic.IEnumerable")
                    || fullName.StartsWith("System.Collections.Generic.IEnumerator"))
                && named.TypeArguments.Length > 0
            )
            {
                return new TsNamedType("Generator", [Map(named.TypeArguments[0])]);
            }
        }

        return Map(type);
    }

    /// <summary>
    /// Wraps a type as T | null. Avoids double-wrapping if already nullable.
    /// </summary>
    private static TsType MakeNullable(TsType inner)
    {
        // Already a union containing null? Don't wrap again
        if (inner is TsUnionType union && union.Types.Any(t => t is TsNamedType { Name: "null" }))
            return inner;

        return new TsUnionType([inner, new TsNamedType("null")]);
    }

    private static bool IsTaskLike(INamedTypeSymbol type)
    {
        var fullName = type.ToDisplayString();
        return fullName.StartsWith("System.Threading.Tasks.Task")
            || fullName.StartsWith("System.Threading.Tasks.ValueTask");
    }

    private static bool IsDictionaryLike(INamedTypeSymbol type)
    {
        var fullName = type.ToDisplayString();
        return fullName.StartsWith("System.Collections.Generic.Dictionary")
            || fullName.StartsWith("System.Collections.Generic.IDictionary")
            || fullName.StartsWith("System.Collections.Generic.IReadOnlyDictionary")
            || fullName.StartsWith("System.Collections.Concurrent.ConcurrentDictionary");
    }

    private static bool IsSetLike(INamedTypeSymbol type)
    {
        var fullName = type.ToDisplayString();
        return fullName.StartsWith("System.Collections.Generic.HashSet")
            || fullName.StartsWith("System.Collections.Generic.ISet")
            || fullName.StartsWith("System.Collections.Generic.SortedSet")
            || fullName.StartsWith("System.Collections.Immutable.ImmutableHashSet");
    }

    /// <summary>
    /// Returns true if a named type maps to a Temporal type and needs the polyfill import.
    /// </summary>
    public static bool NeedsTemporalImport(ITypeSymbol type)
    {
        if (type is not INamedTypeSymbol named) return false;
        var fullName = named.ToDisplayString();
        return fullName is "System.DateTime" or "System.DateTimeOffset"
            or "System.DateOnly" or "System.TimeOnly" or "System.TimeSpan";
    }

    private static bool IsCollectionLike(INamedTypeSymbol type)
    {
        var fullName = type.ToDisplayString();
        return fullName.StartsWith("System.Collections.Generic.List")
            || fullName.StartsWith("System.Collections.Generic.IList")
            || fullName.StartsWith("System.Collections.Generic.IReadOnlyList")
            || fullName.StartsWith("System.Collections.Generic.IReadOnlyCollection")
            || fullName.StartsWith("System.Collections.Generic.IEnumerable")
            || fullName.StartsWith("System.Collections.Generic.ICollection")
            || fullName.StartsWith("System.Collections.Immutable.ImmutableList")
            || fullName.StartsWith("System.Collections.Immutable.ImmutableArray");
    }
}
