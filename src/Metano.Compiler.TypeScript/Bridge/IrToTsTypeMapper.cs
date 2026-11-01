using Metano.Compiler.IR;
using Metano.Compiler.TypeScript.AST;

namespace Metano.Compiler.TypeScript.Bridge;

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
            IrFunctionTypeRef f => MapFunctionType(f, overrides),
            // Overloaded `[JsCallable]` Invoke → intersection of call signatures.
            // `((v: T) => void) & ((u: (c: T) => T) => void)` — TS reads an
            // intersection of function types as a single value callable with
            // either signature, the structural mirror of an overloaded method.
            IrCallableTypeRef c => new TsIntersectionType(
                c.Signatures.Select(s => (TsType)MapFunctionType(s, overrides)).ToList()
            ),
            IrPromiseTypeRef pr => new TsPromiseType(Map(pr.ResultType, overrides)),
            IrGeneratorTypeRef g => new TsNamedType("Generator", [Map(g.YieldType, overrides)]),
            IrIterableTypeRef i => new TsNamedType("Iterable", [Map(i.ElementType, overrides)]),
            IrIteratorTypeRef i => new TsNamedType("Iterator", [Map(i.ElementType, overrides)]),
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
            OutResultTypeRef outResult => MapOutResult(outResult, overrides),
            _ => new TsAnyType(),
        };

    /// <summary>
    /// Optional name rewriter consulted by <see cref="MapNamed"/>.
    /// Set by <see cref="Metano.Compiler.TypeScript.Transformation.TypeTransformer"/>
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

    /// <summary>
    /// Per-source-file alias map captured from C# <c>using X = Y;</c>
    /// directives. Set by <c>TypeTransformer</c> per emitted file group so
    /// every <see cref="MapNamed"/> call inside that file can substitute
    /// the canonical type name with the user's alias and the import
    /// collector can render the matching <c>{ Original as Alias }</c>
    /// form. <see cref="AsyncLocal{T}"/> keeps the slot per execution
    /// context so parallel test runs don't race.
    /// </summary>
    private static readonly AsyncLocal<UsingAliasScope?> _usingAliases = new();

    internal static UsingAliasScope? UsingAliases
    {
        get => _usingAliases.Value;
        set => _usingAliases.Value = value;
    }

    /// <summary>
    /// Returns the alias for <paramref name="canonicalName"/> if one is
    /// active for the current emitted file, otherwise the canonical name
    /// itself. Used at every emit site that produces a type-name token —
    /// declarations, body-position identifiers, and static-member access
    /// roots — so the alias replaces the canonical consistently.
    /// </summary>
    public static string ResolveAliasedName(string canonicalName) =>
        UsingAliases is { } scope
        && scope.CanonicalToAlias.TryGetValue(canonicalName, out var alias)
            ? alias
            : canonicalName;

    private static TsType MapNamed(IrNamedTypeRef named, IrToTsTypeOverrides? overrides)
    {
        // BCL exceptions (System.Exception and its non-transpilable
        // subclasses) have no TS declaration; their runtime values are
        // `Error` instances, so the type position names the built-in too.
        if (IsBclException(named))
            return new TsNamedType("Error");

        var name = named.Name;
        if (NamedTypeRenames is { } renames && renames.TryGetValue(name, out var renamed))
            name = renamed;

        string? originalName = null;
        if (
            UsingAliases is { } aliasScope
            && aliasScope.CanonicalToAlias.TryGetValue(name, out var aliasName)
        )
        {
            originalName = name;
            name = aliasName;
        }

        TsTypeOrigin? origin = named.Origin is { } o
            ? BuildTsOrigin(o, originalName ?? name)
            : null;

        IReadOnlyList<TsType>? args = named.TypeArguments is { Count: > 0 } ta
            ? ta.Select(t => Map(t, overrides)).ToList()
            : null;

        return new TsNamedType(name, args, origin, originalName);
    }

    /// <summary>
    /// Whether <paramref name="named"/> is an exception type without a TS
    /// declaration (<c>System.Exception</c> and its BCL / non-transpilable
    /// subclasses), whose runtime values are JS <c>Error</c> instances.
    /// </summary>
    internal static bool IsBclException(IrNamedTypeRef named) =>
        named.Semantics is { Kind: IrNamedTypeKind.Exception, IsTranspilable: false };

    /// <summary>
    /// Resolves the cross-package <see cref="TsTypeOrigin"/> for a type reference, or
    /// <see langword="null"/> when the type is intra-project (its <c>Origin</c> is
    /// null). Lets a JSX component tag from a referenced <c>[EmitPackage]</c> assembly
    /// be routed through the cross-package import channel instead of a wrong
    /// intra-project import.
    /// </summary>
    public static TsTypeOrigin? TryResolveOrigin(IrTypeRef type) =>
        type is IrNamedTypeRef { Origin: { } origin } named
            ? BuildTsOrigin(origin, named.Name)
            : null;

    /// <summary>
    /// Resolves the cross-package <see cref="TsTypeOrigin"/> of a value-position type
    /// reference (<see cref="IrTypeReference"/>), or <see langword="null"/> when it is
    /// intra-project, BCL or synthesized.
    /// </summary>
    public static TsTypeOrigin? TryResolveOrigin(IrTypeReference reference) =>
        reference.Origin is { } origin ? BuildTsOrigin(origin, reference.Name) : null;

    /// <summary>
    /// Converts a semantic <see cref="IrTypeOrigin"/> to a <see cref="TsTypeOrigin"/> whose
    /// <c>SubPath</c> is a kebab-case, namespace-relative path — matching what
    /// <c>TypeMapper.ResolveOrigin</c> produces on the legacy path.
    /// </summary>
    private static TsTypeOrigin BuildTsOrigin(IrTypeOrigin origin, string typeName)
    {
        // Full-namespace layout (D1, ADR-0025): the subpath is the referenced type's
        // full namespace, minus the producer's declared exports subpath root (spec 008)
        // so it matches the key that package exports.
        var subPath = Metano.Compiler.TypeScript.Transformation.PathNaming.ComputeExportedSubPath(
            origin.PackageSubpathRoot,
            origin.Namespace ?? ""
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

    /// <summary>
    /// <c>readonly [result: R, value: T]</c>, or for a discriminated result
    /// <c>readonly [found: true, value: V] | readonly [found: false, value: V | null]</c>.
    /// </summary>
    private static TsType MapOutResult(OutResultTypeRef outResult, IrToTsTypeOverrides? overrides)
    {
        var labels = outResult
            .Outs.Select(o => o.Name)
            .Prepend(outResult.Result is null ? null : outResult.ResultLabel)
            .OfType<string>()
            .Select(IrToTsNamingPolicy.ToParameterName)
            .ToList();

        TsTupleType Tuple(TsType? result, Func<IrParameter, IrTypeRef> typeOf)
        {
            var outs = outResult.Outs.Select(o => Map(typeOf(o), overrides));
            var elements = result is null ? outs : outs.Prepend(result);

            return new TsTupleType(elements.ToList(), labels, IsReadonly: true);
        }

        if (!outResult.IsDiscriminated)
        {
            var result = outResult.Result is null ? null : Map(outResult.Result, overrides);

            return Tuple(result, o => o.Type);
        }

        return new TsUnionType([
            Tuple(new TsBooleanLiteralType(true), o => o.OutTypesByResult?.WhenTrue ?? o.Type),
            Tuple(new TsBooleanLiteralType(false), o => o.OutTypesByResult?.WhenFalse ?? o.Type),
        ]);
    }

    private static TsFunctionType MapFunctionType(
        IrFunctionTypeRef f,
        IrToTsTypeOverrides? overrides
    ) =>
        new(
            f.Parameters.Select(p => MapParameter(p, overrides)).ToList(),
            Map(f.ReturnType, overrides),
            f.ThisType is null ? null : Map(f.ThisType, overrides)
        );

    private static TsParameter MapParameter(IrParameter param, IrToTsTypeOverrides? overrides) =>
        new(
            IrToTsNamingPolicy.ToParameterName(param.Name),
            Map(param.Type, overrides),
            Rest: param.IsParams
        );

    private static TsType MakeNullable(TsType inner)
    {
        if (inner is TsUnionType union && union.Types.Any(t => t is TsNamedType { Name: "null" }))
            return inner;

        return new TsUnionType([inner, new TsNamedType("null")]);
    }
}
