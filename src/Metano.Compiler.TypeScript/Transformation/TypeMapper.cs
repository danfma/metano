using Metano.Compiler;
using Metano.TypeScript;
using Metano.TypeScript.AST;
using Microsoft.CodeAnalysis;

namespace Metano.Transformation;

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
    private static Dictionary<
        string,
        (string ExportedName, string FromPackage, string Version)
    >? _bclExportMap;

    public static Dictionary<
        string,
        (string ExportedName, string FromPackage, string Version)
    > BclExportMap
    {
        get => _bclExportMap ??= [];
        set => _bclExportMap = value;
    }

    /// <summary>
    /// Cross-assembly type origins (set by TypeTransformer before transpilation). When a
    /// referenced type's symbol is in this map, the produced <see cref="TsNamedType"/>
    /// carries an <see cref="TsTypeOrigin"/> so the import collector can emit a
    /// cross-package import statement of the form
    /// <c>import { Foo } from "&lt;package&gt;/&lt;subpath&gt;"</c>.
    /// </summary>
    [ThreadStatic]
    private static Dictionary<ISymbol, CrossAssemblyEntry>? _crossAssemblyTypeMap;

    public static Dictionary<ISymbol, CrossAssemblyEntry> CrossAssemblyTypeMap
    {
        get => _crossAssemblyTypeMap ??= new(SymbolEqualityComparer.Default);
        set => _crossAssemblyTypeMap = value;
    }

    /// <summary>
    /// Assemblies that declare <c>[TranspileAssembly]</c> but lack
    /// <c>[EmitPackage(JavaScript)]</c>. When the type mapper encounters a type whose
    /// containing assembly is in this set, it adds the type's display name to
    /// <see cref="CrossPackageMisses"/> so the transformer can raise MS0007.
    /// </summary>
    [ThreadStatic]
    private static HashSet<IAssemblySymbol>? _assembliesNeedingEmitPackage;

    public static HashSet<IAssemblySymbol> AssembliesNeedingEmitPackage
    {
        get => _assembliesNeedingEmitPackage ??= new(SymbolEqualityComparer.Default);
        set => _assembliesNeedingEmitPackage = value;
    }

    /// <summary>
    /// Display names of cross-assembly types whose containing assembly lacks
    /// <c>[EmitPackage]</c>. The transformer drains this set after each transformation
    /// pass and raises one MS0007 per unique entry, telling the user which producing
    /// assembly needs the missing attribute.
    /// </summary>
    [ThreadStatic]
    private static HashSet<string>? _crossPackageMisses;

    public static HashSet<string> CrossPackageMisses
    {
        get => _crossPackageMisses ??= new();
        set => _crossPackageMisses = value;
    }

    /// <summary>
    /// Package names that were actually referenced during transformation, mapped to a
    /// pre-formatted npm version specifier (e.g., <c>^1.2.3</c> or <c>workspace:*</c>).
    /// Populated by three paths:
    /// <list type="bullet">
    ///   <item><see cref="ResolveOrigin"/> for cross-assembly types — version comes
    ///   from <c>IAssemblySymbol.Identity.Version</c></item>
    ///   <item><see cref="Map"/> for <c>[ExportFromBcl]</c> types whose mapping
    ///   declares a <c>Version</c></item>
    ///   <item><see cref="Transformation.ImportCollector"/> for <c>[Import]</c> types
    ///   whose mapping declares a <c>Version</c></item>
    /// </list>
    /// The transformer reads this after <c>TransformAll</c> to compute the
    /// auto-generated <c>dependencies</c> entries for the consumer's <c>package.json</c>.
    /// </summary>
    [ThreadStatic]
    private static Dictionary<string, string>? _usedCrossPackages;

    public static Dictionary<string, string> UsedCrossPackages
    {
        get => _usedCrossPackages ??= new();
        set => _usedCrossPackages = value;
    }

    /// <summary>
    /// Formats an assembly's version as an npm-compatible specifier. Assemblies that
    /// don't declare a version (Roslyn defaults to <c>0.0.0.0</c>) get
    /// <c>workspace:*</c>, which is the right call for sibling projects in a Bun
    /// monorepo. Anything with a real version becomes <c>^Major.Minor.Patch</c>.
    /// </summary>
    public static string FormatAssemblyVersion(IAssemblySymbol assembly)
    {
        var v = assembly.Identity.Version;
        if (v.Major == 0 && v.Minor == 0 && v.Build <= 0)
            return "workspace:*";
        var build = v.Build > 0 ? v.Build : 0;
        return $"^{v.Major}.{v.Minor}.{build}";
    }

    public static TsType Map(ITypeSymbol type)
    {
        // Nullable<T> (value types: int?, bool?, etc.) → T | null
        if (
            type is INamedTypeSymbol
            {
                OriginalDefinition.SpecialType: SpecialType.System_Nullable_T
            } nullable
        )
        {
            var inner = Map(nullable.TypeArguments[0]);
            return MakeNullable(inner);
        }

        // Nullable reference types (string?, Money?, etc.) → T | null
        if (
            type.NullableAnnotation == NullableAnnotation.Annotated
            && type.OriginalDefinition.SpecialType != SpecialType.System_Nullable_T
        )
        {
            var inner = Map(type.WithNullableAnnotation(NullableAnnotation.NotAnnotated));
            return MakeNullable(inner);
        }

        // Check BCL export map first (overrides hardcoded mappings)
        if (
            type is INamedTypeSymbol bclType
            && BclExportMap.TryGetValue(bclType.ToDisplayString(), out var bclExport)
        )
        {
            // Track for auto-deps generation when the mapping declares a Version. The
            // package name is the npm package the user will need to install.
            if (bclExport.Version.Length > 0 && bclExport.FromPackage.Length > 0)
                UsedCrossPackages[bclExport.FromPackage] = bclExport.Version;
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
            if (fullName is "System.DateTime")
                return new TsNamedType("Temporal.PlainDateTime");
            if (fullName is "System.DateTimeOffset")
                return new TsNamedType("Temporal.ZonedDateTime");
            if (fullName is "System.DateOnly")
                return new TsNamedType("Temporal.PlainDate");
            if (fullName is "System.TimeOnly")
                return new TsNamedType("Temporal.PlainTime");
            if (fullName is "System.TimeSpan")
                return new TsNamedType("Temporal.Duration");

            // Simple mappings
            if (fullName is "System.Guid")
                return new TsStringType();
            if (fullName is "System.Uri")
                return new TsStringType();
            if (fullName is "System.Object")
                return new TsNamedType("unknown");

            // Dictionary → Map<K, V>
            if (IsDictionaryLike(named) && named.TypeArguments.Length >= 2)
                return new TsNamedType(
                    "Map",
                    [Map(named.TypeArguments[0]), Map(named.TypeArguments[1])]
                );

            // HashSet / ISet → HashSet<T> (from metano-runtime, respects equals/hashCode)
            if (IsSetLike(named) && named.TypeArguments.Length > 0)
                return new TsNamedType("HashSet", [Map(named.TypeArguments[0])]);

            // KeyValuePair<K,V> → [K, V]
            if (
                fullName.StartsWith("System.Collections.Generic.KeyValuePair")
                && named.TypeArguments.Length >= 2
            )
                return new TsTupleType([Map(named.TypeArguments[0]), Map(named.TypeArguments[1])]);

            // Tuple / ValueTuple → [T1, T2, ...]
            var originalName = named.OriginalDefinition.ToDisplayString();
            if (
                (
                    originalName.StartsWith("System.Tuple")
                    || originalName.StartsWith("System.ValueTuple")
                    || named.IsTupleType
                )
                && named.TypeArguments.Length > 0
            )
                return new TsTupleType(named.TypeArguments.Select(Map).ToList());

            // IGrouping<K,V> → Grouping<K,V> (from metano-runtime)
            if (fullName.StartsWith("System.Linq.IGrouping") && named.TypeArguments.Length >= 2)
                return new TsNamedType(
                    "Grouping",
                    [Map(named.TypeArguments[0]), Map(named.TypeArguments[1])]
                );

            // IReadOnlyCollection<T> → Iterable<T> (compatible with both Array and HashSet)
            if (
                fullName.StartsWith("System.Collections.Generic.IReadOnlyCollection")
                && named.TypeArguments.Length > 0
            )
                return new TsNamedType("Iterable", [Map(named.TypeArguments[0])]);

            // Collections → T[]
            if (IsCollectionLike(named) && named.TypeArguments.Length > 0)
                return new TsArrayType(Map(named.TypeArguments[0]));

            // Generic type with type arguments → preserve them
            if (named.TypeArguments.Length > 0)
            {
                var args = named.TypeArguments.Select(Map).ToList();
                return new TsNamedType(BuildQualifiedName(named), args, ResolveOrigin(named));
            }

            // Non-generic named type
            return new TsNamedType(BuildQualifiedName(named), Origin: ResolveOrigin(named));
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
                (
                    fullName.StartsWith("System.Collections.Generic.IEnumerable")
                    || fullName.StartsWith("System.Collections.Generic.IEnumerator")
                )
                && named.TypeArguments.Length > 0
            )
            {
                return new TsNamedType("Generator", [Map(named.TypeArguments[0])]);
            }
        }

        return Map(type);
    }

    /// <summary>
    /// Returns a <see cref="TsTypeOrigin"/> when <paramref name="named"/> is a type from
    /// a cross-assembly source registered in <see cref="CrossAssemblyTypeMap"/>; null
    /// otherwise. Looking up by symbol identity (via the map's
    /// <see cref="SymbolEqualityComparer"/>) avoids the simple-name ambiguity that a
    /// string-keyed lookup would have when two assemblies declare types with the same
    /// name.
    /// </summary>
    private static TsTypeOrigin? ResolveOrigin(INamedTypeSymbol named)
    {
        // Use the original definition so a closed generic (e.g., MyType<int>) resolves
        // to the same map entry as the open one.
        var key = named.OriginalDefinition;
        if (!CrossAssemblyTypeMap.TryGetValue(key, out var entry))
        {
            // The lookup missed. If the symbol's containing assembly is in the
            // "needs EmitPackage" set, that's the diagnostic case: a transpilable
            // library that hasn't declared its package name. Record the type so the
            // transformer can raise MS0007 once per unique miss.
            var containingAssembly = key.ContainingAssembly;
            if (
                containingAssembly is not null
                && AssembliesNeedingEmitPackage.Contains(containingAssembly)
            )
            {
                CrossPackageMisses.Add(named.ToDisplayString());
            }
            return null;
        }

        var ns = PathNaming.GetNamespace(entry.Symbol);
        // Cross-package imports are namespace-first: resolve to the producer package's
        // namespace barrel, and to the package root when the type lives directly under
        // the assembly root namespace.
        var subPath = PathNaming.ComputeSubPath(entry.AssemblyRootNamespace, ns, entry.Symbol.Name);

        // Track that this package was actually referenced so the transformer can emit
        // a corresponding `dependencies` entry in the consumer's package.json. The
        // version comes from one of two sources, in order of precedence:
        //   1. [EmitPackage(..., Version = "...")] declarative override on the source
        //      assembly (lets the user pin to `workspace:*` for monorepo siblings or
        //      to a specific tag for published packages).
        //   2. The source assembly's Identity.Version, formatted as ^Major.Minor.Patch.
        if (entry.VersionOverride is not null)
            UsedCrossPackages[entry.PackageName] = entry.VersionOverride;
        else if (entry.Symbol.ContainingAssembly is { } sourceAsm)
            UsedCrossPackages[entry.PackageName] = FormatAssemblyVersion(sourceAsm);

        return new TsTypeOrigin(entry.PackageName, subPath);
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

    internal static bool IsDictionaryLike(INamedTypeSymbol type)
    {
        var fullName = type.ToDisplayString();
        return fullName.StartsWith("System.Collections.Generic.Dictionary")
            || fullName.StartsWith("System.Collections.Generic.IDictionary")
            || fullName.StartsWith("System.Collections.Generic.IReadOnlyDictionary")
            || fullName.StartsWith("System.Collections.Concurrent.ConcurrentDictionary");
    }

    internal static bool IsSetLike(INamedTypeSymbol type)
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
        if (type is not INamedTypeSymbol named)
            return false;
        var fullName = named.ToDisplayString();
        return fullName
            is "System.DateTime"
                or "System.DateTimeOffset"
                or "System.DateOnly"
                or "System.TimeOnly"
                or "System.TimeSpan";
    }

    /// <summary>
    /// Builds the TS-side qualified name for a type. Nested types are dotted: `Outer.Inner`.
    /// Top-level types just return their own name.
    /// </summary>
    private static string BuildQualifiedName(INamedTypeSymbol type)
    {
        if (type.ContainingType is null)
            return TypeTransformer.GetTsTypeName(type);
        var parts = new List<string> { TypeTransformer.GetTsTypeName(type) };
        var current = type.ContainingType;
        while (current is not null)
        {
            parts.Insert(0, TypeTransformer.GetTsTypeName(current));
            current = current.ContainingType;
        }
        return string.Join(".", parts);
    }

    internal static bool IsCollectionLike(INamedTypeSymbol type)
    {
        var fullName = type.ToDisplayString();
        return fullName.StartsWith("System.Collections.Generic.List")
            || fullName.StartsWith("System.Collections.Generic.IList")
            || fullName.StartsWith("System.Collections.Generic.IReadOnlyList")
            // IReadOnlyCollection mapped separately to Iterable<T>
            || fullName.StartsWith("System.Collections.Generic.IEnumerable")
            || fullName.StartsWith("System.Collections.Generic.ICollection")
            || fullName.StartsWith("System.Collections.Immutable.ImmutableList")
            || fullName.StartsWith("System.Collections.Immutable.ImmutableArray")
            || fullName.StartsWith("System.Collections.Generic.Queue")
            || fullName.StartsWith("System.Collections.Generic.Stack");
    }
}
