using Metano.Compiler.IR;
using Metano.Transformation;
using Metano.TypeScript.AST;

namespace Metano.TypeScript.Bridge;

/// <summary>
/// Lowers a generic <see cref="IrClassDeclaration"/> (record / class / struct
/// that isn't <c>[PlainObject]</c>, <c>[InlineWrapper]</c>, an exception, or a
/// static module) into a TypeScript <see cref="TsClass"/>.
/// <para>
/// Used as a library of lowering helpers — member emission (fields,
/// properties, methods, events, operators), constructor synthesis,
/// and overload dispatch — called from <see cref="IrToTsClassEmitter"/>,
/// which wires them together into the full <see cref="TsClass"/> shape.
/// </para>
/// </summary>
internal static class IrToTsClassBridge
{
    /// <summary>
    /// Builds the <c>extends</c> clause for an <see cref="IrClassDeclaration"/>.
    /// Returns <c>null</c> when the IR has no base type, the base is the
    /// implicit <c>System.Object</c> / <c>System.ValueType</c> sentinel (which
    /// the extractor leaves unset), or the base is a non-transpilable named
    /// type — TypeScript can't extend something that doesn't have a class
    /// emitted for it.
    /// </summary>
    public static TsType? BuildExtends(IrClassDeclaration ir) =>
        ir.BaseType switch
        {
            null => null,
            IrNamedTypeRef { Semantics.IsTranspilable: false } => null,
            _ => IrToTsTypeMapper.Map(ir.BaseType),
        };

    /// <summary>
    /// Builds the <c>implements</c> list for an <see cref="IrClassDeclaration"/>.
    /// Filters out non-transpilable interfaces — TypeScript can't implement an
    /// interface that doesn't get emitted alongside the class. Returns
    /// <c>null</c> when nothing remains so the printer elides the clause.
    /// </summary>
    public static IReadOnlyList<TsType>? BuildImplements(IrClassDeclaration ir)
    {
        if (ir.Interfaces is not { Count: > 0 } ifaces)
            return null;
        var result = new List<TsType>();
        foreach (var iface in ifaces)
        {
            if (iface is IrNamedTypeRef { Semantics.IsTranspilable: false })
                continue;
            result.Add(IrToTsTypeMapper.Map(iface));
        }
        return result.Count > 0 ? result : null;
    }

    /// <summary>
    /// Lowers IR type parameters into the TS form. Only the first constraint
    /// of each parameter is rendered today — TS only supports a single
    /// <c>extends</c> bound and the legacy transformer made the same choice.
    /// </summary>
    public static IReadOnlyList<TsTypeParameter>? BuildTypeParameters(IrClassDeclaration ir) =>
        ir.TypeParameters is { Count: > 0 } tps
            ? tps.Select(tp => new TsTypeParameter(
                    tp.Name,
                    tp.Constraints is { Count: > 0 } cs ? IrToTsTypeMapper.Map(cs[0]) : null
                ))
                .ToList()
            : null;

    /// <summary>
    /// Computes the implicit <c>default(T)</c> initializer for a field /
    /// property whose declaration omits an explicit one:
    /// <list type="bullet">
    ///   <item>nullable types → <c>null</c></item>
    ///   <item>numeric primitives → <c>0</c></item>
    ///   <item>boolean → <c>false</c></item>
    ///   <item>decimal → <c>new Decimal("0")</c></item>
    ///   <item>enums → first member (numeric: smallest value; string: first
    ///     declared). String enums use the source-name key; numeric enums
    ///     honor a target <c>[Name]</c> override on the member.</item>
    ///   <item>everything else → no implicit initializer (TS leaves
    ///     reference fields <c>undefined</c>, matching nullable C#)</item>
    /// </list>
    /// Returns <c>null</c> when no default applies — the caller emits the
    /// field without an initializer in that case.
    /// </summary>
    public static TsExpression? ComputeDefaultInitializer(IrTypeRef type)
    {
        if (type is IrNullableTypeRef)
            return new TsLiteral("null");

        if (type is IrPrimitiveTypeRef p)
            return p.Primitive switch
            {
                IrPrimitive.Int16
                or IrPrimitive.Int32
                or IrPrimitive.Int64
                or IrPrimitive.Byte
                or IrPrimitive.Float32
                or IrPrimitive.Float64 => new TsLiteral("0"),
                IrPrimitive.Decimal => new TsNewExpression(
                    new TsIdentifier("Decimal"),
                    [new TsStringLiteral("0")]
                ),
                IrPrimitive.Boolean => new TsLiteral("false"),
                _ => null,
            };

        if (
            type is IrNamedTypeRef
            {
                Semantics:
                {
                    Kind: IrNamedTypeKind.NumericEnum or IrNamedTypeKind.StringEnum,
                    EnumDefaultMember: { } defaultMember,
                } enumSemantics,
            } namedEnum
        )
        {
            // String enums key the runtime object on the source-cased name —
            // a [Name(target, ...)] rename would produce an invalid property
            // access (`MyEnum.in-progress`). Numeric enums honor the override.
            var memberName =
                enumSemantics.Kind == IrNamedTypeKind.StringEnum
                    ? defaultMember.Name
                    : defaultMember.EmittedName ?? defaultMember.Name;
            return new TsPropertyAccess(new TsIdentifier(namedEnum.Name), memberName);
        }

        return null;
    }

    /// <summary>
    /// Maps an IR member visibility to the TS accessibility keyword the
    /// printer renders. <c>Internal</c> / <c>ProtectedInternal</c> /
    /// <c>PrivateProtected</c> all collapse to <c>public</c> in TS — the
    /// language has no narrower-than-public sibling-of-private band.
    /// </summary>
    public static TsAccessibility MapAccessibility(IrVisibility visibility) =>
        visibility switch
        {
            IrVisibility.Private => TsAccessibility.Private,
            IrVisibility.Protected or IrVisibility.ProtectedInternal => TsAccessibility.Protected,
            _ => TsAccessibility.Public,
        };

    /// <summary>
    /// Lowers an <see cref="IrFieldDeclaration"/> into a <see cref="TsFieldMember"/>.
    /// Returns <c>null</c> when the field's accessibility is below TS-visible
    /// (internal / not-applicable) — those don't surface on the emitted class.
    /// <paramref name="capturedParamNames"/> carries the camelCase names of
    /// constructor parameters that DI-style assignments redirect to: when the
    /// field's initializer is a bare identifier referencing one of those
    /// names, the initializer is dropped because the assignment moves to the
    /// constructor body.
    /// </summary>
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
        if (prop.Visibility is IrVisibility.Internal or IrVisibility.PrivateProtected)
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
        var body = IrToTsBodyHelpers.LowerOrNotImplemented(method.Body, method.Name, bclRegistry);
        return new TsMethodMember(
            name,
            parameters,
            returnType,
            body,
            Static: method.IsStatic,
            Async: method.Semantics.IsAsync,
            Generator: method.Semantics.IsGenerator,
            Accessibility: MapAccessibility(method.Visibility),
            TypeParameters: typeParameters
        );
    }

    /// <summary>
    /// Lowers a single user-defined operator into a static method
    /// (<c>__add</c> / <c>__negate</c>) plus a thin instance helper
    /// (<c>$add(other)</c> / <c>$negate()</c>) that delegates to it.
    /// Multi-operator dispatch (two overloads sharing the same name) is
    /// handled by <see cref="BuildOperatorDispatcher"/>.
    /// </summary>
    public static IReadOnlyList<TsClassMember> BuildOperator(
        IrMethodDeclaration method,
        string containingTypeName,
        string operatorName,
        IReadOnlyList<TsParameter> parameters,
        TsType returnType,
        DeclarativeMappingRegistry? bclRegistry
    )
    {
        var body = IrToTsBodyHelpers.LowerOrNotImplemented(method.Body, method.Name, bclRegistry);

        var staticName = $"__{operatorName}";
        var isUnary = method.Parameters.Count == 1;
        var typeRef = new TsIdentifier(containingTypeName);

        TsStatement helperBody;
        IReadOnlyList<TsParameter> helperParams;
        if (isUnary)
        {
            helperParams = [];
            helperBody = new TsReturnStatement(
                new TsCallExpression(
                    new TsPropertyAccess(typeRef, staticName),
                    [new TsIdentifier("this")]
                )
            );
        }
        else
        {
            var rightParam = parameters[^1];
            helperParams = [rightParam];
            helperBody = new TsReturnStatement(
                new TsCallExpression(
                    new TsPropertyAccess(typeRef, staticName),
                    [new TsIdentifier("this"), new TsIdentifier(rightParam.Name)]
                )
            );
        }

        return
        [
            new TsMethodMember(staticName, parameters, returnType, body, Static: true),
            new TsMethodMember($"${operatorName}", helperParams, returnType, [helperBody]),
        ];
    }

    /// <summary>
    /// Lowers an overloaded operator group (two or more <c>operator +</c>
    /// methods sharing a derived name) into a static dispatcher
    /// (<c>__add(...args: unknown[])</c>) plus one private fast-path method
    /// per overload, plus a single instance helper (<c>$add(...args)</c>)
    /// that delegates to the static side with <c>this</c> as the first
    /// argument. Per-overload parameter lists, return types, and type-checks
    /// come pre-resolved from the caller because the legacy
    /// <see cref="TypeMapper"/> still owns <c>[ExportFromBcl]</c> overrides
    /// and <see cref="IrTypeCheckBuilder"/> consumes <see cref="IrTypeRef"/>
    /// directly.
    /// </summary>
    public static IReadOnlyList<TsClassMember> BuildOperatorDispatcher(
        IReadOnlyList<IrMethodDeclaration> overloads,
        string containingTypeName,
        string operatorName,
        IReadOnlyList<IReadOnlyList<TsParameter>> overloadParameters,
        IReadOnlyList<TsType> overloadReturnTypes,
        IReadOnlyList<IReadOnlyList<IrTypeRef>> overloadParamIrTypes,
        DeclarativeMappingRegistry? bclRegistry
    )
    {
        var staticName = $"__{operatorName}";
        var className = new TsIdentifier(containingTypeName);
        var members = new List<TsClassMember>();

        // Sort indices by descending arity to match the legacy "most-specific
        // first" dispatch order. The three input lists are kept parallel so
        // the index works on all of them.
        var indices = Enumerable
            .Range(0, overloads.Count)
            .OrderByDescending(i => overloads[i].Parameters.Count)
            .ToList();
        var sharedReturnType = overloadReturnTypes[indices[0]];

        var staticOverloadSigs = indices
            .Select(i => new TsMethodOverload(overloadParameters[i], overloadReturnTypes[i]))
            .ToList();

        // Fast-path naming follows the same convention as
        // IrToTsOverloadDispatcherBridge: <staticName><CapitalizedParamType>...
        var fastPathNames = indices
            .Select(i =>
                staticName
                + string.Concat(overloadParamIrTypes[i].Select(t => Capitalize(SimpleTypeName(t))))
            )
            .ToList();

        // Fast-path private static methods — one per overload, real body via
        // IrToTsStatementBridge.
        for (var k = 0; k < indices.Count; k++)
        {
            var i = indices[k];
            var body = IrToTsBodyHelpers.LowerOrNotImplemented(
                overloads[i].Body,
                overloads[i].Name,
                bclRegistry
            );
            members.Add(
                new TsMethodMember(
                    fastPathNames[k],
                    overloadParameters[i],
                    overloadReturnTypes[i],
                    body,
                    Static: true,
                    Accessibility: TsAccessibility.Private
                )
            );
        }

        // Static dispatcher body: per-overload `if (args.length === N && isT(args[i]) …)`
        // chain, throwing a runtime error when no branch matches.
        var staticDispatchBody = BuildDispatcherBranches(
            indices,
            overloadParamIrTypes,
            overloadParameters,
            fastPathNames,
            className,
            operatorName,
            paramOffset: 0,
            includeThis: false
        );

        members.Add(
            new TsMethodMember(
                staticName,
                [new TsParameter("...args", new TsNamedType("unknown[]"))],
                sharedReturnType,
                staticDispatchBody,
                Static: true,
                Overloads: staticOverloadSigs
            )
        );

        // Instance helper signatures: drop the first parameter (it becomes
        // `this`); the dispatch ignores arity index 0 too.
        var instanceOverloadSigs = indices
            .Select(i => new TsMethodOverload(
                overloadParameters[i].Skip(1).ToList(),
                overloadReturnTypes[i]
            ))
            .ToList();

        var instanceDispatchBody = BuildDispatcherBranches(
            indices,
            overloadParamIrTypes,
            overloadParameters,
            fastPathNames,
            className,
            operatorName,
            paramOffset: 1,
            includeThis: true
        );

        members.Add(
            new TsMethodMember(
                $"${operatorName}",
                [new TsParameter("...args", new TsNamedType("unknown[]"))],
                sharedReturnType,
                instanceDispatchBody,
                Overloads: instanceOverloadSigs
            )
        );

        return members;
    }

    /// <summary>
    /// Builds the per-branch dispatcher body shared by the static and
    /// instance helpers. <paramref name="paramOffset"/> = 0 dispatches over
    /// every overload parameter (static side); <paramref name="paramOffset"/>
    /// = 1 skips the receiver (instance side) and uses <c>this</c> as the
    /// leading call argument when <paramref name="includeThis"/> is set.
    /// </summary>
    private static List<TsStatement> BuildDispatcherBranches(
        IReadOnlyList<int> indices,
        IReadOnlyList<IReadOnlyList<IrTypeRef>> overloadParamIrTypes,
        IReadOnlyList<IReadOnlyList<TsParameter>> overloadParameters,
        IReadOnlyList<string> fastPathNames,
        TsIdentifier className,
        string operatorName,
        int paramOffset,
        bool includeThis
    )
    {
        var body = new List<TsStatement>();
        for (var k = 0; k < indices.Count; k++)
        {
            var i = indices[k];
            var paramTypes = overloadParamIrTypes[i];
            var argCount = paramTypes.Count - paramOffset;

            TsExpression condition = new TsBinaryExpression(
                new TsPropertyAccess(new TsIdentifier("args"), "length"),
                "===",
                new TsLiteral(argCount.ToString())
            );
            for (var j = 0; j < argCount; j++)
            {
                var check = IrTypeCheckBuilder.GenerateForParam(paramTypes[j + paramOffset], j);
                condition = new TsBinaryExpression(condition, "&&", check);
            }

            var callArgs = new List<TsExpression>();
            if (includeThis)
                callArgs.Add(new TsIdentifier("this"));
            for (var j = 0; j < argCount; j++)
            {
                // TsParameter.Type is nullable on the AST side because some
                // bridges (extension blocks, etc.) elide the annotation; in
                // the dispatcher we always have a real type so the fallback
                // is just to keep the compiler honest.
                var paramType = overloadParameters[i][j + paramOffset].Type ?? new TsAnyType();
                callArgs.Add(new TsCastExpression(new TsIdentifier($"args[{j}]"), paramType));
            }

            var delegateCall = new TsCallExpression(
                new TsPropertyAccess(className, fastPathNames[k]),
                callArgs
            );
            body.Add(new TsIfStatement(condition, [new TsReturnStatement(delegateCall)]));
        }

        body.Add(
            new TsThrowStatement(
                new TsNewExpression(
                    new TsIdentifier("Error"),
                    [new TsStringLiteral($"No matching overload for {operatorName}")]
                )
            )
        );
        return body;
    }

    private static string Capitalize(string s) =>
        string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s[1..];

    /// <summary>
    /// Short per-type tag the operator dispatcher uses in its fast-path
    /// helper names (<c>__add$Int$Int</c>, <c>__divide$Money$Decimal</c>, …),
    /// so each overload gets a distinct dispatch target.
    /// </summary>
    private static string SimpleTypeName(IrTypeRef type) =>
        type switch
        {
            IrPrimitiveTypeRef p => p.Primitive switch
            {
                IrPrimitive.Int32 => "Int",
                IrPrimitive.Int64 => "Long",
                IrPrimitive.String => "String",
                IrPrimitive.Boolean => "Bool",
                IrPrimitive.Float64 => "Double",
                IrPrimitive.Float32 => "Float",
                IrPrimitive.Decimal => "Decimal",
                _ => p.Primitive.ToString(),
            },
            IrNamedTypeRef n => n.Name,
            _ => "Unknown",
        };

    /// <summary>
    /// Maps the IR's canonical operator name (<c>"Addition"</c>,
    /// <c>"Equality"</c>, …) to the conventional TypeScript helper name
    /// (<c>"add"</c>, <c>"equals"</c>). Returns <c>null</c> for unsupported
    /// operator kinds. Unary forms (<c>"UnaryNegation"</c>, …) are folded
    /// onto the same canonical name as their binary form when there's no
    /// natural distinction at call site.
    /// </summary>
    public static string? MapOperatorKindToName(string kind) =>
        kind switch
        {
            "Addition" => "add",
            "Subtraction" => "subtract",
            "Multiply" => "multiply",
            "Division" => "divide",
            "Modulus" => "modulo",
            "Equality" => "equals",
            "Inequality" => "notEquals",
            "LessThan" => "lessThan",
            "GreaterThan" => "greaterThan",
            "LessThanOrEqual" => "lessThanOrEqual",
            "GreaterThanOrEqual" => "greaterThanOrEqual",
            "LogicalNot" => "not",
            "OnesComplement" => "bitwiseNot",
            "BitwiseAnd" => "bitwiseAnd",
            "BitwiseOr" => "bitwiseOr",
            "ExclusiveOr" => "xor",
            "LeftShift" => "shiftLeft",
            "RightShift" => "shiftRight",
            // Unary +/- collapse to the same canonical name as their binary
            // form so the call-site lowering for `-x` and `x - y` agree on
            // the helper name (matches the legacy single-token mapping).
            "UnaryNegation" => "subtract",
            "UnaryPlus" => "add",
            _ => null,
        };

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
            var isReadonly = p.Promotion is IrParameterPromotion.ReadonlyProperty;
            var accessibility = MapAccessibility(p.PromotedVisibility ?? IrVisibility.Public);
            var defaultValue = ResolveCtorParamDefault(p, bclRegistry);
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
                new TsConstructorParam(name, resolveType(p), Accessibility: TsAccessibility.None)
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
        if (superArgs is not null && superArgs.Count > 0)
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

            // Explicit ctor body statements (a non-record ctor with body
            // beyond captured-param assignments) — append after the
            // synthesized super + captured assignments so the resulting body
            // matches the source order.
            if (ir.Body is { Count: > 0 } explicitBody)
                body.AddRange(IrToTsStatementBridge.MapBody(explicitBody, bclRegistry));
        }

        return new TsConstructor(tsCtorParams, body);
    }

    public static TsFieldMember? BuildField(
        IrFieldDeclaration field,
        TsType tsType,
        DeclarativeMappingRegistry? bclRegistry
    )
    {
        if (field.Visibility is IrVisibility.Internal or IrVisibility.PrivateProtected)
            return null;

        var name = IrToTsNamingPolicy.ToInterfaceMemberName(field.Name, field.Attributes);

        // When the field captures a constructor parameter (DI shape) the
        // assignment moves to the ctor body — emit the field with no
        // initializer here so the value is set exactly once.
        var initializer =
            field.Initializer is not null && !field.IsCapturedByCtor
                ? IrToTsExpressionBridge.Map(field.Initializer, bclRegistry)
                : null;
        // Static fields with an explicit initializer are the canonical
        // shape (`static readonly Zero = new Counter(0)`); the
        // default-initializer fallback only makes sense for instance
        // fields that the constructor would otherwise leave undefined.
        if (!field.IsStatic)
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
