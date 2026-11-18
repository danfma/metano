using Metano.Compiler.Analysis;
using Metano.Compiler.Diagnostics;
using Metano.Compiler.IR;
using Metano.Compiler.TypeScript.AST;
using Metano.Compiler.TypeScript.Transformation;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// Given an <see cref="IrClassDeclaration"/> that passes
/// <see cref="ShouldSynthesize"/> (a record or a plain struct emitted as a
/// class, <see cref="IrValueEqualitySynthesis"/>), produces the helpers C#
/// records are expected to carry in their emitted TypeScript shape (a plain
/// struct gets <c>equals</c> and <c>hashCode</c>, its
/// <c>ValueType.Equals</c>):
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
    /// <summary>The name of the synthesized <c>Equals</c>, which <c>valueEquals</c> calls.</summary>
    internal const string EqualsMethodName = "equals";

    private const string HashCodeMethodName = IrToTsNamingPolicy.HashCodeMethodName;

    /// <summary>The name of the synthesized copy, which a <c>with</c> expression calls.</summary>
    internal const string WithMethodName = "with";

    private static readonly IReadOnlyList<string> StructMemberNames =
    [
        EqualsMethodName,
        HashCodeMethodName,
    ];

    private static readonly IReadOnlyList<string> RecordMemberNames =
    [
        .. StructMemberNames,
        WithMethodName,
    ];

    /// <summary>
    /// The instance members <see cref="Generate"/> declares on
    /// <paramref name="ir"/>, which a class's claim pass treats as user
    /// members (spec 013 FR-004): each one the type's value equality
    /// synthesizes, unless it joined a user group (spec 013 FR-038, emitted
    /// there as a fast path) or a user member of <paramref name="userMembers"/>
    /// already emits under its name. A member left out that way is a data
    /// member: a field <c>HashCode</c> keeps <c>hashCode</c> (the runtime reads
    /// a record's hash through <c>hashCode()</c> only when it is a function),
    /// and one named <c>Equals</c> is reported (<see cref="ReportClaimedEquals"/>).
    /// </summary>
    internal static IReadOnlyList<string> SynthesizedMemberNames(
        IrClassDeclaration ir,
        TsClassMemberTable userMembers
    )
    {
        if (!ShouldSynthesize(ir))
            return [];

        var joined = JoinedKindsOf(ir)
            .Select(TsGroupingPolicy.NameOf)
            .ToHashSet(StringComparer.Ordinal);

        return
        [
            .. CandidateNames(ir)
                .Where(name => !joined.Contains(name) && !userMembers.Instances.ContainsKey(name)),
        ];
    }

    private static IReadOnlyList<string> CandidateNames(IrClassDeclaration ir) =>
        IrValueEqualitySynthesis.SynthesizesRecordMembers(ir)
            ? RecordMemberNames
            : StructMemberNames;

    /// <summary>The synthesized members of <paramref name="ir"/> that joined a user group (spec 013 FR-038).</summary>
    private static IEnumerable<IrSynthesizedValueMember> JoinedKindsOf(IrClassDeclaration ir) =>
        SynthesizedGroupMembersOf(ir).Select(member => member.SynthesizedKind!.Value);

    private static IEnumerable<IrMethodDeclaration> SynthesizedGroupMembersOf(
        IrClassDeclaration ir
    ) =>
        (ir.Members ?? [])
            .OfType<IrMethodDeclaration>()
            .Where(method => method.OverloadName is not null)
            .Select(IrMethodGroup.Of)
            .Select(group => group.Synthesized)
            .OfType<IrMethodDeclaration>();

    /// <summary>
    /// The base member a joined synthesized member's body chains to: the base's
    /// fast path it overrides when anchored, else the base record's plain member
    /// of its name; <c>null</c> for a root record.
    /// </summary>
    private static string? SuperMemberOf(
        IrClassDeclaration ir,
        IrMethodDeclaration member,
        string fastPathName
    )
    {
        if (IrMethodGroup.IsOverride(member))
            return fastPathName;

        return IrToTsClassBridge.BuildExtends(ir) is null
            ? null
            : TsGroupingPolicy.NameOf(member.SynthesizedKind!.Value);
    }

    /// <summary>The overrides a copy takes: <c>Partial&lt;Self&gt;</c>.</summary>
    internal static TsType OverridesTypeOf(TsType selfType) =>
        new TsNamedType("Partial", [selfType]);

    /// <summary>
    /// Whether the class should receive synthesized members: a record
    /// (<c>equals</c> / <c>hashCode</c> / <c>with</c>) or a plain struct
    /// (<c>equals</c> / <c>hashCode</c>) emitted as a class.
    /// </summary>
    public static bool ShouldSynthesize(IrClassDeclaration ir) =>
        IrValueEqualitySynthesis.SynthesizesEquality(ir);

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
    internal static IReadOnlyList<TsClassMember> Generate(
        IrClassDeclaration ir,
        TsClassMemberTable userMembers
    )
    {
        var fields = (ir.EqualityMembers ?? []).Select(ToRecordField).ToList();
        var overridesBase = IrToTsClassBridge.BuildExtends(ir) is not null;
        var unclaimedNames = SynthesizedMemberNames(ir, userMembers);

        var with = unclaimedNames.Contains(WithMethodName)
            ? [GenerateWith(ir)]
            : Array.Empty<TsMethodMember>();

        TsMethodMember[] members =
        [
            .. SynthesizedEquality(ir, unclaimedNames, fields, overridesBase),
            .. with,
        ];

        return members.Select(m => m with { Override = overridesBase }).ToList();
    }

    /// <summary>
    /// Whether the bridge emits a <c>hashCode</c> body for <paramref name="ir"/>:
    /// a standalone one, or the fast path of a joined <c>HashCode</c> with no
    /// user body folded in.
    /// </summary>
    internal static bool SynthesizesHashCode(IrClassDeclaration ir)
    {
        var standalone =
            SynthesizedMemberNames(ir, TsClassMemberTable.UserMembersOf(ir))
                .Contains(HashCodeMethodName) && !IrValueEqualitySynthesis.DeclaresHashCode(ir);

        return standalone
            || SynthesizedGroupMembersOf(ir)
                .Any(member =>
                    member is { SynthesizedKind: IrSynthesizedValueMember.HashCode, Body: null }
                );
    }

    /// <summary>
    /// The fast path of a synthesized member that joined a group (spec 013
    /// FR-041) under its claimed name, with the body the target synthesizes: an
    /// anchored one overrides the base's fast path and chains to it
    /// (<c>super.equalsByValue(other)</c>), never through the base dispatcher; an
    /// unanchored one of a derived record chains to the base's plain member.
    /// </summary>
    internal static TsMethodMember BuildFastPath(
        IrClassDeclaration ir,
        IrMethodDeclaration member,
        string fastPathName,
        IrToTsBridgeContext context
    )
    {
        var fields = (ir.EqualityMembers ?? []).Select(ToRecordField).ToList();
        var isAnchored = IrMethodGroup.IsOverride(member);
        var superMember = SuperMemberOf(ir, member, fastPathName);

        var built = member.SynthesizedKind switch
        {
            IrSynthesizedValueMember.Equals => GenerateEquals(fields, superMember),
            IrSynthesizedValueMember.HashCode => GenerateHashCode(fields, superMember),
            _ => GenerateWith(ir),
        };

        return built with
        {
            Name = fastPathName,
            Override = isAnchored,
        };
    }

    /// <summary>
    /// Reports <c>MS0001</c> (an error) at a field or property of
    /// <paramref name="type"/> whose emitted name is <c>equals</c> when the type's
    /// value equality would synthesize that member: a data member has no
    /// renaming path, and keeping it would leave the type without its
    /// equality, which <c>valueEquals</c> and the runtime collections call.
    /// </summary>
    internal static void ReportClaimedEquals(
        INamedTypeSymbol type,
        IrClassDeclaration ir,
        TsClassMemberTable userMembers,
        Action<MetanoDiagnostic> report
    )
    {
        if (!ShouldSynthesize(ir))
            return;

        if (userMembers.Instances.GetValueOrDefault(EqualsMethodName) is not { } declaredBy)
            return;

        var member = type.GetMembers()
            .FirstOrDefault(candidate =>
                candidate is IFieldSymbol or IPropertySymbol
                && !candidate.IsStatic
                && candidate.Name == declaredBy
            );

        if (member is null)
            return;

        report(
            new MetanoDiagnostic(
                MetanoDiagnosticSeverity.Error,
                DiagnosticCodes.UnsupportedFeature,
                $"'{type.Name}.{member.Name}' is emitted as 'equals', the "
                    + $"member that carries the value equality of '{type.Name}'; a field or "
                    + "property cannot be renamed, so the type would lose its equality. Rename "
                    + "the member.",
                member.Locations.FirstOrDefault() ?? type.Locations.FirstOrDefault()
            )
        );
    }

    /// <summary>
    /// <c>equals</c> and <c>hashCode</c>, each unless the record declares its
    /// own (<see cref="IrValueEqualitySynthesis"/>) — C# uses the declared one
    /// instead of synthesizing it, and it is emitted under the same name — or
    /// another user member already emits under the name (not in
    /// <paramref name="unclaimedNames"/>).
    /// </summary>
    private static IEnumerable<TsMethodMember> SynthesizedEquality(
        IrClassDeclaration ir,
        IReadOnlyList<string> unclaimedNames,
        IReadOnlyList<RecordField> fields,
        bool overridesBase
    )
    {
        var synthesizesEquals =
            unclaimedNames.Contains(EqualsMethodName)
            && !IrValueEqualitySynthesis.DeclaresEquals(ir);

        if (synthesizesEquals)
            yield return GenerateEquals(fields, overridesBase ? EqualsMethodName : null);

        var synthesizesHashCode =
            unclaimedNames.Contains(HashCodeMethodName)
            && !IrValueEqualitySynthesis.DeclaresHashCode(ir);

        if (synthesizesHashCode)
            yield return GenerateHashCode(fields, overridesBase ? HashCodeMethodName : null);
    }

    /// <summary>
    /// <c>equals</c>: the root record tests the exact runtime type
    /// (<see cref="SameRecordTypeTest"/>); a derived record starts from the
    /// base's <paramref name="superMember"/> (<c>super.equals(other)</c>, or the
    /// base's fast path), which runs that test with <c>this</c> being the
    /// derived instance. Then every own member is compared.
    /// </summary>
    private static TsMethodMember GenerateEquals(
        IReadOnlyList<RecordField> fields,
        string? superMember
    )
    {
        var baseCondition = superMember is not null
            ? SuperCall(superMember, new TsIdentifier("other"))
            : SameRecordTypeTest();

        var condition = fields.Aggregate(baseCondition, (accumulated, field) =>
            new TsBinaryExpression(accumulated, "&&", FieldEquality(field))
        );

        return new TsMethodMember(
            EqualsMethodName,
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

        return IrEqualityClassifier.EqualityOf(field.Type) switch
        {
            FieldEqualityKind.Strict => new TsBinaryExpression(self, "===", other),
            FieldEqualityKind.Default => new TsCallExpression(
                new TsRuntimeHelperReference("defaultEquals"),
                [self, other]
            ),
            _ => new TsCallExpression(new TsRuntimeHelperReference("valueEquals"), [self, other]),
        };
    }

    /// <summary>
    /// <c>hashCode</c> over the own members; a derived record first adds the
    /// base's <paramref name="superMember"/> (<c>super.hashCode()</c>, or the
    /// base's fast path), mirroring C#'s <c>base.GetHashCode()</c> seed.
    /// </summary>
    private static TsMethodMember GenerateHashCode(
        IReadOnlyList<RecordField> fields,
        string? superMember
    )
    {
        var body = new List<TsStatement>
        {
            new TsVariableDeclaration(
                "hc",
                new TsNewExpression(new TsRuntimeHelperReference("HashCode"), [])
            ),
        };

        if (superMember is not null)
            body.Add(AddToHash(SuperCall(superMember)));

        foreach (var field in fields)
            body.Add(AddToHash(new TsPropertyAccess(new TsIdentifier("this"), field.Name)));

        body.Add(
            new TsReturnStatement(
                new TsCallExpression(new TsPropertyAccess(new TsIdentifier("hc"), "toHashCode"), [])
            )
        );

        return new TsMethodMember(HashCodeMethodName, [], new TsNumberType(), body);
    }

    private static TsExpressionStatement AddToHash(TsExpression value) =>
        new(new TsCallExpression(new TsPropertyAccess(new TsIdentifier("hc"), "add"), [value]));

    private static TsMethodMember GenerateWith(IrClassDeclaration ir)
    {
        var selfType = MakeSelfType(ir);

        var copy = new TsCallExpression(
            new TsRuntimeHelperReference("copyRecordWith"),
            [new TsIdentifier("this"), new TsIdentifier("overrides")],
            TypeArguments: [selfType]
        );

        return new TsMethodMember(
            WithMethodName,
            [new TsParameter("overrides?", OverridesTypeOf(selfType))],
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
