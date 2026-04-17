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
        foreach (var param in ctorParams)
        {
            condition = new TsBinaryExpression(
                condition,
                "&&",
                new TsBinaryExpression(
                    new TsPropertyAccess(new TsIdentifier("this"), param.Name),
                    "===",
                    new TsPropertyAccess(new TsIdentifier("other"), param.Name)
                )
            );
        }
        return new TsMethodMember(
            "equals",
            [new TsParameter("other", new TsAnyType())],
            new TsBooleanType(),
            [new TsReturnStatement(condition)]
        );
    }

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
