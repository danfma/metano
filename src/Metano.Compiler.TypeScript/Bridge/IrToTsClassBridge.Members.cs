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
    ///   <item>A static property keeps <c>static</c> on every emitted member
    ///     (field, getter and setter), so <c>Type.Prop</c> reads the class.</item>
    ///   <item>Abstract property → abstract declaration with no storage
    ///     (<see cref="BuildAbstractProperty"/>).</item>
    ///   <item>Auto-property of an override chain
    ///     (<see cref="IrPropertySemantics.OwnsPerClassStorage"/>) → a private backing
    ///     field per class plus a getter (and a setter when C# declares <c>set</c> /
    ///     <c>init</c>) (<see cref="BuildPerClassStorageProperty"/>).</item>
    ///   <item>Computed getter → <see cref="TsGetterMember"/>.</item>
    ///   <item>Custom setter body → <see cref="TsSetterMember"/> (in addition
    ///     to the getter when both are present).</item>
    /// </list>
    /// Returns an empty list for a <c>private protected</c> property: no
    /// TypeScript accessibility expresses it — <c>public</c> would widen it
    /// and <c>private</c> would hide it from the derived classes C# lets
    /// read it.
    /// </summary>
    public static IReadOnlyList<TsClassMember> BuildProperty(
        IrPropertyDeclaration prop,
        TsType tsType,
        IrToTsBridgeContext context
    )
    {
        if (prop.Visibility is IrVisibility.PrivateProtected)
            return [];

        var name = IrToTsNamingPolicy.ToDeclaredDataMemberName(prop.Name, prop.Attributes);
        var (accessibility, jsdoc) = IrToTsVisibilityMapper.Map(prop.Visibility);

        if (prop.Semantics?.IsAbstract == true)
            return [BuildAbstractProperty(prop, name, tsType)];

        if (PerClassBackingFields.DeclaresBackingField(prop))
            return WithInheritedAccessor(
                BuildPerClassStorageProperty(prop, name, tsType, context),
                prop,
                name,
                tsType
            );

        var hasGetterBody = prop.Semantics?.HasGetterBody == true;
        var hasSetterBody = prop.Semantics?.HasSetterBody == true;

        // Auto-property — no custom bodies, render as field.
        if (!hasGetterBody && !hasSetterBody)
        {
            var initializer = prop.Initializer is { } init
                ? IrToTsExpressionBridge.Map(init, context)
                : ComputeDefaultInitializer(prop.Type);
            return
            [
                new TsFieldMember(
                    name,
                    tsType,
                    initializer,
                    IsReadOnly(prop),
                    Static: prop.IsStatic,
                    Accessibility: accessibility,
                    DefinitelyAssigned: prop.Semantics?.IsRequired == true,
                    JSDoc: jsdoc
                ),
            ];
        }

        var members = new List<TsClassMember>();

        if (hasGetterBody)
        {
            var body = prop.GetterBody is null
                ? new List<TsStatement>()
                : IrToTsStatementBridge.MapBody(prop.GetterBody, context).ToList();

            members.Add(
                new TsGetterMember(name, tsType, body, Static: prop.IsStatic, JSDoc: jsdoc)
            );
        }

        if (hasSetterBody)
        {
            var body = prop.SetterBody is null
                ? new List<TsStatement>()
                : IrToTsStatementBridge.MapBody(prop.SetterBody, context).ToList();

            members.Add(
                new TsSetterMember(
                    name,
                    new TsParameter("value", tsType),
                    body,
                    Static: prop.IsStatic,
                    JSDoc: jsdoc
                )
            );
        }

        return WithInheritedAccessor(members, prop, name, tsType);
    }

    /// <summary>
    /// An override that declares one accessor of a property whose other accessor stays
    /// the base's (<see cref="IrInheritedAccessor"/>) forwards the missing one to
    /// <c>super</c>: a JavaScript accessor declaration replaces the base's pair, so the
    /// inherited half would otherwise be gone (a write would throw, a read yield
    /// <c>undefined</c>).
    /// </summary>
    private static List<TsClassMember> WithInheritedAccessor(
        List<TsClassMember> members,
        IrPropertyDeclaration prop,
        string name,
        TsType tsType
    )
    {
        var superMember = new TsPropertyAccess(new TsIdentifier("super"), name);
        var (accessibility, jsdoc) = IrToTsVisibilityMapper.Map(prop.Visibility);

        switch (prop.Semantics?.InheritedAccessor)
        {
            case IrInheritedAccessor.Setter:
                members.Add(
                    new TsSetterMember(
                        name,
                        new TsParameter("value", tsType),
                        [
                            new TsExpressionStatement(
                                new TsBinaryExpression(superMember, "=", new TsIdentifier("value"))
                            ),
                        ],
                        Accessibility: accessibility,
                        JSDoc: jsdoc
                    )
                );
                break;

            case IrInheritedAccessor.Getter:
                members.Add(
                    new TsGetterMember(
                        name,
                        tsType,
                        [new TsReturnStatement(superMember)],
                        Accessibility: accessibility,
                        JSDoc: jsdoc
                    )
                );
                break;
        }

        return members;
    }

    /// <summary>
    /// An overridable auto-property becomes
    /// <c>private readonly p$Shape_k3x9: T = init; get p(): T { return this.p$Shape_k3x9; }</c>,
    /// with a setter writing the same field when C# declares <c>set</c> or <c>init</c>
    /// (so <c>with</c> and object initializers still assign it). The accessor lives on the
    /// prototype, so a derived accessor overrides it, and <c>super.p</c> reads the base's
    /// own field (<see cref="PerClassBackingFields"/>).
    /// </summary>
    private static List<TsClassMember> BuildPerClassStorageProperty(
        IrPropertyDeclaration prop,
        string name,
        TsType tsType,
        IrToTsBridgeContext context
    )
    {
        // DeclaresBackingField (checked by the caller) guarantees the field exists.
        var fieldName = context.ClassFields.FieldOf(prop)!;
        var (accessibility, jsdoc) = IrToTsVisibilityMapper.Map(prop.Visibility);
        var storage = PerClassBackingFields.StorageAccess(fieldName);
        var writable = prop.Accessors is not IrPropertyAccessors.GetOnly;

        var initializer = prop.Initializer is { } init
            ? IrToTsExpressionBridge.Map(init, context)
            : ComputeDefaultInitializer(prop.Type);

        var members = new List<TsClassMember>
        {
            new TsFieldMember(
                fieldName,
                tsType,
                initializer,
                Readonly: !writable,
                Accessibility: TsAccessibility.Private,
                DefinitelyAssigned: prop.Semantics?.IsRequired == true
            ),
            new TsGetterMember(
                name,
                tsType,
                [new TsReturnStatement(storage)],
                Accessibility: accessibility,
                JSDoc: jsdoc
            ),
        };

        if (writable)
            members.Add(
                new TsSetterMember(
                    name,
                    new TsParameter("value", tsType),
                    [
                        new TsExpressionStatement(
                            new TsBinaryExpression(storage, "=", new TsIdentifier("value"))
                        ),
                    ],
                    Accessibility: accessibility,
                    JSDoc: jsdoc
                )
            );

        return members;
    }

    /// <summary>
    /// An abstract property becomes an abstract property declaration
    /// (<c>abstract readonly kind: string;</c>) with no initializer: like the
    /// C# one, it reserves no storage, so it never shadows the field,
    /// parameter property or accessor a derived class implements it with.
    /// </summary>
    private static TsFieldMember BuildAbstractProperty(
        IrPropertyDeclaration prop,
        string name,
        TsType tsType
    )
    {
        var (accessibility, jsdoc) = IrToTsVisibilityMapper.Map(prop.Visibility);

        return new TsFieldMember(
            name,
            tsType,
            Readonly: IsReadOnly(prop),
            Accessibility: accessibility,
            Abstract: true,
            JSDoc: jsdoc
        );
    }

    /// <summary><c>get;</c> and <c>get; init;</c> map to a <c>readonly</c> member.</summary>
    private static bool IsReadOnly(IrPropertyDeclaration prop) =>
        prop.Accessors is IrPropertyAccessors.GetOnly or IrPropertyAccessors.GetInit;

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
        IrToTsBridgeContext context
    )
    {
        var name = IrToTsNamingPolicy.ToMethodName(method);
        // Abstract methods carry no body in C#; the printer emits the
        // signature followed by `;` and skips the not-implemented stub
        // that would otherwise replace a missing body.
        IReadOnlyList<TsStatement> body = method.Semantics.IsAbstract
            ? []
            : IrToTsBodyHelpers.LowerOrNotImplemented(method.Body, method.Name, context);
        var (accessibility, jsdoc) = IrToTsVisibilityMapper.Map(method.Visibility);

        return new TsMethodMember(
            name,
            parameters,
            returnType,
            body,
            Static: method.IsStatic,
            Async: method.Semantics.IsAsync,
            Generator: method.Semantics.IsGenerator,
            Accessibility: accessibility,
            TypeParameters: typeParameters,
            Abstract: method.Semantics.IsAbstract,
            JSDoc: jsdoc
        );
    }

    /// <summary>
    /// Lowers a C# <c>event</c> declaration into the canonical TypeScript
    /// shape: a private nullable backing field plus public <c>name$add</c> /
    /// <c>name$remove</c> methods that funnel through the runtime helpers
    /// <c>delegateAdd</c> and <c>delegateRemove</c>. The handler delegate
    /// type comes pre-resolved from the caller because the legacy
    /// <see cref="TypeMapper"/> still owns <c>[ExportFromBcl]</c> overrides.
    /// Returns an empty list for a <c>private protected</c> event, as
    /// <see cref="BuildProperty"/> and <see cref="BuildField"/> skip a
    /// <c>private protected</c> member.
    /// </summary>
    public static IReadOnlyList<TsClassMember> BuildEvent(
        IrEventDeclaration evt,
        TsType delegateType
    )
    {
        if (evt.Visibility is IrVisibility.PrivateProtected)
            return [];

        var name = TypeScriptNaming.ToCamelCaseMember(evt.Name);
        var nullableDelegateType = new TsUnionType([delegateType, new TsNamedType("null")]);
        var eventVisibility = IrToTsVisibilityMapper.Map(evt.Visibility);
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
            BuildDelegateAccessor(name, handlerParam, "delegateAdd", eventVisibility),
            BuildDelegateAccessor(name, handlerParam, "delegateRemove", eventVisibility),
        ];
    }

    private static TsMethodMember BuildDelegateAccessor(
        string eventName,
        TsParameter handlerParam,
        string runtimeHelper,
        TsMemberVisibility visibility
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
            Accessibility: visibility.Accessibility,
            JSDoc: visibility.JSDoc
        );
    }

    /// <summary>
    /// Lowers an <see cref="IrFieldDeclaration"/>. Mirrors C#'s
    /// zero-initialization story for instance + static fields: a field
    /// without an explicit initializer reads as the type's default at
    /// runtime (<c>0</c>, <c>false</c>, <c>null</c>, …), so the bridge
    /// fills in the matching default to preserve semantics. Fields whose
    /// value comes from a captured ctor parameter skip the default —
    /// the assignment lives in the ctor body to avoid a TDZ-like read. A
    /// <c>required</c> field left without an initializer carries the
    /// definite-assignment assertion (<c>name!: T</c>). A <c>readonly</c>
    /// field written by a transpiled <c>init</c> accessor drops <c>readonly</c>,
    /// since the setter that accessor lowers to cannot write a read-only field.
    /// Returns <c>null</c> for a <c>private protected</c> field, for the
    /// reason <see cref="BuildProperty"/> gives.
    /// </summary>
    public static TsFieldMember? BuildField(
        IrFieldDeclaration field,
        TsType tsType,
        IrToTsBridgeContext context
    )
    {
        if (field.Visibility is IrVisibility.PrivateProtected)
            return null;

        var name = IrToTsNamingPolicy.ToDeclaredDataMemberName(field.Name, field.Attributes);

        // When the field captures a constructor parameter (DI shape) the
        // assignment moves to the ctor body — emit the field with no
        // initializer here so the value is set exactly once.
        var initializer =
            field.Initializer is not null && !field.IsCapturedByCtor
                ? IrToTsExpressionBridge.Map(field.Initializer, context)
                : null;
        if (!field.IsCapturedByCtor)
            initializer ??= ComputeDefaultInitializer(field.Type);

        var (accessibility, jsdoc) = IrToTsVisibilityMapper.Map(field.Visibility);

        return new TsFieldMember(
            name,
            tsType,
            initializer,
            field.IsReadonly && !field.IsAssignedByInitAccessor,
            Static: field.IsStatic,
            Accessibility: accessibility,
            DefinitelyAssigned: field.IsRequired,
            JSDoc: jsdoc
        );
    }
}
