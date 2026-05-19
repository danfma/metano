using Metano.Compiler.IR;
using Metano.Compiler.Mappings;
using Metano.Compiler.TypeScript.AST;
using Metano.Compiler.TypeScript.Transformation;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// Per-member lowering for class shapes: properties (auto vs getter /
/// setter body), methods, fields with C#-style default zero
/// initialisation, and events lowered to a private backing field plus
/// <c>name$add</c> / <c>name$remove</c> runtime-helper forwarders.
/// </summary>
internal static partial class IrToTsClassBridge
{
    /// <summary>
    /// Lowers an <see cref="IrPropertyDeclaration"/> into the matching TS
    /// member shape:
    /// <list type="bullet">
    ///   <item>Auto-property (no getter / setter body) → <see cref="TsFieldMember"/>
    ///     with the initializer or a computed default. <c>get;</c> /
    ///     <c>get; init;</c> map to <c>readonly</c>; <c>get; set;</c> to mutable.</item>
    ///   <item>Computed getter → <see cref="TsGetterMember"/>.</item>
    ///   <item>Custom setter body → <see cref="TsSetterMember"/> (in addition
    ///     to the getter when both are present).</item>
    /// </list>
    /// Returns an empty list when the property's accessibility is below
    /// TS-visible (internal / not-applicable).
    /// </summary>
    public static IReadOnlyList<TsClassMember> BuildProperty(
        IrPropertyDeclaration prop,
        TsType tsType,
        DeclarativeMappingRegistry? bclRegistry
    )
    {
        if (prop.Visibility is IrVisibility.PrivateProtected)
            return [];

        var name = IrToTsNamingPolicy.ToInterfaceMemberName(prop.Name, prop.Attributes);
        var hasGetterBody = prop.Semantics?.HasGetterBody == true;
        var hasSetterBody = prop.Semantics?.HasSetterBody == true;

        // Auto-property — no custom bodies, render as field.
        if (!hasGetterBody && !hasSetterBody)
        {
            var initializer = prop.Initializer is { } init
                ? IrToTsExpressionBridge.Map(init, bclRegistry)
                : ComputeDefaultInitializer(prop.Type);
            var isReadonly =
                prop.Accessors is IrPropertyAccessors.GetOnly or IrPropertyAccessors.GetInit;
            return
            [
                new TsFieldMember(
                    name,
                    tsType,
                    initializer,
                    isReadonly,
                    Accessibility: MapAccessibility(prop.Visibility)
                ),
            ];
        }

        var members = new List<TsClassMember>();
        if (hasGetterBody)
        {
            var body = prop.GetterBody is null
                ? new List<TsStatement>()
                : IrToTsStatementBridge.MapBody(prop.GetterBody, bclRegistry).ToList();
            members.Add(new TsGetterMember(name, tsType, body, Static: prop.IsStatic));
        }
        if (hasSetterBody)
        {
            var body = prop.SetterBody is null
                ? new List<TsStatement>()
                : IrToTsStatementBridge.MapBody(prop.SetterBody, bclRegistry).ToList();
            members.Add(new TsSetterMember(name, new TsParameter("value", tsType), body));
        }
        return members;
    }

    /// <summary>
    /// Lowers an <see cref="IrMethodDeclaration"/> into a <see cref="TsMethodMember"/>.
    /// Parameter and return types come pre-resolved by the caller because the
    /// legacy <see cref="TypeMapper"/> still owns the <c>[ExportFromBcl]</c>
    /// overrides that the IR primitive mapper alone can't reach. Visibility +
    /// accessibility filters live here; <c>[Emit]</c> / <c>[Ignore]</c>
    /// filtering happens in the caller because they decide whether to extract
    /// IR for the method at all.
    /// </summary>
    public static TsMethodMember BuildMethod(
        IrMethodDeclaration method,
        IReadOnlyList<TsParameter> parameters,
        TsType returnType,
        IReadOnlyList<TsTypeParameter>? typeParameters,
        DeclarativeMappingRegistry? bclRegistry
    )
    {
        var name = IrToTsNamingPolicy.ToMethodName(method.Name, method.Attributes);
        // Abstract methods carry no body in C#; the printer emits the
        // signature followed by `;` and skips the not-implemented stub
        // that would otherwise replace a missing body.
        IReadOnlyList<TsStatement> body = method.Semantics.IsAbstract
            ? []
            : IrToTsBodyHelpers.LowerOrNotImplemented(method.Body, method.Name, bclRegistry);
        return new TsMethodMember(
            name,
            parameters,
            returnType,
            body,
            Static: method.IsStatic,
            Async: method.Semantics.IsAsync,
            Generator: method.Semantics.IsGenerator,
            Accessibility: MapAccessibility(method.Visibility),
            TypeParameters: typeParameters,
            Abstract: method.Semantics.IsAbstract
        );
    }

    /// <summary>
    /// Lowers a C# <c>event</c> declaration into the canonical TypeScript
    /// shape: a private nullable backing field plus public <c>name$add</c> /
    /// <c>name$remove</c> methods that funnel through the runtime helpers
    /// <c>delegateAdd</c> and <c>delegateRemove</c>. The handler delegate
    /// type comes pre-resolved from the caller because the legacy
    /// <see cref="TypeMapper"/> still owns <c>[ExportFromBcl]</c> overrides.
    /// </summary>
    public static IReadOnlyList<TsClassMember> BuildEvent(
        IrEventDeclaration evt,
        TsType delegateType
    )
    {
        var name = TypeScriptNaming.ToCamelCaseMember(evt.Name);
        var nullableDelegateType = new TsUnionType([delegateType, new TsNamedType("null")]);
        var eventAccessibility = MapAccessibility(evt.Visibility);
        var handlerParam = new TsParameter("handler", delegateType);

        return
        [
            // Backing field is always private — C# events restrict direct
            // invocation/assignment to the declaring class. Only the
            // $add/$remove methods carry the event's declared accessibility.
            new TsFieldMember(
                name,
                nullableDelegateType,
                Initializer: new TsLiteral("null"),
                Accessibility: TsAccessibility.Private
            ),
            BuildDelegateAccessor(name, handlerParam, "delegateAdd", eventAccessibility),
            BuildDelegateAccessor(name, handlerParam, "delegateRemove", eventAccessibility),
        ];
    }

    private static TsMethodMember BuildDelegateAccessor(
        string eventName,
        TsParameter handlerParam,
        string runtimeHelper,
        TsAccessibility accessibility
    )
    {
        var suffix = runtimeHelper == "delegateAdd" ? "$add" : "$remove";
        return new TsMethodMember(
            $"{eventName}{suffix}",
            [handlerParam],
            new TsVoidType(),
            Body:
            [
                new TsExpressionStatement(
                    new TsBinaryExpression(
                        new TsPropertyAccess(new TsIdentifier("this"), eventName),
                        "=",
                        new TsCallExpression(
                            new TsIdentifier(runtimeHelper),
                            [
                                new TsPropertyAccess(new TsIdentifier("this"), eventName),
                                new TsIdentifier("handler"),
                            ]
                        )
                    )
                ),
            ],
            Accessibility: accessibility
        );
    }

    /// <summary>
    /// Lowers an <see cref="IrFieldDeclaration"/>. Mirrors C#'s
    /// zero-initialization story for instance + static fields: a field
    /// without an explicit initializer reads as the type's default at
    /// runtime (<c>0</c>, <c>false</c>, <c>null</c>, …), so the bridge
    /// fills in the matching default to preserve semantics. Fields whose
    /// value comes from a captured ctor parameter skip the default —
    /// the assignment lives in the ctor body to avoid a TDZ-like read.
    /// Returns <c>null</c> for accessibility levels below TS-visible.
    /// </summary>
    public static TsFieldMember? BuildField(
        IrFieldDeclaration field,
        TsType tsType,
        DeclarativeMappingRegistry? bclRegistry
    )
    {
        if (field.Visibility is IrVisibility.PrivateProtected)
            return null;

        var name = IrToTsNamingPolicy.ToInterfaceMemberName(field.Name, field.Attributes);

        // When the field captures a constructor parameter (DI shape) the
        // assignment moves to the ctor body — emit the field with no
        // initializer here so the value is set exactly once.
        var initializer =
            field.Initializer is not null && !field.IsCapturedByCtor
                ? IrToTsExpressionBridge.Map(field.Initializer, bclRegistry)
                : null;
        if (!field.IsCapturedByCtor)
            initializer ??= ComputeDefaultInitializer(field.Type);

        return new TsFieldMember(
            name,
            tsType,
            initializer,
            field.IsReadonly,
            Static: field.IsStatic,
            Accessibility: MapAccessibility(field.Visibility)
        );
    }
}
