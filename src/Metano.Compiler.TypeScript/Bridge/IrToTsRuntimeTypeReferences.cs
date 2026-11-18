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
/// against the reached set of <see cref="RuntimeTypeScope.Current"/>: a read
/// outside it is an internal error, so a drift between the reachability phase
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
    /// drift error.
    /// </summary>
    public static TsExpression? Of(IrTypeRef type, string site) =>
        type switch
        {
            _ when type.IsObject => new TsRuntimeHelperReference("ObjectType"),
            IrNullableTypeRef { Inner: var inner } => Of(inner, site) is { } innerDescriptor
                ? Helper("nullableType", innerDescriptor)
                : null,
            IrPrimitiveTypeRef { Primitive: var primitive } => PrimitiveConstantName(primitive)
                is { } constant
                ? new TsRuntimeHelperReference(constant)
                : null,
            IrArrayTypeRef => new TsRuntimeHelperReference("ArrayType"),
            IrFunctionTypeRef => new TsRuntimeHelperReference("DelegateType"),
            IrNamedTypeRef named => NamedDescriptor(named, site),
            _ => null,
        };

    /// <summary>
    /// The descriptor of a named type; a type argument with no descriptor
    /// specializes to the <c>UnknownType</c> wildcard.
    /// </summary>
    private static TsExpression? NamedDescriptor(IrNamedTypeRef named, string site)
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

        if (!IsDescribable(named))
            return null;

        AssertReached(named, site);

        return named.Semantics?.Kind switch
        {
            IrNamedTypeKind.Interface
            or IrNamedTypeKind.NumericEnum
            or IrNamedTypeKind.StringEnum => ExportReference(named),
            _ => Specialized(named, site),
        };
    }

    private static TsExpression Specialized(IrNamedTypeRef named, string site)
    {
        var open = new TsPropertyAccess(ClassReference(named), GetterName);

        if (named.TypeArguments is not { Count: > 0 } arguments)
            return open;

        var descriptors = arguments
            .Select(argument => Of(argument, site) ?? new TsRuntimeHelperReference("UnknownType"))
            .ToList();

        return Helper("specialize", open, new TsArrayLiteral(descriptors));
    }

    /// <summary>
    /// Whether a named type has a descriptor TypeScript can name: a class,
    /// record, struct, interface or enum that is neither a plain object, a
    /// branded wrapper nor a <c>[JsTuple]</c>, and that the analysis did not
    /// report as undescribable.
    /// </summary>
    public static bool IsDescribable(IrNamedTypeRef named)
    {
        if (
            named.Semantics
            is not { IsTranspilable: true, IsExternal: false, IsIgnored: false }
                or { IsPlainObject: true }
                or { IsJsTuple: true }
                or { Kind: IrNamedTypeKind.Branded or IrNamedTypeKind.Unknown }
        )
            return false;

        return RuntimeTypeScope.Current?.Undescribable.Contains(named.SupertypeKey) is not true;
    }

    /// <summary>
    /// Fails loudly when a descriptor of this compilation is read outside the
    /// reached set. A type of a referenced <c>[EmitPackage]</c> library always
    /// has its descriptor (D29), and so does a type the BCL lowers to a runtime
    /// class.
    /// </summary>
    public static void AssertReached(IrNamedTypeRef named, string site)
    {
        if (
            RuntimeTypeScope.Current is not { } reachability
            || named.Origin is not null
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

    private static TsCallExpression Helper(string name, params TsExpression[] arguments) =>
        new(new TsRuntimeHelperReference(name), arguments);

    /// <summary>The <c>metano-runtime</c> constant that describes <paramref name="primitive"/>.</summary>
    public static string? PrimitiveConstantName(IrPrimitive primitive) =>
        primitive switch
        {
            IrPrimitive.Boolean => "BooleanType",
            IrPrimitive.Byte => "ByteType",
            IrPrimitive.Int16 => "Int16Type",
            IrPrimitive.Int32 => "Int32Type",
            IrPrimitive.Int64 => "Int64Type",
            IrPrimitive.UInt64 => "UInt64Type",
            IrPrimitive.Float32 => "SingleType",
            IrPrimitive.Float64 => "DoubleType",
            IrPrimitive.Decimal => "DecimalType",
            IrPrimitive.BigInteger => "BigIntegerType",
            IrPrimitive.String => "StringType",
            IrPrimitive.Char => "CharType",
            IrPrimitive.Guid => "GuidType",
            IrPrimitive.DateTime => "DateTimeType",
            IrPrimitive.DateTimeOffset => "DateTimeOffsetType",
            IrPrimitive.DateOnly => "DateOnlyType",
            IrPrimitive.TimeOnly => "TimeOnlyType",
            IrPrimitive.TimeSpan => "TimeSpanType",
            IrPrimitive.Object => "ObjectType",
            _ => null,
        };
}
