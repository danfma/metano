using Metano.Compiler.IR;
using Metano.TypeScript.AST;

namespace Metano.TypeScript.Bridge;

/// <summary>
/// Given an <see cref="IrClassDeclaration"/> that passes
/// <see cref="ShouldSynthesize"/> (a record that isn't
/// <c>[PlainObject]</c>) and the TS-ified record parameter list, produces
/// the three value-equality helpers C# records are expected to carry in
/// their emitted TypeScript shape:
/// <list type="bullet">
///   <item><c>equals(other: any): boolean</c> — exact-type narrow via
///   <c>instanceof</c> plus a short-circuiting <c>&amp;&amp;</c> chain
///   comparing every ctor field with <c>===</c>.</item>
///   <item><c>hashCode(): number</c> — drives a <c>HashCode</c> builder
///   over every field and returns <c>hc.toHashCode()</c>.</item>
///   <item><c>with(overrides?: Partial&lt;Self&gt;): Self</c> — rebuilds
///   the record, preferring values from <c>overrides</c> via <c>??</c> and
///   falling back to the current instance's fields.</item>
/// </list>
/// <para>
/// The emitted shape matches the legacy synthesizer byte-for-byte so golden
/// tests don't shift when a record moves to a different bridge path.
/// </para>
/// </summary>
public static class IrToTsRecordSynthesisBridge
{
    /// <summary>
    /// Whether the class should receive synthesized <c>equals</c>/<c>hashCode</c>/<c>with</c>.
    /// Records yes; <c>[PlainObject]</c> records no (those emit as bare
    /// object literals with no class wrapper).
    /// </summary>
    public static bool ShouldSynthesize(IrClassDeclaration ir) =>
        ir.Semantics.IsRecord && !ir.Semantics.IsPlainObject;

    public static IReadOnlyList<TsClassMember> Generate(
        IrClassDeclaration ir,
        IReadOnlyList<TsConstructorParam> ctorParams
    ) =>
        [
            GenerateEquals(ir, ctorParams),
            GenerateHashCode(ctorParams),
            GenerateWith(ir, ctorParams),
        ];

    // ── equals ───────────────────────────────────────────────────────────

    private static TsMethodMember GenerateEquals(
        IrClassDeclaration ir,
        IReadOnlyList<TsConstructorParam> ctorParams
    )
    {
        TsExpression condition = new TsBinaryExpression(
            new TsIdentifier("other"),
            "instanceof",
            new TsIdentifier(GetTypeName(ir))
        );
        // The IR ctor param list mirrors the TS-lowered ctorParams in
        // declaration order, so we lift each TS param's IR type by
        // positional index rather than by name (TS names are
        // camelCased while the IR keeps the C# casing).
        var irTypes = ir.Constructor?.Parameters.Select(p => p.Parameter.Type).ToList();
        for (var i = 0; i < ctorParams.Count; i++)
        {
            var irType = irTypes is not null && i < irTypes.Count ? irTypes[i] : null;
            condition = new TsBinaryExpression(
                condition,
                "&&",
                FieldEquality(ctorParams[i], irType)
            );
        }
        return new TsMethodMember(
            "equals",
            [new TsParameter("other", new TsAnyType())],
            new TsBooleanType(),
            [new TsReturnStatement(condition)]
        );
    }

    /// <summary>
    /// Emits the per-field comparison inside the synthesized
    /// <c>equals</c>. Primitive / enum / branded / type-parameter
    /// fields use <c>===</c>; everything else (nested records, BCL
    /// value wrappers like <c>Decimal</c> / <c>Temporal</c>, plain
    /// classes, arrays, maps, …) routes through the runtime
    /// <c>valueEquals</c> helper so wrappers carrying their own
    /// <c>equals</c> contract compare structurally instead of by
    /// reference.
    /// </summary>
    private static TsExpression FieldEquality(TsConstructorParam param, IrTypeRef? irType)
    {
        var self = new TsPropertyAccess(new TsIdentifier("this"), param.Name);
        var other = new TsPropertyAccess(new TsIdentifier("other"), param.Name);
        if (UseStrictEquality(irType))
            return new TsBinaryExpression(self, "===", other);
        return new TsCallExpression(new TsIdentifier("valueEquals"), [self, other]);
    }

    private static bool UseStrictEquality(IrTypeRef? type) =>
        type switch
        {
            null => true,
            IrPrimitiveTypeRef p => UseStrictEqualityForPrimitive(p.Primitive),
            IrTypeParameterRef => true,
            IrNullableTypeRef nullable => UseStrictEquality(nullable.Inner),
            IrNamedTypeRef named => UseStrictEqualityForNamed(named),
            _ => false,
        };

    /// <summary>
    /// JS treats some C# primitives as object values at runtime — the
    /// BCL mappings lower <c>Decimal</c> to <c>decimal.js</c>'s
    /// <c>Decimal</c> class, the date / time family to Temporal
    /// objects, and <c>Guid</c> to the runtime's <c>UUID</c> class.
    /// Those wrappers expose <c>equals</c> contracts and need to
    /// route through <c>valueEquals</c>; the rest are real JS
    /// primitives where <c>===</c> is correct and faster.
    /// </summary>
    private static bool UseStrictEqualityForPrimitive(IrPrimitive p) =>
        p switch
        {
            IrPrimitive.Decimal => false,
            IrPrimitive.Guid => false,
            IrPrimitive.DateTime => false,
            IrPrimitive.DateTimeOffset => false,
            IrPrimitive.DateOnly => false,
            IrPrimitive.TimeOnly => false,
            IrPrimitive.TimeSpan => false,
            _ => true,
        };

    private static bool UseStrictEqualityForNamed(IrNamedTypeRef named) =>
        named.Semantics?.Kind
            is IrNamedTypeKind.StringEnum
                or IrNamedTypeKind.NumericEnum
                or IrNamedTypeKind.Branded;


    // ── hashCode ─────────────────────────────────────────────────────────

    private static TsMethodMember GenerateHashCode(IReadOnlyList<TsConstructorParam> ctorParams)
    {
        var body = new List<TsStatement>
        {
            new TsVariableDeclaration("hc", new TsNewExpression(new TsIdentifier("HashCode"), [])),
        };
        foreach (var param in ctorParams)
        {
            body.Add(
                new TsExpressionStatement(
                    new TsCallExpression(
                        new TsPropertyAccess(new TsIdentifier("hc"), "add"),
                        [new TsPropertyAccess(new TsIdentifier("this"), param.Name)]
                    )
                )
            );
        }
        body.Add(
            new TsReturnStatement(
                new TsCallExpression(new TsPropertyAccess(new TsIdentifier("hc"), "toHashCode"), [])
            )
        );
        return new TsMethodMember("hashCode", [], new TsNumberType(), body);
    }

    // ── with ─────────────────────────────────────────────────────────────

    private static TsMethodMember GenerateWith(
        IrClassDeclaration ir,
        IReadOnlyList<TsConstructorParam> ctorParams
    )
    {
        var selfType = MakeSelfType(ir);
        var args = ctorParams
            .Select<TsConstructorParam, TsExpression>(p => new TsBinaryExpression(
                new TsPropertyAccess(new TsIdentifier("overrides?"), p.Name),
                "??",
                new TsPropertyAccess(new TsIdentifier("this"), p.Name)
            ))
            .ToList();
        return new TsMethodMember(
            "with",
            [new TsParameter("overrides?", new TsNamedType("Partial", [selfType]))],
            selfType,
            [new TsReturnStatement(new TsNewExpression(new TsIdentifier(GetTypeName(ir)), args))]
        );
    }

    // ── helpers ──────────────────────────────────────────────────────────

    private static TsNamedType MakeSelfType(IrClassDeclaration ir)
    {
        var typeName = GetTypeName(ir);
        if (ir.TypeParameters is not { Count: > 0 } tps)
            return new TsNamedType(typeName);
        var args = tps.Select<IrTypeParameter, TsType>(tp => new TsNamedType(tp.Name)).ToList();
        return new TsNamedType(typeName, args);
    }

    /// <summary>
    /// Emitted class name, honoring target-aware <c>[Name]</c> overrides so a
    /// record renamed for TS still closes over itself consistently in the
    /// synthesized <c>equals</c>/<c>with</c>.
    /// </summary>
    private static string GetTypeName(IrClassDeclaration ir) =>
        IrToTsNamingPolicy.ToTypeName(ir.Name, ir.Attributes);
}
