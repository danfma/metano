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
    /// <summary>
    /// The runtime classes of the BCL exceptions for the running transform,
    /// which name a BCL exception in type position (spec 011 FR-014).
    /// <see cref="AsyncLocal{T}"/> keeps the slot per execution context so
    /// parallel test runs don't race. Reading it outside a transform is an
    /// internal error: no bridge may lower an exception without the mapping.
    /// </summary>
    private static readonly AsyncLocal<BclExceptionClasses?> _exceptionClasses = new();

    internal static BclExceptionClasses? ExceptionClassesScope
    {
        get => _exceptionClasses.Value;
        set => _exceptionClasses.Value = value;
    }

    internal static BclExceptionClasses ExceptionClasses =>
        _exceptionClasses.Value
        ?? throw new InvalidOperationException(
            "Internal error: a BCL exception was lowered outside a TypeScript transform, so "
                + "the runtime exception classes are unknown."
        );

    /// <summary>
    /// The TypeScript type of <paramref name="type"/>. Without explicit
    /// <paramref name="overrides"/>, the running transform's
    /// <c>[ExportFromBcl]</c> overrides (<see cref="BclOverrides"/>) apply, so a
    /// position outside class emission (a module function, a companion, a
    /// plain object, a branded type) spells a BCL-exported type
    /// (<c>decimal</c> → <c>Decimal</c>) as its runtime value and as the class
    /// emitter does (#525).
    /// </summary>
    public static TsType Map(IrTypeRef type, IrToTsTypeOverrides? overrides = null)
    {
        var effective = overrides ?? BclOverrides;

        return effective?.TryResolve(type) is { } overridden
            ? overridden
            : MapCore(type, effective);
    }

    private static TsType MapCore(IrTypeRef type, IrToTsTypeOverrides? overrides) =>
        type switch
        {
            IrPrimitiveTypeRef p => MapPrimitive(p.Primitive),
            IrNullableTypeRef n => MakeNullable(Map(n.Inner, overrides)),
            IrCollectionTypeRef c => MapCollection(c, overrides),
            IrArrayTypeRef a => new TsArrayType(Map(a.ElementType, overrides), a.IsReadOnly),
            IrMapTypeRef m => new TsNamedType(
                "Map",
                [Map(m.KeyType, overrides), Map(m.ValueType, overrides)]
            ),
            IrSetTypeRef s => RuntimeClassType(
                RuntimeCollections.NameOf(IrCollectionKind.HashSet),
                [Map(s.ElementType, overrides)]
            ),
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
            // A KeyValuePair is immutable, and the runtime dictionaries yield
            // `readonly [K, V]`: the read-only tuple accepts both shapes.
            IrKeyValuePairTypeRef kv => new TsTupleType(
                [Map(kv.KeyType, overrides), Map(kv.ValueType, overrides)],
                IsReadonly: true
            ),
            IrGroupingTypeRef gr => new TsNamedType(
                "Grouping",
                [Map(gr.KeyType, overrides), Map(gr.ElementType, overrides)],
                RuntimeHelperBinding: "Grouping"
            ),
            IrTypeParameterRef tp => new TsNamedType(tp.Name),
            IrNamedTypeRef named => MapNamed(named, overrides),
            IrUnknownTypeRef or IrDynamicTypeRef => new TsAnyType(),
            OutResultTypeRef outResult => MapOutResult(outResult, overrides),
            _ => new TsAnyType(),
        };

    /// <summary>
    /// A BCL collection as its <c>metano-runtime</c> class or interface
    /// (spec 011 FR-001), bound like every class-shaped runtime helper so a
    /// colliding user name aliases it (#484).
    /// </summary>
    private static TsNamedType MapCollection(IrCollectionTypeRef c, IrToTsTypeOverrides? overrides)
    {
        return RuntimeClassType(
            RuntimeCollections.NameOf(c.Kind),
            c.TypeArguments.Select(argument => Map(argument, overrides)).ToList()
        );
    }

    /// <summary>A collections runtime class or interface, bound as a runtime helper (#484).</summary>
    private static TsNamedType RuntimeClassType(string name, IReadOnlyList<TsType> typeArguments) =>
        new(name, typeArguments, RuntimeHelperBinding: name);

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
    /// The <c>[ExportFromBcl]</c> overrides of the running transform, consulted by
    /// every bridge site that lowers a BCL-exported type in expression position
    /// (<c>new Decimal(…)</c>, the <c>1.5m</c> literal, the field zero) to attach
    /// the export's cross-package origin (issue #477). <see cref="AsyncLocal{T}"/>
    /// keeps the slot per execution context so parallel test runs don't race;
    /// <c>null</c> outside a transform keeps the pre-origin bare identifiers.
    /// </summary>
    private static readonly AsyncLocal<BclExportTypeOverrides?> _bclOverrides = new();

    internal static BclExportTypeOverrides? BclOverrides
    {
        get => _bclOverrides.Value;
        set => _bclOverrides.Value = value;
    }

    /// <summary>
    /// Returns the alias the type <paramref name="canonicalName"/> of
    /// <paramref name="namespace"/> prints as in the current emitted file, otherwise
    /// the canonical name itself (<see cref="UsingAliasScope.AliasOf"/>). Used at every
    /// emit site that produces a type-name token — declarations, body-position
    /// identifiers, and static-member access roots — so the alias replaces the
    /// canonical consistently.
    /// </summary>
    public static string ResolveAliasedName(string canonicalName, string? @namespace) =>
        UsingAliases?.AliasOf(canonicalName, @namespace) ?? canonicalName;

    private static bool IsSystemType(IrNamedTypeRef named) =>
        named is { Name: "Type", Namespace: "System", Origin: null }
        && named.Semantics is null or { IsTranspilable: false };

    private static TsType MapNamed(IrNamedTypeRef named, IrToTsTypeOverrides? overrides)
    {
        // BCL exceptions (System.Exception and its non-transpilable
        // subclasses) have no TS declaration; they name their runtime class.
        if (BclExceptionClasses.IsBclException(named))
            return ExceptionClasses.ClassOf(named).TypeReference();

        // `System.Type` is a runtime type descriptor (spec 011 FR-025).
        if (IsSystemType(named))
            return new TsNamedType("RuntimeType", RuntimeHelperBinding: "RuntimeType");

        if (
            named is
            {
                Namespace: "System.Collections.Generic",
                Name: "IComparer" or "IEqualityComparer",
                Origin: null,
                TypeArguments: { Count: 1 } arguments,
            }
        )
            return RuntimeClassType(
                named.Name,
                arguments.Select(argument => Map(argument, overrides)).ToList()
            );

        // `ILookup<K, T>` is the runtime `Lookup` class `ToLookup` returns (#747).
        if (
            named is
            {
                Namespace: "System.Linq",
                Name: "ILookup",
                Origin: null,
                TypeArguments: { Count: 2 } lookupArguments,
            }
        )
            return RuntimeClassType(
                "Lookup",
                lookupArguments.Select(argument => Map(argument, overrides)).ToList()
            );

        var name = ModuleLocalNaming.Flatten(named.Name, named.PrivateNestedSegments);

        if (NamedTypeRenames is { } renames && renames.TryGetValue(name, out var renamed))
            name = renamed;

        string? originalName = null;

        if (UsingAliases?.AliasOf(name, named.Namespace) is { } aliasName)
        {
            originalName = name;
            name = aliasName;
        }

        TsTypeOrigin? origin = ImportableOrigin(named) is { } o
            ? BuildTsOrigin(o, originalName ?? name)
            : null;

        IReadOnlyList<TsType>? args = AllTypeArgumentsOf(named) is { Count: > 0 } ta
            ? ta.Select(t => Map(t, overrides)).ToList()
            : null;

        return new TsNamedType(name, args, origin, originalName);
    }

    /// <summary>
    /// The type arguments a reference to <paramref name="named"/> passes: those of
    /// its generic containers, which its declaration binds as its own leading type
    /// parameters (#436), then its own.
    /// </summary>
    internal static IReadOnlyList<IrTypeRef>? AllTypeArgumentsOf(IrNamedTypeRef named) =>
        named.ContainerTypeArguments is { } container
            ? [.. container, .. named.TypeArguments ?? []]
            : named.TypeArguments;

    /// <summary>
    /// Resolves the cross-package <see cref="TsTypeOrigin"/> for a type reference, or
    /// <see langword="null"/> when the type is intra-project (its <c>Origin</c> is
    /// null). Lets a JSX component tag from a referenced <c>[EmitPackage]</c> assembly
    /// be routed through the cross-package import channel instead of a wrong
    /// intra-project import.
    /// </summary>
    public static TsTypeOrigin? TryResolveOrigin(IrTypeRef type) =>
        type is IrNamedTypeRef named && ImportableOrigin(named) is { } origin
            ? BuildTsOrigin(origin, named.Name)
            : null;

    /// <summary>
    /// The cross-package origin a reference to <paramref name="named"/> imports from, or
    /// <see langword="null"/> when it imports nothing: an <c>[External]</c> type is
    /// ambient (a runtime global such as a DOM interface) wherever it is referenced, so a
    /// referenced package's declaration gets no import, the same as one in the declaring
    /// project, and its package is not added as a dependency for it.
    /// </summary>
    private static IrTypeOrigin? ImportableOrigin(IrNamedTypeRef named) =>
        named.Semantics is { IsExternal: true } ? null : named.Origin;

    /// <summary>
    /// Resolves the cross-package <see cref="TsTypeOrigin"/> of a value-position type
    /// reference (<see cref="IrTypeReference"/>), or <see langword="null"/> when it is
    /// intra-project, BCL or synthesized.
    /// </summary>
    public static TsTypeOrigin? TryResolveOrigin(IrTypeReference reference) =>
        reference is { IsExternal: false, Origin: { } origin }
            ? BuildTsOrigin(origin, reference.Name)
            : null;

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
            IrPrimitive.Int64 or IrPrimitive.UInt64 => SixtyFourBitIntegerType(),
            IrPrimitive.Float32 => new TsNumberType(),
            IrPrimitive.Float64 => new TsNumberType(),
            IrPrimitive.Decimal => new TsNumberType(),
            IrPrimitive.BigInteger => new TsBigIntType(),
            IrPrimitive.String => new TsStringType(),
            IrPrimitive.Char => new TsStringType(),
            IrPrimitive.Void => new TsVoidType(),
            IrPrimitive.Object => new TsNamedType("unknown"),
            IrPrimitive.Guid => new TsNamedType("UUID", RuntimeHelperBinding: "UUID"),
            IrPrimitive.DateTime => new TsNamedType("Temporal.PlainDateTime"),
            IrPrimitive.DateTimeOffset => new TsNamedType("Temporal.ZonedDateTime"),
            IrPrimitive.DateOnly => new TsNamedType("Temporal.PlainDate"),
            IrPrimitive.TimeOnly => new TsNamedType("Temporal.PlainTime"),
            IrPrimitive.TimeSpan => new TsNamedType("Temporal.Duration"),
            _ => new TsAnyType(),
        };

    /// <summary>
    /// <c>long</c> / <c>ulong</c>: a native <c>bigint</c> (spec 012 FR-002).
    /// </summary>
    private static TsType SixtyFourBitIntegerType() => new TsBigIntType();

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
            MapParameterType(param, overrides),
            Rest: param.IsRestParams
        );

    /// <summary>
    /// The TS type of a parameter's annotation. A <c>params</c> parameter lowers to
    /// a rest parameter, which must have an array type (TS2370) and never holds
    /// <c>null</c>: a call that passes a null array drops the argument (reported as
    /// MS0001), so the callee receives an empty array. A nullable <c>params T[]?</c>
    /// therefore drops its nullability (<c>...xs: T[]</c>, not
    /// <c>...xs: T[] | null</c>).
    /// </summary>
    public static TsType MapParameterType(
        IrParameter parameter,
        IrToTsTypeOverrides? overrides = null
    ) => Map(RestSafeTypeOf(parameter), overrides);

    private static IrTypeRef RestSafeTypeOf(IrParameter parameter) =>
        parameter is { IsRestParams: true, Type: IrNullableTypeRef nullable }
            ? nullable.Inner
            : parameter.Type;

    /// <summary><c>inner | null</c>, unless <paramref name="inner"/> already
    /// admits <c>null</c>.</summary>
    internal static TsType MakeNullable(TsType inner)
    {
        if (inner is TsUnionType union && union.Types.Any(t => t is TsNamedType { Name: "null" }))
            return inner;

        return new TsUnionType([inner, new TsNamedType("null")]);
    }
}
