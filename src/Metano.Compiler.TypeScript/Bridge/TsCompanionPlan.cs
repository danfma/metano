using Metano.Compiler.IR;
using Metano.Compiler.TypeScript.AST;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// What the companion namespace of a <c>[Branded]</c> / <c>[InlineWrapper]</c>
/// type declares: the primitive it brands, its synthesized members and its
/// static method groups with the name each function takes. The emission
/// (<see cref="IrToTsBrandedBridge"/>) and the call sites
/// (<see cref="TsOverloadClaimsRegistry"/>) both read a plan built here, so a
/// call names the fast path the namespace exports (issue #569).
/// <para>
/// A plan is a function of the type's shape (names, parameters, visibility),
/// never of a body, so the one built from a shape-only extraction equals the
/// one built from the full IR.
/// </para>
/// </summary>
internal sealed class TsCompanionPlan
{
    private TsCompanionPlan(TsType primitiveType, IReadOnlyList<TsCompanionGroup> groups)
    {
        PrimitiveType = primitiveType;
        Groups = groups;
    }

    /// <summary>The TS primitive the brand intersects.</summary>
    public TsType PrimitiveType { get; }

    /// <summary>
    /// Whether the companion synthesizes <c>toString</c>: over a string it
    /// would be the identity.
    /// </summary>
    public bool HasToString => PrimitiveType is not TsStringType;

    /// <summary>The static method groups in member order, none empty.</summary>
    public IReadOnlyList<TsCompanionGroup> Groups { get; }

    /// <summary>
    /// The plan of <paramref name="ir"/>; <c>null</c> when the type is not
    /// branded or wraps something a brand cannot intersect — it then lowers
    /// through the regular class path. The extractor gates the
    /// <c>id.Value</c> → <c>id</c> collapse on the same rule.
    /// </summary>
    public static TsCompanionPlan? Of(IrClassDeclaration ir)
    {
        if (ir.Semantics is not { IsBranded: true, BrandedUnderlyingType: { } underlying })
            return null;

        if (!underlying.IsBrandablePrimitive())
            return null;

        var primitiveType = IrToTsTypeMapper.Map(underlying);

        var statics = (ir.Members ?? [])
            .OfType<IrMethodDeclaration>()
            .Where(method => method.IsStatic)
            .ToList();

        // Namespace functions share one scope: a fast path is claimed against
        // the synthesized members and every static method's own name.
        string[] synthesized =
            primitiveType is TsStringType
                ? [IrToTsBrandedBridge.CreateMember]
                : [IrToTsBrandedBridge.CreateMember, IrToTsBrandedBridge.ToStringMember];

        var takenNames = synthesized
            .Concat(statics.Select(FunctionNameOf))
            .ToHashSet(StringComparer.Ordinal);

        var groups = statics
            .Select(primary => SurvivingOverloadsOf(primary, ir.Semantics))
            .Where(overloads => overloads.Count > 0)
            .Select(overloads => GroupOf(overloads, takenNames))
            .ToList();

        return new TsCompanionPlan(primitiveType, groups);
    }

    /// <summary>
    /// The exported fast path of the static overload declared under
    /// <paramref name="memberName"/> at <paramref name="ordinal"/>
    /// (<see cref="IrOverloadRef"/>); <c>null</c> when the companion
    /// dispatches no such overload.
    /// </summary>
    public string? FastPathAt(string memberName, int ordinal) =>
        Groups
            .SelectMany(group => group.Overloads)
            .FirstOrDefault(overload =>
                overload.Method.Name == memberName && overload.Method.DeclarationOrdinal == ordinal
            )
            ?.FastPath;

    /// <summary>
    /// A static method with the overload siblings folded onto
    /// <see cref="IrMethodDeclaration.Overloads"/>, minus the identity unwrap
    /// conversions (see <see cref="IsIdentityUnwrap"/>).
    /// </summary>
    private static List<IrMethodDeclaration> SurvivingOverloadsOf(
        IrMethodDeclaration primary,
        IrTypeSemantics semantics
    ) =>
        new[] { primary }
            .Concat(primary.Overloads ?? [])
            .Where(method => !IsIdentityUnwrap(method, semantics))
            .ToList();

    /// <summary>
    /// A single surviving method is one function under its own name; two or
    /// more are a dispatcher under that name plus one fast path each, claimed
    /// in dispatch order.
    /// </summary>
    private static TsCompanionGroup GroupOf(
        IReadOnlyList<IrMethodDeclaration> overloads,
        ISet<string> takenNames
    )
    {
        var functionName = FunctionNameOf(overloads[0]);

        if (overloads.Count == 1)
            return new TsCompanionGroup(
                functionName,
                [new TsCompanionOverload(overloads[0], null)]
            );

        var claimed = IrToTsOverloadDispatcherBridge.ClaimNamespaceFastPaths(
            overloads,
            functionName,
            takenNames
        );

        return new TsCompanionGroup(
            functionName,
            claimed
                .Select(claim => new TsCompanionOverload(claim.Overload, claim.FastPath))
                .ToList()
        );
    }

    /// <summary>
    /// A conversion operator from the wrapper to its underlying primitive
    /// (<c>implicit operator string(UserId self) => self.Value</c>, or its
    /// nullable-annotated <c>string?</c> form) is the identity once the
    /// wrapper lowers to <c>primitive &amp; { __brand }</c>, and call sites
    /// never invoke it — the companion skips it. Wrap-direction conversions
    /// (<c>explicit operator UserId(string raw)</c>) return the wrapper, not
    /// the primitive, so they are kept: they can carry validation.
    /// </summary>
    private static bool IsIdentityUnwrap(IrMethodDeclaration method, IrTypeSemantics semantics) =>
        method.Semantics is { IsOperator: true, OperatorKind: "Implicit" or "Explicit" }
        && StripNullable(method.ReturnType).Equals(semantics.BrandedUnderlyingType);

    private static IrTypeRef StripNullable(IrTypeRef type) =>
        type is IrNullableTypeRef nullable ? nullable.Inner : type;

    /// <summary>
    /// Inline wrapper helpers lower to <c>export namespace TypeName { export
    /// function methodName() { … } }</c>. Function declarations inside a TS
    /// namespace can NOT use reserved words (<c>function new() {}</c> is
    /// illegal even though <c>obj.new</c> is fine), so this stays on the
    /// escaping naming policy. The call-site bridge detects [Branded]
    /// receivers and matches the escape so the two halves stay in sync.
    /// </summary>
    private static string FunctionNameOf(IrMethodDeclaration method) =>
        IrToTsNamingPolicy.ToFunctionName(method);
}

/// <summary>
/// One static method group of a companion namespace: the function name its
/// single method or its dispatcher takes, and its overloads in dispatch order.
/// </summary>
internal sealed record TsCompanionGroup(
    string FunctionName,
    IReadOnlyList<TsCompanionOverload> Overloads
)
{
    /// <summary>Two or more overloads: a dispatcher plus one fast path each.</summary>
    public bool IsDispatched => Overloads.Count > 1;

    /// <summary>The C# name the group's methods share.</summary>
    public string MemberName => Overloads[0].Method.Name;

    public IReadOnlyList<IrMethodDeclaration> Methods =>
        Overloads.Select(overload => overload.Method).ToList();
}

/// <summary>
/// One static method of a companion group and the fast-path name it claimed;
/// <c>null</c> for the single method of a group that does not dispatch.
/// </summary>
internal sealed record TsCompanionOverload(IrMethodDeclaration Method, string? FastPath);
