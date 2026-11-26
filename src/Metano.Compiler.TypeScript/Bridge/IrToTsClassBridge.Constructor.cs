using Metano.Compiler.IR;
using Metano.Compiler.Mappings;
using Metano.Compiler.TypeScript.AST;
using Metano.Compiler.TypeScript.Transformation;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// Constructor synthesis: the ordered primary-constructor parameter list
/// (promoted parameter properties + plain forwarded / DI-captured params),
/// default-value resolution (including the <c>[StringEnum]</c>
/// member-access shortcut), and the single-constructor body builder
/// that wires <c>super(...)</c> + captured assignments + promoted-params
/// copies + the IR statement body.
/// </summary>
internal static partial class IrToTsClassBridge
{
    /// <summary>
    /// The temporaries a base call evaluates its arguments into when C#
    /// evaluates them out of parameter order (<c>__baseArg0</c>).
    /// </summary>
    private const string BaseArgumentPrefix = "__baseArg";

    /// <summary>
    /// The optional trailing constructor parameter carrying a reified generic
    /// class's runtime type arguments (spec 011 W5-5(b), (i)).
    /// </summary>
    public const string TypeArgsParameterName = "$typeArgs";

    /// <summary>
    /// The non-enumerable per-declaring-class property name storing a reified
    /// generic class's runtime type arguments (spec 011 W5-5(a)).
    /// </summary>
    public static string SlotNameOf(string tsClassName) => $"$typeArguments${tsClassName}";

    /// <summary>
    /// Builds <c>readonly [RuntimeType&lt;T&gt;, ...]</c> for a reified generic
    /// class's full-arity slot and constructor <c>$typeArgs</c> parameter.
    /// </summary>
    public static TsTupleType TypeArgsTupleType(IReadOnlyList<IrTypeParameter> typeParameters) =>
        new(
            [
                .. typeParameters.Select(tp =>
                    (TsType)
                        new TsNamedType(
                            "RuntimeType",
                            [new TsNamedType(tp.Name)],
                            RuntimeHelperBinding: "RuntimeType"
                        )
                ),
            ],
            IsReadonly: true
        );

    /// <summary>
    /// Builds the optional trailing <c>$typeArgs?: readonly [RuntimeType&lt;T&gt;, ...]</c>
    /// constructor parameter, inserted before any <c>params</c> rest parameter
    /// (spec 011 W5-5(b)).
    /// </summary>
    public static IReadOnlyList<TsConstructorParam> WithTypeArgsParam(
        IReadOnlyList<TsConstructorParam> parameters,
        IReadOnlyList<IrTypeParameter> typeParameters
    )
    {
        var typeArgsParam = new TsConstructorParam(
            TypeArgsParameterName,
            TypeArgsTupleType(typeParameters),
            Accessibility: TsAccessibility.None,
            Optional: true
        );

        var restIndex = -1;

        for (var i = 0; i < parameters.Count; i++)
        {
            if (parameters[i].Rest)
            {
                restIndex = i;
                break;
            }
        }

        if (restIndex < 0)
            return [.. parameters, typeArgsParam];

        var updated = new List<TsConstructorParam>(parameters.Count + 1);
        updated.AddRange(parameters.Take(restIndex));
        updated.Add(typeArgsParam);
        updated.AddRange(parameters.Skip(restIndex));

        return updated;
    }

    /// <summary>
    /// Builds <c>declare readonly $typeArguments$&lt;Class&gt;: readonly [RuntimeType&lt;T&gt;, ...];</c>
    /// for a generic class that defines a runtime type-argument slot (spec 011 W5-5(a)).
    /// </summary>
    public static TsFieldMember BuildSlotDeclaration(
        string slotName,
        IReadOnlyList<IrTypeParameter> typeParameters
    ) =>
        new(
            slotName,
            TypeArgsTupleType(typeParameters),
            Readonly: true,
            Accessibility: TsAccessibility.None,
            Declare: true
        );

    /// <summary>
    /// Builds <c>Object.defineProperty(this, "$typeArguments$&lt;Class&gt;", { value: $typeArgs, enumerable: false });</c>,
    /// emitted first in the constructor body (spec 011 W5-5(i)).
    /// </summary>
    public static TsStatement BuildSlotDefineProperty(
        string slotName,
        string typeArgsParameterName = TypeArgsParameterName
    ) =>
        new TsExpressionStatement(
            new TsCallExpression(
                new TsPropertyAccess(new TsIdentifier("Object"), "defineProperty"),
                [
                    new TsIdentifier("this"),
                    new TsStringLiteral(slotName),
                    new TsObjectLiteral([
                        new TsObjectProperty("value", new TsIdentifier(typeArgsParameterName)),
                        new TsObjectProperty("enumerable", new TsLiteral("false")),
                    ]),
                ]
            )
        );

    /// <summary>
    /// Builds the TS constructor-parameter list for the primary constructor,
    /// one plain parameter per C# parameter in declaration order so positional
    /// call sites (<c>new T(a, b)</c>, the JSON factory) line up with the
    /// emitted signature. A promoted parameter is passed through its member's
    /// name (<see cref="PromotedParameterName"/>); the member is a declared
    /// field the constructor stores (W10-1), not a parameter property, which
    /// TypeScript would define as a class field.
    /// <para>
    /// <paramref name="resolveType"/> takes the IR parameter and yields the
    /// TS type — the caller routes this through <see cref="TypeMapper"/> so
    /// <c>[ExportFromBcl]</c> overrides (e.g., <c>decimal → Decimal</c>)
    /// still apply. The bridge stays target-agnostic about that resolution.
    /// </para>
    /// </summary>
    public static IReadOnlyList<TsConstructorParam> BuildPrimaryCtorParams(
        IrConstructorDeclaration? ctor,
        Func<IrConstructorParameter, TsType> resolveType,
        IrToTsBridgeContext context
    )
    {
        if (ctor is null)
            return [];

        return ctor
            .Parameters.Select(p => new TsConstructorParam(
                IsPromoted(p)
                    ? PromotedParameterName(p)
                    : IrToTsNamingPolicy.ToParameterName(p.Parameter.Name),
                resolveType(p),
                Accessibility: TsAccessibility.None,
                DefaultValue: ResolveCtorParamDefault(p, context),
                Rest: p.Parameter.IsRestParams
            ))
            .ToList();
    }

    /// <summary>
    /// Emitted member names of the primary constructor's promoted
    /// parameters — the properties the constructor signature already
    /// declares, which the member walk must not emit a second time.
    /// </summary>
    public static IReadOnlySet<string> PromotedMemberNames(IrConstructorDeclaration? ctor) =>
        (ctor?.Parameters ?? [])
            .Where(IsPromoted)
            .Select(PromotedName)
            .ToHashSet(StringComparer.Ordinal);

    private static bool IsPromoted(IrConstructorParameter p) =>
        p.Promotion is not IrParameterPromotion.None;

    /// <summary>
    /// The member a promoted parameter declares: the property it is promoted to,
    /// under the data-member rule (<c>Of</c> → <c>of_</c>, a positional <c>@new</c>
    /// → <c>new</c>).
    /// </summary>
    internal static string PromotedName(IrConstructorParameter p) =>
        IrToTsNamingPolicy.ToDataMemberName(
            p.PromotedPropertyName ?? p.Parameter.Name,
            p.EmittedName
        );

    /// <summary>
    /// A promoted member named like a JS reserved word (<c>record R(int @new)</c>):
    /// a valid property name but not a parameter name, so it cannot be a parameter
    /// property. It is declared as a field, assigned from the escaped parameter
    /// (<c>new_</c>), and keeps its public name (#404).
    /// </summary>
    private static bool HasReservedPromotedName(IrConstructorParameter p) =>
        IsPromoted(p) && TypeScriptNaming.EscapeIfReserved(PromotedName(p)) != PromotedName(p);

    /// <summary>
    /// The constructor parameter a promoted member is passed through: the member's
    /// own name, or the escaped parameter name when that name is reserved.
    /// </summary>
    private static string PromotedParameterName(IrConstructorParameter p) =>
        HasReservedPromotedName(p)
            ? IrToTsNamingPolicy.ToParameterName(p.Parameter.Name)
            : PromotedName(p);

    /// <summary>The TypeScript visibility of a promoted member: its property's.</summary>
    internal static TsMemberVisibility PromotedMemberVisibility(IrConstructorParameter p) =>
        IrToTsVisibilityMapper.Map(p.PromotedVisibility ?? IrVisibility.Public);

    /// <summary>
    /// Lowers a constructor parameter's default value to TS. The general
    /// case routes through the IR expression bridge, but a
    /// <c>[StringEnum]</c>-typed parameter whose default is a member access
    /// (e.g., <c>= Priority.Medium</c>) collapses to a bare string literal
    /// (<c>= "medium"</c>) — both are runtime-equivalent because the
    /// <c>type</c> alias for a string enum is the value union, but the
    /// literal form matches the legacy convention and avoids the property
    /// access at the call site.
    /// </summary>
    internal static TsExpression? ResolveCtorParamDefault(
        IrConstructorParameter p,
        IrToTsBridgeContext context
    )
    {
        if (p.Parameter.DefaultValue is not { } d)
            return null;
        if (
            p.Parameter.Type is IrNamedTypeRef { Semantics.Kind: IrNamedTypeKind.StringEnum }
            && d is IrMemberAccess { Origin.EmittedName: { } literal }
        )
            return new TsStringLiteral(literal);
        return IrToTsExpressionBridge.Map(d, context);
    }

    /// <summary>
    /// The base-constructor call of <paramref name="ctor"/>: the role-mapped
    /// arguments of its runtime class when the base is a BCL exception (see
    /// <see cref="IrToTsExceptionConstructionBridge"/>); the base overload's
    /// selector before the bound arguments when the base call resolved into a
    /// dispatched set (<c>super(Money.byMoneyAmount, amount)</c>, spec 013
    /// FR-017); the plain base arguments otherwise. Returns <c>null</c> when
    /// the constructor passes nothing to its base and the class keeps no base
    /// message.
    /// </summary>
    /// <param name="ctor">The constructor making the base call.</param>
    /// <param name="baseClass">The class's <c>extends</c> type, which
    /// qualifies the base overload's selector.</param>
    /// <param name="keepsBaseMessage">Whether the class overrides
    /// <c>Message</c>, so the call is followed by the base-message capture.</param>
    public static TsBaseConstructorCall? BuildBaseCall(
        IrConstructorDeclaration ctor,
        TsType? baseClass,
        bool keepsBaseMessage,
        IrToTsBridgeContext context
    )
    {
        if (ctor.BaseExceptionConstruction is { } construction)
        {
            return IrToTsExceptionConstructionBridge.MapBaseExceptionCall(
                ctor,
                construction,
                context
            ) with
            {
                KeepsBaseMessage = keepsBaseMessage,
            };
        }

        var baseArguments = ctor.BaseArguments ?? [];
        var selector = BaseSelectorOf(ctor, baseClass);

        if (PassesNothingToBase(baseArguments, selector, keepsBaseMessage))
            return null;

        // The arguments run before `super(...)`, as statements of the constructor
        // there may: the temporaries they assign inline are declared ahead of it.
        // An argument that binds `out` locals keeps the arrow extraction wraps it
        // in (ExtractStandalone): hoisting its statements ahead of `super` would
        // run them before the arguments to its left.
        IReadOnlyList<TsExpression> loweredArguments = [];

        var temporaries = TsTemporaries.Around(context, scoped =>
        {
            loweredArguments =
            [
                .. baseArguments.Select(argument =>
                    IrToTsExpressionBridge.MapArgument(argument, scoped)
                ),
            ];

            return [];
        });

        var ordered = TsEvaluationOrder.Bind(
            IrToTsExpressionBridge.WithoutNullRestArray(baseArguments, loweredArguments),
            ctor.BaseOverload?.EvaluationOrder ?? ctor.BaseEvaluationOrder,
            BaseArgumentPrefix,
            context: null
        );

        return new TsBaseConstructorCall(
            [
                .. temporaries,
                .. ordered.Bindings.Select(binding => new TsVariableDeclaration(
                    binding.Name,
                    binding.Value
                )),
            ],
            selector is null ? ordered.Arguments : [selector, .. ordered.Arguments],
            keepsBaseMessage
        );
    }

    private static bool PassesNothingToBase(
        IReadOnlyList<IrArgument> baseArguments,
        TsPropertyAccess? selector,
        bool keepsBaseMessage
    ) => baseArguments.Count == 0 && selector is null && !keepsBaseMessage;

    /// <summary>
    /// <c>Money.byMoneyAmount</c>: the claimed selector of the base overload
    /// <paramref name="ctor"/> calls; <c>null</c> when the call keeps the
    /// base dispatcher.
    /// </summary>
    private static TsPropertyAccess? BaseSelectorOf(
        IrConstructorDeclaration ctor,
        TsType? baseClass
    )
    {
        if (ctor.BaseOverload is not { } overload || baseClass is not TsNamedType named)
            return null;

        if (TsOverloadCallSites.ConstructorNamesOf(overload.Target) is not { } names)
            return null;

        return new TsPropertyAccess(
            IrToTsExpressionBridge.TypeValueReference(named.Name, named.Origin),
            names.Selector
        );
    }

    /// <summary>
    /// Lowers the single-constructor case (no overload dispatch) into a
    /// <see cref="TsConstructor"/>. The caller pre-resolves the parameter
    /// list (<see cref="BuildPrimaryCtorParams"/>) and the optional
    /// <c>super(...)</c> argument list. The body is composed in canonical order:
    /// <list type="number">
    ///   <item><c>super(args)</c> (after any argument temporaries) when
    ///     <paramref name="baseCall"/> is non-null;</item>
    ///   <item>the type-argument slot of a reified generic class;</item>
    ///   <item>the instance field stores (<see cref="FieldStoresOf"/>);</item>
    ///   <item>any explicit body statements lowered via the IR statement
    ///     bridge (omitted when the IR carries no body — record-style and
    ///     primary-ctor classes don't have one).</item>
    /// </list>
    /// </summary>
    public static TsConstructor BuildSimpleConstructor(
        IrConstructorDeclaration? ir,
        IReadOnlyList<TsConstructorParam> tsCtorParams,
        TsBaseConstructorCall? baseCall,
        IrToTsBridgeContext context,
        string? typeArgumentSlot = null
    )
    {
        var body = new List<TsStatement>();
        // `baseCall is not null` signals that the class extends a
        // base — TypeScript requires every derived constructor to
        // call `super(...)` before reading `this`, even when the
        // base constructor takes no arguments. Empty arguments lower
        // to a bare `super()` call rather than skipping the
        // statement; argument temporaries (source-order evaluation)
        // precede it.
        if (baseCall is not null)
            body.AddRange(baseCall.ToStatements());

        if (typeArgumentSlot is not null)
            body.Add(BuildSlotDefineProperty(typeArgumentSlot));

        body.Add(
            ir is null
                ? new TsInstanceFieldStores([], [])
                : FieldStoresOf(ir, p => new TsFieldStoreBinding(
                    context.ClassFields.FieldOf(p.Parameter.Name) ?? PromotedName(p),
                    new TsIdentifier(PromotedParameterName(p))
                ))
        );

        if (ir?.Body is { Count: > 0 } explicitBody)
            body.AddRange(IrToTsStatementBridge.MapBody(explicitBody, context));

        return new TsConstructor(tsCtorParams, body);
    }

    /// <summary>
    /// The field stores of a constructor that runs the C# initializers, with
    /// the members its parameters set: each promoted member
    /// (<paramref name="promotedBinding"/>, in parameter order; none when it is
    /// <c>null</c>, as the members keep their own declarations) and each field
    /// a parameter is captured into (DI-style; the parameter binding uses the
    /// local policy, the captured field the data-member rule — camelCase,
    /// reserved words escaped — without a [Name] override).
    /// </summary>
    public static TsInstanceFieldStores FieldStoresOf(
        IrConstructorDeclaration ctor,
        Func<IrConstructorParameter, TsFieldStoreBinding>? promotedBinding
    ) =>
        new(
            promotedBinding is null
                ? []
                : [.. ctor.Parameters.Where(IsPromoted).Select(promotedBinding)],
            [.. ctor.Parameters.Where(p => p.CapturedFieldName is not null).Select(CapturedBinding)]
        );

    /// <summary>
    /// The binding of a dispatcher branch's promoted member: the branch reads
    /// the parameter under the local policy.
    /// </summary>
    public static TsFieldStoreBinding PromotedBranchBinding(IrConstructorParameter p) =>
        new(
            PromotedName(p),
            new TsIdentifier(IrToTsNamingPolicy.ToParameterName(p.Parameter.Name))
        );

    private static TsFieldStoreBinding CapturedBinding(IrConstructorParameter p) =>
        new(
            IrToTsNamingPolicy.ToDeclaredDataMemberName(p.CapturedFieldName!, attributes: null),
            new TsIdentifier(IrToTsNamingPolicy.ToParameterName(p.Parameter.Name))
        );
}

/// <summary>
/// A lowered base-constructor call: <paramref name="Temporaries"/> to declare
/// before <c>super(...)</c> (the temporaries its arguments assign inline, and
/// the arguments bound to be evaluated in source order), the
/// <c>super</c> <paramref name="Arguments"/>, and whether the class keeps its
/// base message (overrides <c>Message</c>), which appends the capture of the
/// native message right after <c>super</c>.
/// </summary>
public sealed record TsBaseConstructorCall(
    IReadOnlyList<TsStatement> Temporaries,
    IReadOnlyList<TsExpression> Arguments,
    bool KeepsBaseMessage = false
)
{
    /// <summary>The temporaries, the <c>super(...)</c> statement and, when
    /// the class keeps its base message, its capture.</summary>
    public IReadOnlyList<TsStatement> ToStatements()
    {
        var superCall = new TsExpressionStatement(
            new TsCallExpression(new TsIdentifier("super"), Arguments)
        );

        return KeepsBaseMessage
            ? [.. Temporaries, superCall, .. IrToTsExceptionConstructionBridge.CaptureBaseMessage()]
            : [.. Temporaries, superCall];
    }
}
