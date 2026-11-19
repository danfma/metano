using Metano.Compiler.Analysis;
using Metano.Compiler.IR;
using Metano.Compiler.Mappings;
using Metano.Compiler.TypeScript.AST;
using Metano.Compiler.TypeScript.Transformation;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// Lowers a generic <see cref="IrClassDeclaration"/> (record / class / struct
/// that isn't <c>[PlainObject]</c>, <c>[Branded]</c>, an exception, or a
/// static module) into a TypeScript <see cref="TsClass"/>.
/// <para>
/// Implemented as a library of static lowering helpers — class header
/// (this file), member emission (<c>.Members.cs</c>), constructor
/// synthesis (<c>.Constructor.cs</c>), operator dispatch
/// (<c>.Operators.cs</c>), and object-args factories
/// (<c>.ObjectArgs.cs</c>) — called from <see cref="IrToTsClassEmitter"/>,
/// which wires them together into the full <see cref="TsClass"/> shape.
/// </para>
/// </summary>
internal static partial class IrToTsClassBridge
{
    /// <summary>
    /// Builds the <c>extends</c> clause for an <see cref="IrClassDeclaration"/>.
    /// Returns <c>null</c> when the IR has no base type, the base is the
    /// implicit <c>System.Object</c> / <c>System.ValueType</c> sentinel (which
    /// the extractor leaves unset), or the base is a non-transpilable named
    /// type — TypeScript can't extend something that doesn't have a class
    /// emitted for it. One case differs: a non-transpilable exception base (the
    /// BCL hierarchy) extends its runtime class (spec 011 FR-014; see
    /// <see cref="BclExceptionClasses"/>), so user exceptions keep the .NET
    /// hierarchy and <c>instanceof Error</c>.
    /// </summary>
    public static TsType? BuildExtends(IrClassDeclaration ir) =>
        ir.BaseType switch
        {
            null => null,
            IrNamedTypeRef named when BclExceptionClasses.IsBclException(named) =>
                IrToTsTypeMapper.Map(named),
            IrNamedTypeRef { Semantics.IsTranspilable: false } => null,
            _ => IrToTsTypeMapper.Map(ir.BaseType),
        };

    /// <summary>
    /// Builds the <c>implements</c> list for an <see cref="IrClassDeclaration"/>.
    /// Filters out non-transpilable interfaces — TypeScript can't implement an
    /// interface that doesn't get emitted alongside the class — and an iterator
    /// contract the class cannot honor
    /// (<see cref="IrToTsIterationProtocolBridge.ImplementedInterfaces"/>). Returns
    /// <c>null</c> when nothing remains so the printer elides the clause.
    /// </summary>
    public static IReadOnlyList<TsType>? BuildImplements(IrClassDeclaration ir) =>
        ConvertHeritageList(IrToTsIterationProtocolBridge.ImplementedInterfaces(ir));

    /// <summary>
    /// Lowers a list of IR base / interface references into TS heritage
    /// entries. Drops named refs flagged <c>IsTranspilable: false</c>
    /// (e.g. <c>[Ignore]</c> or unmapped BCL interfaces) and rewrites
    /// the array shorthand <c>T[]</c> (produced when a base maps
    /// through <see cref="IrArrayTypeRef"/> for collection-like BCL
    /// types) into <c>Array&lt;T&gt;</c>: TypeScript rejects
    /// <c>extends T[]</c> / <c>implements T[]</c> in heritage clauses,
    /// but accepts the named <c>Array</c> form.
    /// </summary>
    public static IReadOnlyList<TsType>? ConvertHeritageList(IReadOnlyList<IrTypeRef>? references)
    {
        if (references is not { Count: > 0 } refs)
            return null;
        var result = new List<TsType>();
        foreach (var reference in refs)
        {
            if (reference is IrNamedTypeRef { Semantics.IsTranspilable: false })
                continue;
            var mapped = IrToTsTypeMapper.Map(reference);
            result.Add(
                mapped is TsArrayType array ? new TsNamedType("Array", [array.ElementType]) : mapped
            );
        }
        return result.Count > 0 ? result : null;
    }

    /// <summary>
    /// Lowers IR type parameters into the TS form. Only the first constraint
    /// of each parameter is rendered today — TS only supports a single
    /// <c>extends</c> bound and the legacy transformer made the same choice.
    /// </summary>
    public static IReadOnlyList<TsTypeParameter>? BuildTypeParameters(IrClassDeclaration ir) =>
        IrToTsTypeParameterMapper.Convert(ir.TypeParameters);

    /// <summary>
    /// Prints the implicit <c>default(T)</c> initializer of a field / property
    /// whose declaration omits an explicit one, as the core's default-value
    /// policy spells it (<see cref="IrDefaultValues.KindOf"/>): <c>null</c>
    /// for a nullable type, the zero of a primitive (<c>0</c>, <c>0n</c>,
    /// <c>false</c>, <c>new Decimal("0")</c>, <c>"\0"</c>, <c>UUID.empty</c>), the zero member of an enum
    /// (<c>0 as E</c> when a numeric enum declares none), and <c>new S()</c>
    /// for a default-constructible struct. <c>null</c> when the policy spells
    /// no value — the caller emits the field without an initializer (TS leaves
    /// a reference field <c>undefined</c>).
    /// </summary>
    public static TsExpression? ComputeDefaultInitializer(IrTypeRef type) =>
        IrDefaultValues.KindOf(type) switch
        {
            IrDefaultValueKind.Null => new TsLiteral("null"),
            IrDefaultValueKind.Zero => PrimitiveZero(((IrPrimitiveTypeRef)type).Primitive),
            IrDefaultValueKind.EnumZeroMember => EnumZeroMember((IrNamedTypeRef)type),
            IrDefaultValueKind.EnumZeroAssertion => new TsCastExpression(
                IrToTsExpressionBridge.Map(
                    TsEnumRepresentation.Constant(new IrLiteral(0, IrLiteralKind.Int32), type),
                    IrToTsBridgeContext.For(null)
                ),
                IrToTsTypeMapper.Map(type)
            ),
            IrDefaultValueKind.StructConstruction => IrToTsExpressionBridge.Map(
                new IrNewExpression(type, []),
                IrToTsBridgeContext.For(null)
            ),
            _ => null,
        };

    private static TsExpression PrimitiveZero(IrPrimitive primitive) =>
        primitive switch
        {
            IrPrimitive.Int64 or IrPrimitive.UInt64 or IrPrimitive.BigInteger => SixtyFourBitZero(),
            IrPrimitive.Char => new TsStringLiteral("\0"),
            IrPrimitive.Guid => new TsPropertyAccess(UuidRuntimeReference.Value(), "empty"),
            IrPrimitive.Decimal => new TsNewExpression(
                IrToTsExpressionBridge.BclDecimalValueReference(),
                [new TsStringLiteral("0")]
            ),
            IrPrimitive.Boolean => new TsLiteral("false"),
            _ => new TsLiteral("0"),
        };

    private static TsExpression EnumZeroMember(IrNamedTypeRef namedEnum)
    {
        var semantics = namedEnum.Semantics!;
        var zeroMember = semantics.EnumZeroMember!;

        // String enums key the runtime object on the source-cased name —
        // a [Name(target, ...)] rename would produce an invalid property
        // access (`MyEnum.in-progress`). Numeric enums honor the override.
        var memberName =
            semantics.Kind == IrNamedTypeKind.StringEnum
                ? zeroMember.Name
                : zeroMember.EmittedName ?? zeroMember.Name;

        return new TsPropertyAccess(
            IrToTsExpressionBridge.TypeValueReference(
                namedEnum.Name,
                IrToTsTypeMapper.TryResolveOrigin(namedEnum)
            ),
            memberName
        );
    }

    /// <summary>
    /// The zero of <c>long</c> / <c>ulong</c>: <c>0n</c> (spec 012 FR-004).
    /// </summary>
    internal static TsLiteral SixtyFourBitZero() => new("0n");
}
