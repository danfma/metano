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
    /// Builds the TS constructor-parameter list for the primary constructor,
    /// one entry per C# parameter in declaration order so positional call
    /// sites (<c>new T(a, b)</c>, the JSON factory) line up with the emitted
    /// signature. Promoted parameters
    /// become TS parameter properties (see
    /// <see cref="BuildPromotedCtorParam"/>); every other parameter — those
    /// forwarded to the base constructor (<c>record ImageClip(string Path,
    /// double Seconds) : Clip(Seconds)</c>), DI-captured ones, or ones only
    /// read by the body — stays a plain parameter with
    /// <see cref="TsAccessibility.None"/> so it neither redeclares an
    /// inherited property nor leaks a public member.
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
        DeclarativeMappingRegistry? bclRegistry
    )
    {
        if (ctor is null)
            return [];

        return ctor
            .Parameters.Select(p =>
                IsPromoted(p)
                    ? BuildPromotedCtorParam(p, resolveType, bclRegistry)
                    : BuildPlainCtorParam(p, resolveType, bclRegistry)
            )
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

    private static string PromotedName(IrConstructorParameter p) =>
        IrToTsNamingPolicy.ToDataMemberName(p.Parameter.Name, p.EmittedName);

    /// <summary>
    /// Lowers one promoted parameter: the promoted property's accessibility,
    /// the readonly flag (init-only or get-only ⇒ readonly), the
    /// target-resolved <c>[Name]</c> override (or camelCased parameter name
    /// as a fallback), and the declared default value when present.
    /// </summary>
    private static TsConstructorParam BuildPromotedCtorParam(
        IrConstructorParameter p,
        Func<IrConstructorParameter, TsType> resolveType,
        DeclarativeMappingRegistry? bclRegistry
    )
    {
        var name = PromotedName(p);
        var tsType = resolveType(p);
        var defaultValue = ResolveCtorParamDefault(p, bclRegistry);

        // TypeScript forbids combining a parameter-property modifier
        // (`readonly` / `public`) with the rest prefix (`...`). For a
        // record positional `params T[]` slot, drop the modifiers and
        // emit the rest form; the field declaration + body assignment
        // are synthesized separately by the class emitter so the
        // promoted shape is preserved at the surface. (#152)
        if (p.Parameter.IsParams)
        {
            return new TsConstructorParam(
                name,
                tsType,
                Readonly: false,
                Accessibility: TsAccessibility.None,
                DefaultValue: defaultValue,
                Rest: true
            );
        }

        var isReadonly = p.Promotion is IrParameterPromotion.ReadonlyProperty;
        var accessibility = MapAccessibility(p.PromotedVisibility ?? IrVisibility.Public);

        return new TsConstructorParam(name, tsType, isReadonly, accessibility, defaultValue);
    }

    /// <summary>
    /// Lowers one non-promoted parameter to a plain TS parameter (no
    /// parameter-property modifier). Its name matches what
    /// <see cref="BuildSimpleConstructor"/> reads for DI-captured
    /// assignments and what the base-initializer arguments reference.
    /// </summary>
    private static TsConstructorParam BuildPlainCtorParam(
        IrConstructorParameter p,
        Func<IrConstructorParameter, TsType> resolveType,
        DeclarativeMappingRegistry? bclRegistry
    ) =>
        new(
            TypeScriptNaming.ToCamelCase(p.Parameter.Name),
            resolveType(p),
            Accessibility: TsAccessibility.None,
            DefaultValue: ResolveCtorParamDefault(p, bclRegistry),
            Rest: p.Parameter.IsParams
        );

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
    private static TsExpression? ResolveCtorParamDefault(
        IrConstructorParameter p,
        DeclarativeMappingRegistry? bclRegistry
    )
    {
        if (p.Parameter.DefaultValue is not { } d)
            return null;
        if (
            p.Parameter.Type is IrNamedTypeRef { Semantics.Kind: IrNamedTypeKind.StringEnum }
            && d is IrMemberAccess { Origin.EmittedName: { } literal }
        )
            return new TsStringLiteral(literal);
        return IrToTsExpressionBridge.Map(d, bclRegistry);
    }

    /// <summary>
    /// Lowers the single-constructor case (no overload dispatch) into a
    /// <see cref="TsConstructor"/>. The caller pre-resolves the parameter
    /// list (<see cref="BuildPrimaryCtorParams"/>) and the optional
    /// <c>super(...)</c> argument list. The body is composed in canonical order:
    /// <list type="number">
    ///   <item><c>super(args)</c> (after any argument temporaries) when
    ///     <paramref name="baseCall"/> is non-null;</item>
    ///   <item>one <c>this.&lt;capturedField&gt; = &lt;paramName&gt;</c>
    ///     assignment per captured parameter (resolved from
    ///     <see cref="IrConstructorParameter.CapturedFieldName"/>);</item>
    ///   <item>any explicit body statements lowered via the IR statement
    ///     bridge (omitted when the IR carries no body — record-style and
    ///     primary-ctor classes don't have one).</item>
    /// </list>
    /// </summary>
    /// <summary>
    /// The base-constructor call of <paramref name="ctor"/>: role-mapped
    /// <c>Error</c> arguments when the base is a BCL exception (see
    /// <see cref="IrToTsExceptionConstructionBridge"/>), the plain base
    /// arguments otherwise. Returns <c>null</c> when the constructor passes
    /// nothing to its base and the class keeps no base message.
    /// </summary>
    /// <param name="ctor">The constructor making the base call.</param>
    /// <param name="keepsBaseMessage">Whether the class overrides
    /// <c>Message</c>, so the call is followed by the base-message capture.</param>
    /// <param name="bclRegistry">The declarative BCL mappings.</param>
    public static TsBaseConstructorCall? BuildBaseCall(
        IrConstructorDeclaration ctor,
        bool keepsBaseMessage,
        DeclarativeMappingRegistry? bclRegistry
    )
    {
        if (ctor.BaseExceptionConstruction is { } construction)
        {
            return IrToTsExceptionConstructionBridge.MapBaseExceptionCall(
                ctor,
                construction,
                bclRegistry
            ) with
            {
                KeepsBaseMessage = keepsBaseMessage,
            };
        }

        var baseArguments = ctor.BaseArguments ?? [];

        if (baseArguments.Count == 0 && !keepsBaseMessage)
            return null;

        return new TsBaseConstructorCall(
            [],
            baseArguments
                .Select(argument => IrToTsExpressionBridge.MapArgument(argument, bclRegistry))
                .ToList(),
            keepsBaseMessage
        );
    }

    public static TsConstructor BuildSimpleConstructor(
        IrConstructorDeclaration? ir,
        IReadOnlyList<TsConstructorParam> tsCtorParams,
        TsBaseConstructorCall? baseCall,
        DeclarativeMappingRegistry? bclRegistry
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

        if (ir is not null)
        {
            foreach (var p in ir.Parameters)
            {
                if (p.CapturedFieldName is null)
                    continue;
                var paramName = TypeScriptNaming.ToCamelCase(p.Parameter.Name);
                var fieldName = TypeScriptNaming.ToCamelCase(p.CapturedFieldName);
                body.Add(
                    new TsExpressionStatement(
                        new TsBinaryExpression(
                            new TsPropertyAccess(new TsIdentifier("this"), fieldName),
                            "=",
                            new TsIdentifier(paramName)
                        )
                    )
                );
            }

            // Promoted `params T[]` slots can't carry the parameter-property
            // modifier (TS forbids `readonly ...x: T[]`), so the field is
            // declared separately by the class emitter and the value is
            // copied across in the ctor body. (#152)
            foreach (var p in ir.Parameters)
            {
                if (!p.Parameter.IsParams || p.Promotion is IrParameterPromotion.None)
                    continue;
                var paramName = PromotedName(p);
                body.Add(
                    new TsExpressionStatement(
                        new TsBinaryExpression(
                            new TsPropertyAccess(new TsIdentifier("this"), paramName),
                            "=",
                            new TsIdentifier(paramName)
                        )
                    )
                );
            }

            // Explicit ctor body statements (a non-record ctor with body
            // beyond captured-param assignments) — append after the
            // synthesized super + captured assignments so the resulting body
            // matches the source order.
            if (ir.Body is { Count: > 0 } explicitBody)
                body.AddRange(IrToTsStatementBridge.MapBody(explicitBody, bclRegistry));
        }

        return new TsConstructor(tsCtorParams, body);
    }
}

/// <summary>
/// A lowered base-constructor call: <paramref name="Temporaries"/> to declare
/// before <c>super(...)</c> (arguments evaluated in source order), the
/// <c>super</c> <paramref name="Arguments"/>, and whether the class keeps its
/// base message (overrides <c>Message</c>), which appends the capture of the
/// native message right after <c>super</c>.
/// </summary>
public sealed record TsBaseConstructorCall(
    IReadOnlyList<TsVariableDeclaration> Temporaries,
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
