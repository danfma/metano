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
    /// emitted for it.
    /// </summary>
    public static TsType? BuildExtends(IrClassDeclaration ir) =>
        ir.BaseType switch
        {
            null => null,
            IrNamedTypeRef { Semantics.IsTranspilable: false } => null,
            _ => IrToTsTypeMapper.Map(ir.BaseType),
        };

    /// <summary>
    /// Builds the <c>implements</c> list for an <see cref="IrClassDeclaration"/>.
    /// Filters out non-transpilable interfaces — TypeScript can't implement an
    /// interface that doesn't get emitted alongside the class. Returns
    /// <c>null</c> when nothing remains so the printer elides the clause.
    /// </summary>
    public static IReadOnlyList<TsType>? BuildImplements(IrClassDeclaration ir) =>
        ConvertHeritageList(ir.Interfaces);

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
    /// Computes the implicit <c>default(T)</c> initializer for a field /
    /// property whose declaration omits an explicit one:
    /// <list type="bullet">
    ///   <item>nullable types → <c>null</c></item>
    ///   <item>numeric primitives → <c>0</c></item>
    ///   <item>boolean → <c>false</c></item>
    ///   <item>decimal → <c>new Decimal("0")</c></item>
    ///   <item>enums → first member (numeric: smallest value; string: first
    ///     declared). String enums use the source-name key; numeric enums
    ///     honor a target <c>[Name]</c> override on the member.</item>
    ///   <item>everything else → no implicit initializer (TS leaves
    ///     reference fields <c>undefined</c>, matching nullable C#)</item>
    /// </list>
    /// Returns <c>null</c> when no default applies — the caller emits the
    /// field without an initializer in that case.
    /// </summary>
    public static TsExpression? ComputeDefaultInitializer(IrTypeRef type)
    {
        if (type is IrNullableTypeRef)
            return new TsLiteral("null");

        if (type is IrPrimitiveTypeRef p)
            return p.Primitive switch
            {
                IrPrimitive.Int16
                or IrPrimitive.Int32
                or IrPrimitive.Int64
                or IrPrimitive.Byte
                or IrPrimitive.Float32
                or IrPrimitive.Float64 => new TsLiteral("0"),
                IrPrimitive.Decimal => new TsNewExpression(
                    new TsIdentifier("Decimal"),
                    [new TsStringLiteral("0")]
                ),
                IrPrimitive.Boolean => new TsLiteral("false"),
                _ => null,
            };

        if (
            type is IrNamedTypeRef
            {
                Semantics:
                {
                    Kind: IrNamedTypeKind.NumericEnum or IrNamedTypeKind.StringEnum,
                    EnumDefaultMember: { } defaultMember,
                } enumSemantics,
            } namedEnum
        )
        {
            // String enums key the runtime object on the source-cased name —
            // a [Name(target, ...)] rename would produce an invalid property
            // access (`MyEnum.in-progress`). Numeric enums honor the override.
            var memberName =
                enumSemantics.Kind == IrNamedTypeKind.StringEnum
                    ? defaultMember.Name
                    : defaultMember.EmittedName ?? defaultMember.Name;
            return new TsPropertyAccess(new TsIdentifier(namedEnum.Name), memberName);
        }

        return null;
    }

    /// <summary>
    /// Maps an IR member visibility to the TS accessibility keyword the
    /// printer renders. <c>Internal</c> / <c>ProtectedInternal</c> /
    /// <c>PrivateProtected</c> all collapse to <c>public</c> in TS — the
    /// language has no narrower-than-public sibling-of-private band.
    /// </summary>
    public static TsAccessibility MapAccessibility(IrVisibility visibility) =>
        visibility switch
        {
            IrVisibility.Private => TsAccessibility.Private,
            IrVisibility.Protected or IrVisibility.ProtectedInternal => TsAccessibility.Protected,
            _ => TsAccessibility.Public,
        };
}
