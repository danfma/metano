using Metano.Compiler.IR;
using Metano.Compiler.TypeScript.Analysis;
using Metano.Compiler.TypeScript.AST;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// The TypeScript spelling of a runtime type descriptor read (spec 011
/// FR-018–FR-025): <c>C.$runtimeType</c> for a class, <c>X$runtimeType</c>
/// for an interface or an enum (an export of the type's own module),
/// <c>Int32Type</c> and the other <c>metano-runtime</c> constants for the
/// primitives, <c>specialize(C.$runtimeType, [...])</c> for a closed generic
/// class. Every read of a descriptor this compilation declares is checked
/// against the reached set the caller passes (the file group's
/// <see cref="IrToTsBridgeContext.RuntimeTypes"/>): a read outside it is an
/// internal error, so a drift between the reachability phase
/// and the bridges fails loudly instead of emitting a reference to a getter or
/// an export that does not exist (D29).
/// </summary>
internal static class IrToTsRuntimeTypeReferences
{
    /// <summary>The static getter a class descriptor lives on.</summary>
    public const string GetterName = "$runtimeType";

    /// <summary>The suffix of an interface or enum descriptor's export.</summary>
    public const string ExportSuffix = "$runtimeType";

    /// <summary>The suffix of a class descriptor's module-level memo.</summary>
    public const string MemoSuffix = "$rt";

    /// <summary>
    /// The descriptor of <paramref name="type"/>, or <c>null</c> when it has none
    /// (a <c>[PlainObject]</c>, a branded wrapper, a type the analysis reported
    /// as undescribable). <paramref name="site"/> names the construct for the
    /// drift error; <paramref name="reachability"/> is the reached set every
    /// read is checked against (<see cref="RuntimeTypeReachabilityResult.Empty"/>
    /// outside a transform).
    /// </summary>
    public static TsExpression? Of(
        IrTypeRef type,
        string site,
        RuntimeTypeReachabilityResult reachability,
        TsTypeParameterScope? scope = null
    ) =>
        type switch
        {
            _ when type.IsObject => new TsRuntimeHelperReference("ObjectType"),
            IrTypeParameterRef tp => TypeParameterTsResolver.Resolve(tp, scope),
            IrNullableTypeRef { Inner: var inner } => Of(inner, site, reachability, scope)
                is { } innerDescriptor
                ? IsValueType(inner)
                    ? Helper("nullableType", innerDescriptor)
                    : innerDescriptor
                : null,
            IrPrimitiveTypeRef primitive => TsRuntimePrimitive.Of(
                primitive.Primitive,
                primitive.ExactKind
            )
                is { } runtime
                ? new TsRuntimeHelperReference(runtime.Descriptor)
                : null,
            IrArrayTypeRef => new TsRuntimeHelperReference("ArrayType"),
            IrCollectionTypeRef collection => CollectionDescriptor(
                collection,
                site,
                reachability,
                scope
            ),
            IrFunctionTypeRef => new TsRuntimeHelperReference("DelegateType"),
            IrNamedTypeRef named => NamedDescriptor(named, site, reachability, scope),
            _ => null,
        };

    /// <summary>
    /// <c>listType(Int32Type)</c>: the collection factory of the kind over its
    /// arguments' descriptors, a type argument with none reading <c>UnknownType</c>.
    /// <c>null</c> for a kind with no factory (a dictionary view).
    /// </summary>
    private static TsExpression? CollectionDescriptor(
        IrCollectionTypeRef collection,
        string site,
        RuntimeTypeReachabilityResult reachability,
        TsTypeParameterScope? scope
    ) =>
        RuntimeCollections.DescriptorFactoryOf(collection.Kind) is { } factory
            ? new TsCallExpression(
                factory,
                [
                    .. collection.TypeArguments.Select(argument =>
                        SlotArgumentOf(argument, site, reachability, scope)
                    ),
                ]
            )
            : null;

    /// <summary>
    /// The descriptor of a named type; a type argument with no descriptor
    /// specializes to the <c>UnknownType</c> wildcard.
    /// </summary>
    private static TsExpression? NamedDescriptor(
        IrNamedTypeRef named,
        string site,
        RuntimeTypeReachabilityResult reachability,
        TsTypeParameterScope? scope
    )
    {
        if (named.Semantics?.Kind is IrNamedTypeKind.Delegate)
            return new TsRuntimeHelperReference("DelegateType");

        if (BclExceptionClasses.IsBclException(named))
            return IrToTsTypeMapper.ExceptionClasses.IsMapped(named)
                ? new TsPropertyAccess(
                    IrToTsTypeMapper.ExceptionClasses.ClassOf(named).ValueReference(),
                    GetterName
                )
                : null;

        if (!IsDescribable(named, reachability))
            return null;

        AssertReached(named, site, reachability);

        return named.Semantics switch
        {
            { IsPlainObject: true }
            or {
                Kind: IrNamedTypeKind.Interface
                    or IrNamedTypeKind.NumericEnum
                    or IrNamedTypeKind.StringEnum,
            } => ExportReference(named),
            _ => Specialized(named, site, reachability, scope),
        };
    }

    private static TsExpression Specialized(
        IrNamedTypeRef named,
        string site,
        RuntimeTypeReachabilityResult reachability,
        TsTypeParameterScope? scope
    )
    {
        var open = new TsPropertyAccess(ClassReference(named), GetterName);

        if (named.TypeArguments is not { Count: > 0 } arguments)
            return open;

        var descriptors = arguments
            .Select(argument => ArgumentOf(argument, site, reachability, scope))
            .ToList();

        return Helper("specialize", open, new TsArrayLiteral(descriptors));
    }

    /// <summary>
    /// Whether a named type has a descriptor TypeScript can name: a class,
    /// record, struct, interface or enum that is neither a plain object, a
    /// branded wrapper nor a <c>[JsTuple]</c>, and that the analysis did not
    /// report as undescribable in <paramref name="reachability"/>.
    /// </summary>
    public static bool IsDescribable(
        IrNamedTypeRef named,
        RuntimeTypeReachabilityResult reachability
    )
    {
        if (
            named.Semantics
            is not { IsTranspilable: true, IsExternal: false, IsIgnored: false }
                or { IsJsTuple: true }
                or { Kind: IrNamedTypeKind.Branded or IrNamedTypeKind.Unknown }
        )
            return false;

        // A generic plain object has no descriptor yet (IrRuntimeTypeTest.ExistsFor).
        if (named is { Semantics.IsPlainObject: true, TypeArguments.Count: > 0 })
            return false;

        return !reachability.Undescribable.Contains(named.SupertypeKey);
    }

    /// <summary>
    /// Fails loudly when a descriptor of this compilation is read outside the
    /// reached set. A type of a referenced <c>[EmitPackage]</c> library always
    /// has its descriptor (D29), and so does a type the BCL lowers to a runtime
    /// class.
    /// </summary>
    public static void AssertReached(
        IrNamedTypeRef named,
        string site,
        RuntimeTypeReachabilityResult reachability
    )
    {
        if (
            named.Origin is not null
            || named.Semantics is not { IsTranspilable: true }
            || reachability.Reaches(named.SupertypeKey)
        )
            return;

        throw new InvalidOperationException(
            $"Internal error: the TypeScript bridge rendered a runtime type descriptor read of "
                + $"'{named.Namespace}.{named.Name}' ({site}), but runtime type reachability did not "
                + "reach it, so the descriptor is not emitted (spec 011 D29). The frontend "
                + "runtime-question scan and the bridge disagree."
        );
    }

    /// <summary>
    /// Fails loudly when a <c>[PlainObject]</c>'s synthesized guard is called
    /// but the reachability phase did not ask for it, so it is not emitted.
    /// </summary>
    public static void AssertGuarded(
        IrNamedTypeRef named,
        RuntimeTypeReachabilityResult reachability
    )
    {
        if (
            named.Semantics is { HasGeneratedGuard: true }
            || reachability.IsUnchecked
            || reachability.GuardedPlainObjects.Contains(named.SupertypeKey)
        )
            return;

        throw new InvalidOperationException(
            $"Internal error: the TypeScript bridge rendered the structural guard of "
                + $"'{named.Namespace}.{named.Name}', but runtime type reachability did not ask for "
                + "it, so the guard is not emitted (spec 011 FR-020)."
        );
    }

    /// <summary>The class value of <paramref name="named"/>, carrying its import.</summary>
    public static TsExpression ClassReference(IrNamedTypeRef named) =>
        IrToTsTypeMapper.Map(named) is TsNamedType mapped
            ? IrToTsExpressionBridge.TypeValueReference(mapped.Name, mapped.Origin)
            : new TsIdentifier(named.Name);

    /// <summary>
    /// <c>X$runtimeType</c> exported beside the type: imported from the type's
    /// own module, through the package of a referenced library, or read off the
    /// container's namespace for a nested type.
    /// </summary>
    private static TsExpression ExportReference(IrNamedTypeRef named)
    {
        var mapped = IrToTsTypeMapper.Map(named) as TsNamedType;
        var typeName = mapped?.Name ?? named.Name;
        var containerLength = typeName.LastIndexOf('.');

        if (containerLength > 0)
        {
            return new TsPropertyAccess(
                IrToTsExpressionBridge.TypeValueReference(
                    typeName[..containerLength],
                    mapped?.Origin
                ),
                $"{typeName[(containerLength + 1)..]}{ExportSuffix}"
            );
        }

        var exportName = $"{typeName}{ExportSuffix}";

        return mapped?.Origin is { } origin
            ? new TsTypeReference(exportName, origin)
            : new TsModuleExportReference(exportName, named.Name);
    }

    /// <summary>
    /// <c>X$runtimeType</c> of a top-level type of this compilation known by its
    /// reached-set key, imported from the type's own module.
    /// </summary>
    public static TsExpression LocalExportReference(SupertypeKey key) =>
        new TsModuleExportReference($"{key.Name}{ExportSuffix}", key.Name);

    private static TsCallExpression Helper(string name, params TsExpression[] arguments) =>
        new(new TsRuntimeHelperReference(name), arguments);

    /// <summary>
    /// A generic argument descriptor, or the erased-argument wildcard for a type
    /// with none or a class of this compilation the reachability phase did not reach.
    /// </summary>
    public static TsExpression ArgumentOf(
        IrTypeRef type,
        string site,
        RuntimeTypeReachabilityResult reachability,
        TsTypeParameterScope? scope = null
    ) =>
        type is IrNamedTypeRef { Origin: null, Semantics.IsTranspilable: true } named
        && !reachability.Reaches(named.SupertypeKey)
            ? Wildcard()
            : Of(type, site, reachability, scope) ?? Wildcard();

    /// <summary>
    /// The descriptor of a collection slot's argument: a class of this compilation is
    /// always reached when the kind is questioned (the reachability phase reaches every
    /// concrete argument), so reading it unreached is an internal error rather than a
    /// silent <c>UnknownType</c>, which would match every value (FR-008, D31).
    /// A concrete named type with no descriptor (non-<c>[Transpile]</c>, nested)
    /// lowers to <c>opaqueType(fullName)</c> so closed collection tests compare
    /// by identity instead of matching everything via <c>UnknownType</c> (#711).
    /// </summary>
    public static TsExpression SlotArgumentOf(
        IrTypeRef type,
        string site,
        RuntimeTypeReachabilityResult reachability,
        TsTypeParameterScope? scope = null
    )
    {
        if (type is IrNamedTypeRef { Origin: null, Semantics.IsTranspilable: true } named)
            AssertReached(named, site, reachability);

        if (Of(type, site, reachability, scope) is { } descriptor)
            return descriptor;

        if (type is IrNamedTypeRef concrete)
            return Helper("opaqueType", new TsStringLiteral(FullNameOf(concrete)));

        return Wildcard();
    }

    private static string FullNameOf(IrNamedTypeRef named) =>
        string.IsNullOrEmpty(named.Namespace) ? named.Name : $"{named.Namespace}.{named.Name}";

    /// <summary><c>UnknownType</c>: the descriptor that matches every value (FR-008).</summary>
    private static TsExpression Wildcard() => new TsRuntimeHelperReference("UnknownType");

    private static bool IsValueType(IrTypeRef type) =>
        type
            is IrPrimitiveTypeRef { Primitive: not (IrPrimitive.String or IrPrimitive.Object) }
                or IrNamedTypeRef { Semantics.IsValueType: true }
                or IrNullableTypeRef;
}
