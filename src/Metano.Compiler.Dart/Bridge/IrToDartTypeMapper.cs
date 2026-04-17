using Metano.Compiler.IR;
using Metano.Dart.AST;

namespace Metano.Dart.Bridge;

/// <summary>
/// Maps target-agnostic <see cref="IrTypeRef"/> values to Dart type references.
/// Follows idiomatic Dart mappings:
/// <list type="bullet">
///   <item><c>IrPrimitive.Guid</c> → <c>String</c> (Dart has no built-in UUID; the branded
///   distinction exists only in TypeScript).</item>
///   <item><c>IrPrimitive.DateTime*</c> → <c>DateTime</c>.</item>
///   <item><c>IrPrimitive.TimeSpan</c> → <c>Duration</c>.</item>
///   <item><c>IrArrayTypeRef</c> → <c>List&lt;T&gt;</c>.</item>
///   <item><c>IrTupleTypeRef</c> → Dart 3 record type <c>(T1, T2)</c>.</item>
///   <item><c>IrPromiseTypeRef</c> → <c>Future&lt;T&gt;</c>.</item>
/// </list>
/// </summary>
public static class IrToDartTypeMapper
{
    public static DartType Map(IrTypeRef type) =>
        type switch
        {
            IrPrimitiveTypeRef p => MapPrimitive(p.Primitive),
            IrNullableTypeRef n => new DartNullableType(Map(n.Inner)),
            IrArrayTypeRef a => new DartNamedType("List", [Map(a.ElementType)]),
            IrMapTypeRef m => new DartNamedType("Map", [Map(m.KeyType), Map(m.ValueType)]),
            IrSetTypeRef s => new DartNamedType("Set", [Map(s.ElementType)]),
            IrTupleTypeRef t => new DartRecordType(t.Elements.Select(Map).ToList()),
            IrKeyValuePairTypeRef kv => new DartNamedType(
                "MapEntry",
                [Map(kv.KeyType), Map(kv.ValueType)]
            ),
            IrFunctionTypeRef f => new DartFunctionType(
                f.Parameters.Select(p => new DartParameter(p.Name, Map(p.Type))).ToList(),
                Map(f.ReturnType)
            ),
            IrPromiseTypeRef pr => new DartNamedType("Future", [Map(pr.ResultType)]),
            IrGeneratorTypeRef g => new DartNamedType("Iterable", [Map(g.YieldType)]),
            IrIterableTypeRef i => new DartNamedType("Iterable", [Map(i.ElementType)]),
            IrGroupingTypeRef gr => new DartNamedType(
                "MapEntry",
                [Map(gr.KeyType), new DartNamedType("List", [Map(gr.ElementType)])]
            ),
            IrTypeParameterRef tp => new DartNamedType(tp.Name),
            IrNamedTypeRef named => MapNamed(named),
            IrUnknownTypeRef => new DartNamedType("dynamic"),
            _ => new DartNamedType("dynamic"),
        };

    private static DartType MapNamed(IrNamedTypeRef named)
    {
        var origin = named.Origin is { } o
            ? new DartTypeOrigin(ToDartPackage(o.PackageId), BuildDartFilePath(o, named.Name))
            : null;

        var args = named.TypeArguments is { Count: > 0 } ta
            ? (IReadOnlyList<DartType>)ta.Select(Map).ToList()
            : null;

        return new DartNamedType(named.Name, args, origin);
    }

    private static DartType MapPrimitive(IrPrimitive primitive) =>
        primitive switch
        {
            IrPrimitive.Boolean => new DartNamedType("bool"),
            IrPrimitive.Byte or IrPrimitive.Int16 or IrPrimitive.Int32 or IrPrimitive.Int64 =>
                new DartNamedType("int"),
            IrPrimitive.Float32 or IrPrimitive.Float64 => new DartNamedType("double"),
            IrPrimitive.Decimal => new DartNamedType("Decimal"), // package:decimal
            IrPrimitive.BigInteger => new DartNamedType("BigInt"),
            IrPrimitive.String or IrPrimitive.Char => new DartNamedType("String"),
            IrPrimitive.Void => new DartNamedType("void"),
            IrPrimitive.Object => new DartNamedType("Object"),
            IrPrimitive.Guid => new DartNamedType("String"),
            IrPrimitive.DateTime
            or IrPrimitive.DateTimeOffset
            or IrPrimitive.DateOnly
            or IrPrimitive.TimeOnly => new DartNamedType("DateTime"),
            IrPrimitive.TimeSpan => new DartNamedType("Duration"),
            _ => new DartNamedType("dynamic"),
        };

    /// <summary>
    /// Converts the logical package ID (matching a Dart pub package name) to its Dart
    /// identifier. Dart package names are lowercase_with_underscores, so we normalize here.
    /// </summary>
    private static string ToDartPackage(string packageId) =>
        packageId.Replace('-', '_').ToLowerInvariant();

    /// <summary>
    /// Builds the <c>lib/...</c>-style relative path a Dart import uses. Dart files are
    /// snake_case, so we convert the type name + any namespace-derived segments.
    /// </summary>
    private static string BuildDartFilePath(IrTypeOrigin origin, string typeName)
    {
        var parts = new List<string>();
        if (
            origin.AssemblyRootNamespace is not null
            && origin.Namespace is not null
            && origin.Namespace.Length > origin.AssemblyRootNamespace.Length
            && origin.Namespace.StartsWith(origin.AssemblyRootNamespace + ".")
        )
        {
            var relative = origin.Namespace[(origin.AssemblyRootNamespace.Length + 1)..];
            foreach (var segment in relative.Split('.'))
                parts.Add(ToSnakeCase(segment));
        }
        parts.Add(ToSnakeCase(typeName));
        return string.Join("/", parts);
    }

    private static string ToSnakeCase(string pascal)
    {
        if (pascal.Length == 0)
            return pascal;
        var sb = new System.Text.StringBuilder(pascal.Length + 4);
        sb.Append(char.ToLowerInvariant(pascal[0]));
        for (var i = 1; i < pascal.Length; i++)
        {
            var c = pascal[i];
            if (char.IsUpper(c))
            {
                sb.Append('_');
                sb.Append(char.ToLowerInvariant(c));
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }
}
