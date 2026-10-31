using Metano.Compiler.Analysis;
using Metano.Compiler.IR;
using Metano.Compiler.TypeScript.AST;
using Metano.Compiler.TypeScript.Transformation;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// Given an <see cref="IrClassDeclaration"/> that passes
/// <see cref="ShouldSynthesize"/> (a record that isn't
/// <c>[PlainObject]</c>), produces the three helpers C# records are
/// expected to carry in their emitted TypeScript shape:
/// <list type="bullet">
///   <item><c>equals(other: any): boolean</c> — exact-runtime-type check
///   (same prototype) plus a short-circuiting <c>&amp;&amp;</c> chain
///   comparing every <see cref="IrClassDeclaration.EqualityMembers"/> entry
///   (<c>===</c> or <c>valueEquals</c>).</item>
///   <item><c>hashCode(): number</c> — drives a <c>HashCode</c> builder
///   over every field and returns <c>hc.toHashCode()</c>.</item>
///   <item><c>with(overrides?: Partial&lt;Self&gt;): Self</c> — copies the
///   instance through the runtime <c>copyRecordWith</c> helper and applies
///   the defined overrides.</item>
/// </list>
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

    /// <summary>
    /// Synthesizes <c>equals</c> / <c>hashCode</c> over
    /// <see cref="IrClassDeclaration.EqualityMembers"/> and a <c>with</c>
    /// that copies the instance and applies the overrides — C#'s copy
    /// constructor semantics, so members outside the primary constructor
    /// survive and no <c>new</c> is needed (abstract records included).
    /// <para>
    /// A record extending an emitted record base overrides the base's
    /// helpers, so they carry <c>override</c> (required under
    /// <c>noImplicitOverride</c>), and — like C#'s <c>base.Equals</c> /
    /// <c>base.GetHashCode</c> — chain to them for the base's members, which
    /// may be private to the base.
    /// </para>
    /// </summary>
    public static IReadOnlyList<TsClassMember> Generate(IrClassDeclaration ir)
    {
        var fields = (ir.EqualityMembers ?? []).Select(ToRecordField).ToList();
        var overridesBase = IrToTsClassBridge.BuildExtends(ir) is not null;

        TsMethodMember[] members =
        [
            .. SynthesizedEquality(ir, fields, overridesBase),
            GenerateWith(ir),
        ];

        return members.Select(m => m with { Override = overridesBase }).ToList();
    }

    /// <summary>
    /// <c>equals</c> and <c>hashCode</c>, each unless the record declares its
    /// own (<see cref="IrRecordEqualityOverrides"/>) — C# uses the declared one
    /// instead of synthesizing it, and it is emitted under the same name.
    /// </summary>
    private static IEnumerable<TsMethodMember> SynthesizedEquality(
        IrClassDeclaration ir,
        IReadOnlyList<RecordField> fields,
        bool overridesBase
    )
    {
        if (!IrRecordEqualityOverrides.DeclaresEquals(ir))
            yield return GenerateEquals(fields, overridesBase);

        if (!IrRecordEqualityOverrides.DeclaresHashCode(ir))
            yield return GenerateHashCode(fields, overridesBase);
    }

    /// <summary>
    /// <c>equals</c>: the root record tests the exact runtime type
    /// (<see cref="SameRecordTypeTest"/>); a derived record starts from
    /// <c>super.equals(other)</c>, which runs that test with <c>this</c> being
    /// the derived instance. Then every own member is compared.
    /// </summary>
    private static TsMethodMember GenerateEquals(
        IReadOnlyList<RecordField> fields,
        bool overridesBase
    )
    {
        var baseCondition = overridesBase
            ? SuperCall("equals", new TsIdentifier("other"))
            : SameRecordTypeTest();

        var condition = fields.Aggregate(
            baseCondition,
            (accumulated, field) => new TsBinaryExpression(accumulated, "&&", FieldEquality(field))
        );

        return new TsMethodMember(
            "equals",
            [new TsParameter("other", new TsAnyType())],
            new TsBooleanType(),
            [new TsReturnStatement(condition)]
        );
    }

    /// <summary>
    /// <c>other != null &amp;&amp; Object.getPrototypeOf(other) === Object.getPrototypeOf(this)</c>:
    /// C# record equality first compares <c>EqualityContract</c>, the exact
    /// runtime type, so a base instance never equals a derived one (an
    /// <c>instanceof</c> test would let <c>base.equals(derived)</c> pass).
    /// </summary>
    private static TsExpression SameRecordTypeTest()
    {
        var notNull = new TsBinaryExpression(
            new TsIdentifier("other"),
            "!=",
            new TsLiteral("null")
        );

        var samePrototype = new TsBinaryExpression(
            PrototypeOf(new TsIdentifier("other")),
            "===",
            PrototypeOf(new TsIdentifier("this"))
        );

        return new TsBinaryExpression(notNull, "&&", samePrototype);
    }

    private static TsExpression SuperCall(string method, params TsExpression[] arguments) =>
        new TsCallExpression(new TsPropertyAccess(new TsIdentifier("super"), method), arguments);

    private static TsCallExpression PrototypeOf(TsExpression value) =>
        new(new TsPropertyAccess(new TsIdentifier("Object"), "getPrototypeOf"), [value]);

    /// <summary>
    /// Emits the per-field comparison inside the synthesized
    /// <c>equals</c>. The strict / value choice is centralized in
    /// <see cref="IrEqualityClassifier"/> so the runtime-requirement
    /// scanner mirrors it without drift.
    /// </summary>
    private static TsExpression FieldEquality(RecordField field)
    {
        var self = new TsPropertyAccess(new TsIdentifier("this"), field.Name);
        var other = new TsPropertyAccess(new TsIdentifier("other"), field.Name);

        if (IrEqualityClassifier.UseStrictEquality(field.Type))
            return new TsBinaryExpression(self, "===", other);

        return new TsCallExpression(new TsIdentifier("valueEquals"), [self, other]);
    }

    /// <summary>
    /// <c>hashCode</c> over the own members; a derived record first adds
    /// <c>super.hashCode()</c>, mirroring C#'s <c>base.GetHashCode()</c> seed.
    /// </summary>
    private static TsMethodMember GenerateHashCode(
        IReadOnlyList<RecordField> fields,
        bool overridesBase
    )
    {
        var body = new List<TsStatement>
        {
            new TsVariableDeclaration("hc", new TsNewExpression(new TsIdentifier("HashCode"), [])),
        };

        if (overridesBase)
            body.Add(AddToHash(SuperCall(IrToTsNamingPolicy.HashCodeMethodName)));

        foreach (var field in fields)
            body.Add(AddToHash(new TsPropertyAccess(new TsIdentifier("this"), field.Name)));

        body.Add(
            new TsReturnStatement(
                new TsCallExpression(new TsPropertyAccess(new TsIdentifier("hc"), "toHashCode"), [])
            )
        );

        return new TsMethodMember(
            IrToTsNamingPolicy.HashCodeMethodName,
            [],
            new TsNumberType(),
            body
        );
    }

    private static TsExpressionStatement AddToHash(TsExpression value) =>
        new(new TsCallExpression(new TsPropertyAccess(new TsIdentifier("hc"), "add"), [value]));

    private static TsMethodMember GenerateWith(IrClassDeclaration ir)
    {
        var selfType = MakeSelfType(ir);

        var copy = new TsCallExpression(
            new TsIdentifier("copyRecordWith"),
            [new TsIdentifier("this"), new TsIdentifier("overrides")],
            TypeArguments: [selfType]
        );

        return new TsMethodMember(
            "with",
            [new TsParameter("overrides?", new TsNamedType("Partial", [selfType]))],
            selfType,
            [new TsReturnStatement(copy)]
        );
    }

    private static RecordField ToRecordField(IrRecordEqualityMember member) =>
        new(IrToTsNamingPolicy.ToDataMemberName(member.Name, member.EmittedName), member.Type);

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

    /// <summary>An equality member as the TS class names it, with the IR type
    /// that picks strict (<c>===</c>) or value (<c>valueEquals</c>)
    /// comparison.</summary>
    private sealed record RecordField(string Name, IrTypeRef Type);
}
