using Metano.Compiler.IR;
using Metano.TypeScript.AST;

namespace Metano.TypeScript.Bridge;

/// <summary>
/// The TypeScript-specific half of type mapping: converts the target-agnostic
/// <see cref="IrTypeRef"/> to a <see cref="TsType"/> suitable for the TypeScript backend.
/// A Dart/Kotlin target would implement its own analog.
/// </summary>
public static class IrToTsTypeMapper
{
    public static TsType Map(IrTypeRef type, IrToTsTypeOverrides? overrides = null) =>
        overrides?.TryResolve(type) is { } overridden ? overridden : MapCore(type, overrides);

    private static TsType MapCore(IrTypeRef type, IrToTsTypeOverrides? overrides) =>
        type switch
        {
            IrPrimitiveTypeRef p => MapPrimitive(p.Primitive),
            IrNullableTypeRef n => MakeNullable(Map(n.Inner, overrides)),
            IrArrayTypeRef a => new TsArrayType(Map(a.ElementType, overrides)),
            IrMapTypeRef m => new TsNamedType(
                "Map",
                [Map(m.KeyType, overrides), Map(m.ValueType, overrides)]
            ),
            IrSetTypeRef s => new TsNamedType("HashSet", [Map(s.ElementType, overrides)]),
            IrTupleTypeRef t => new TsTupleType(t.Elements.Select(e => Map(e, overrides)).ToList()),
            IrFunctionTypeRef f => new TsFunctionType(
                f.Parameters.Select(p => MapParameter(p, overrides)).ToList(),
                Map(f.ReturnType, overrides),
                f.ThisType is null ? null : Map(f.ThisType, overrides)
            ),
            IrPromiseTypeRef pr => new TsPromiseType(Map(pr.ResultType, overrides)),
            IrGeneratorTypeRef g => new TsNamedType("Generator", [Map(g.YieldType, overrides)]),
            IrIterableTypeRef i => new TsNamedType("Iterable", [Map(i.ElementType, overrides)]),
            IrKeyValuePairTypeRef kv => new TsTupleType([
                Map(kv.KeyType, overrides),
                Map(kv.ValueType, overrides),
            ]),
            IrGroupingTypeRef gr => new TsNamedType(
                "Grouping",
                [Map(gr.KeyType, overrides), Map(gr.ElementType, overrides)]
            ),
            IrTypeParameterRef tp => new TsNamedType(tp.Name),
            IrNamedTypeRef named => MapNamed(named, overrides),
            IrUnknownTypeRef => new TsAnyType(),
            _ => new TsAnyType(),
        };

    /// <summary>
    /// Optional name rewriter consulted by <see cref="MapNamed"/>.
    /// Set by <see cref="Metano.Transformation.TypeTransformer"/>
    /// when <c>--strip-interface-prefix</c> is active so every
    /// named-type reference picks up the stripped identifier at the
    /// emit boundary. Uses <see cref="AsyncLocal{T}"/> so the flow
    /// state stays scoped to the transformer's execution context —
    /// parallel test runs (TUnit default) each see their own value
    /// instead of racing on a process-wide static slot.
    /// </summary>
    private static readonly AsyncLocal<IReadOnlyDictionary<string, string>?> _namedTypeRenames =
        new();

    internal static IReadOnlyDictionary<string, string>? NamedTypeRenames
    {
        get => _namedTypeRenames.Value;
        set => _namedTypeRenames.Value = value;
    }

    private static TsType MapNamed(IrNamedTypeRef named, IrToTsTypeOverrides? overrides)
    {
        var name = named.Name;
        if (NamedTypeRenames is { } renames && renames.TryGetValue(name, out var renamed))
            name = renamed;

        TsTypeOrigin? origin = named.Origin is { } o ? BuildTsOrigin(o, name) : null;

        IReadOnlyList<TsType>? args = named.TypeArguments is { Count: > 0 } ta
            ? ta.Select(t => Map(t, overrides)).ToList()
            : null;

        return new TsNamedType(name, args, origin);
    }

    /// <summary>
    /// Converts a semantic <see cref="IrTypeOrigin"/> to a <see cref="TsTypeOrigin"/> whose
    /// <c>SubPath</c> is a kebab-case, namespace-relative path — matching what
    /// <c>TypeMapper.ResolveOrigin</c> produces on the legacy path.
    /// </summary>
    private static TsTypeOrigin BuildTsOrigin(IrTypeOrigin origin, string typeName)
    {
        var subPath = Metano.Transformation.PathNaming.ComputeSubPath(
            origin.AssemblyRootNamespace ?? "",
            origin.Namespace ?? "",
            typeName
        );
        return new TsTypeOrigin(origin.PackageId, subPath);
    }

    private static TsType MapPrimitive(IrPrimitive primitive) =>
        primitive switch
        {
            IrPrimitive.Boolean => new TsBooleanType(),
            IrPrimitive.Byte => new TsNumberType(),
            IrPrimitive.Int16 => new TsNumberType(),
            IrPrimitive.Int32 => new TsNumberType(),
            IrPrimitive.Int64 => new TsNumberType(),
            IrPrimitive.Float32 => new TsNumberType(),
            IrPrimitive.Float64 => new TsNumberType(),
            IrPrimitive.Decimal => new TsNumberType(),
            IrPrimitive.BigInteger => new TsBigIntType(),
            IrPrimitive.String => new TsStringType(),
            IrPrimitive.Char => new TsStringType(),
            IrPrimitive.Void => new TsVoidType(),
            IrPrimitive.Object => new TsNamedType("unknown"),
            IrPrimitive.Guid => new TsNamedType("UUID"),
            IrPrimitive.DateTime => new TsNamedType("Temporal.PlainDateTime"),
            IrPrimitive.DateTimeOffset => new TsNamedType("Temporal.ZonedDateTime"),
            IrPrimitive.DateOnly => new TsNamedType("Temporal.PlainDate"),
            IrPrimitive.TimeOnly => new TsNamedType("Temporal.PlainTime"),
            IrPrimitive.TimeSpan => new TsNamedType("Temporal.Duration"),
            _ => new TsAnyType(),
        };

    private static TsParameter MapParameter(IrParameter param, IrToTsTypeOverrides? overrides) =>
        new(IrToTsNamingPolicy.ToParameterName(param.Name), Map(param.Type, overrides));

    private static TsType MakeNullable(TsType inner)
    {
        if (inner is TsUnionType union && union.Types.Any(t => t is TsNamedType { Name: "null" }))
            return inner;

        return new TsUnionType([inner, new TsNamedType("null")]);
    }
}
