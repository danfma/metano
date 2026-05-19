using Metano.Compiler.IR;
using Metano.Compiler.Mappings;
using Metano.Compiler.TypeScript.AST;
using Metano.Compiler.TypeScript.Transformation;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// Constructor synthesis: promoted-parameter and DI-captured-parameter
/// lists, default-value resolution (including the <c>[StringEnum]</c>
/// member-access shortcut), and the single-constructor body builder
/// that wires <c>super(...)</c> + captured assignments + promoted-params
/// copies + the IR statement body.
/// </summary>
internal static partial class IrToTsClassBridge
{
    /// <summary>
    /// Builds the TS constructor-parameter list for promoted parameters
    /// (C# record positional parameters / C# 12 primary-constructor params
    /// that map to public properties). Each emitted parameter carries the
    /// promoted property's accessibility, the readonly flag (init-only or
    /// get-only ⇒ readonly), the target-resolved <c>[Name]</c> override (or
    /// camelCased parameter name as a fallback), and the parameter's
    /// declared default value when present.
    /// <para>
    /// <paramref name="resolveType"/> takes the IR type and yields the TS
    /// type — the caller routes this through <see cref="TypeMapper"/> so
    /// <c>[ExportFromBcl]</c> overrides (e.g., <c>decimal → Decimal</c>)
    /// still apply. The bridge stays target-agnostic about that resolution.
    /// </para>
    /// </summary>
    public static IReadOnlyList<TsConstructorParam> BuildPromotedCtorParams(
        IrConstructorDeclaration? ctor,
        Func<IrConstructorParameter, TsType> resolveType,
        DeclarativeMappingRegistry? bclRegistry
    )
    {
        if (ctor is null || ctor.Parameters.Count == 0)
            return [];
        var result = new List<TsConstructorParam>();
        foreach (var p in ctor.Parameters)
        {
            if (p.Promotion is IrParameterPromotion.None)
                continue;
            var name = p.EmittedName ?? TypeScriptNaming.ToCamelCase(p.Parameter.Name);
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
                result.Add(
                    new TsConstructorParam(
                        name,
                        tsType,
                        Readonly: false,
                        Accessibility: TsAccessibility.None,
                        DefaultValue: defaultValue,
                        Rest: true
                    )
                );
                continue;
            }
            var isReadonly = p.Promotion is IrParameterPromotion.ReadonlyProperty;
            var accessibility = MapAccessibility(p.PromotedVisibility ?? IrVisibility.Public);
            result.Add(
                new TsConstructorParam(name, tsType, isReadonly, accessibility, defaultValue)
            );
        }
        return result;
    }

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
    public static TsExpression? ResolveCtorParamDefault(
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
    /// Builds the TS constructor-parameter list for DI-captured parameters
    /// (those whose <see cref="IrConstructorParameter.CapturedFieldName"/>
    /// is set). Captured params are NOT promoted to properties — their
    /// values land in private fields via the body assignment
    /// <see cref="BuildSimpleConstructor"/> emits — so the constructor
    /// signature uses <see cref="TsAccessibility.None"/>.
    /// <paramref name="existingNames"/> filters out parameters that already
    /// appear in a promoted-params list (the same param can be both
    /// promoted and back the field for a different member; the promoted
    /// entry wins).
    /// </summary>
    public static IReadOnlyList<TsConstructorParam> BuildCapturedCtorParams(
        IrConstructorDeclaration? ctor,
        Func<IrConstructorParameter, TsType> resolveType,
        ISet<string> existingNames
    )
    {
        if (ctor is null || ctor.Parameters.Count == 0)
            return [];
        var result = new List<TsConstructorParam>();
        foreach (var p in ctor.Parameters)
        {
            if (p.CapturedFieldName is null)
                continue;
            var name = TypeScriptNaming.ToCamelCase(p.Parameter.Name);
            if (existingNames.Contains(name))
                continue;
            result.Add(
                new TsConstructorParam(
                    name,
                    resolveType(p),
                    Accessibility: TsAccessibility.None,
                    Rest: p.Parameter.IsParams
                )
            );
        }
        return result;
    }

    /// <summary>
    /// Lowers the single-constructor case (no overload dispatch) into a
    /// <see cref="TsConstructor"/>. The caller pre-resolves the parameter
    /// list (own + DI-captured) and the optional <c>super(...)</c> argument
    /// list because both still depend on Roslyn-side inheritance walks the
    /// IR doesn't yet model. The body is composed in canonical order:
    /// <list type="number">
    ///   <item><c>super(args)</c> when <paramref name="superArgs"/> is non-null;</item>
    ///   <item>one <c>this.&lt;capturedField&gt; = &lt;paramName&gt;</c>
    ///     assignment per captured parameter (resolved from
    ///     <see cref="IrConstructorParameter.CapturedFieldName"/>);</item>
    ///   <item>any explicit body statements lowered via the IR statement
    ///     bridge (omitted when the IR carries no body — record-style and
    ///     primary-ctor classes don't have one).</item>
    /// </list>
    /// </summary>
    public static TsConstructor BuildSimpleConstructor(
        IrConstructorDeclaration? ir,
        IReadOnlyList<TsConstructorParam> tsCtorParams,
        IReadOnlyList<TsExpression>? superArgs,
        DeclarativeMappingRegistry? bclRegistry
    )
    {
        var body = new List<TsStatement>();
        // `superArgs is not null` signals that the class extends a
        // base — TypeScript requires every derived constructor to
        // call `super(...)` before reading `this`, even when the
        // base constructor takes no arguments. An empty list lowers
        // to a bare `super()` call rather than skipping the
        // statement.
        if (superArgs is not null)
        {
            body.Add(
                new TsExpressionStatement(
                    new TsCallExpression(new TsIdentifier("super"), superArgs)
                )
            );
        }

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
                var paramName = p.EmittedName ?? TypeScriptNaming.ToCamelCase(p.Parameter.Name);
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
