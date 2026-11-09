using System.Diagnostics;
using Metano.Compiler.IR;
using Metano.Compiler.Mappings;
using Metano.Compiler.TypeScript.AST;
using Metano.Compiler.TypeScript.Transformation;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// Lowers <see cref="IrModuleFunction"/>s into top-level TypeScript
/// <see cref="TsFunctionDeclaration"/>s — the shape a
/// <c>[ExportedAsModule]</c> static class emits (plain functions instead of
/// a class full of statics) — and also renders classic C# extension
/// methods, which Roslyn already exposes as ordinary methods whose first
/// parameter is the receiver. C# 14 <c>extension(R r)</c> blocks and
/// classic-style extension properties also flow through this bridge: the
/// extractor folds them into the same <see cref="IrModuleFunction"/> shape
/// (with the receiver as the first parameter). <c>[ModuleEntryPoint]</c>
/// bodies are emitted separately as <see cref="TsTopLevelStatement"/>s by
/// the caller in <c>TypeTransformer.TryEmitModuleViaIr</c>.
/// </summary>
public static class IrToTsModuleBridge
{
    /// <summary>
    /// Appends a <see cref="TsFunctionDeclaration"/> per
    /// <see cref="IrModuleFunction"/> to <paramref name="statements"/>.
    /// Bodies are routed through <see cref="IrToTsStatementBridge"/> so BCL
    /// mappings and the implicit-<c>this</c> synthesis the extractor already
    /// applied for instance members carry through.
    /// </summary>
    public static void Convert(
        IReadOnlyList<IrModuleFunction> functions,
        List<TsTopLevel> statements,
        DeclarativeMappingRegistry? bclRegistry = null
    )
    {
        foreach (var fn in functions)
            statements.Add(ConvertFunction(fn, bclRegistry));
    }

    /// <summary>
    /// Appends one top-level variable per module field or auto-property of a
    /// containerless static class (see <see cref="Metano.Compiler.Extraction.IrModuleVariableExtractor"/>):
    /// <c>export const max: number = 10;</c>. Call sites read these members as
    /// bare identifiers, so the declaration uses the same data-member name.
    /// <list type="bullet">
    ///   <item>A <c>readonly</c> / <c>const</c> field or a get-only / init-only
    ///     property is a <c>const</c>; anything writable is a <c>let</c>.</item>
    ///   <item>A member without an initializer starts at the type's C# default
    ///     (<c>0</c>, <c>false</c>, <c>null</c>, …); a reference type with no
    ///     literal default is a bare <c>let</c>.</item>
    ///   <item>A <c>private</c> member stays module-local; any other visibility
    ///     is exported, since another file of the assembly may read it.</item>
    /// </list>
    /// Computed properties are not variables; the caller reports them before
    /// calling this.
    /// </summary>
    public static void ConvertVariables(
        IReadOnlyList<IrMemberDeclaration> variables,
        List<TsTopLevel> statements,
        IrToTsTypeOverrides? typeOverrides = null,
        DeclarativeMappingRegistry? bclRegistry = null
    )
    {
        foreach (var variable in variables)
        {
            var declaration = variable switch
            {
                IrFieldDeclaration field => ConvertVariable(
                    field,
                    field.Type,
                    field.Initializer,
                    field.IsReadonly,
                    typeOverrides,
                    bclRegistry
                ),
                IrPropertyDeclaration property => ConvertVariable(
                    property,
                    property.Type,
                    property.Initializer,
                    property.Accessors
                        is IrPropertyAccessors.GetOnly
                            or IrPropertyAccessors.GetInit,
                    typeOverrides,
                    bclRegistry
                ),
                _ => throw new UnreachableException(
                    $"Unexpected module variable {variable.GetType().Name}."
                ),
            };

            statements.Add(new TsTopLevelStatement(declaration));
        }
    }

    private static TsVariableDeclaration ConvertVariable(
        IrMemberDeclaration member,
        IrTypeRef type,
        IrExpression? initializer,
        bool isReadonly,
        IrToTsTypeOverrides? typeOverrides,
        DeclarativeMappingRegistry? bclRegistry
    )
    {
        var value = initializer is not null
            ? IrToTsExpressionBridge.Map(initializer, IrToTsBridgeContext.For(bclRegistry))
            : IrToTsClassBridge.ComputeDefaultInitializer(type);

        return new TsVariableDeclaration(
            IrToTsNamingPolicy.ToDeclaredModuleVariableName(member),
            value,
            // A `const` needs an initializer; a member with no literal default is a `let`.
            Const: isReadonly && value is not null,
            Exported: member.Visibility is not IrVisibility.Private,
            Type: IrToTsTypeMapper.Map(type, typeOverrides),
            JSDoc: IrToTsVisibilityMapper.Map(member.Visibility).JSDoc
        );
    }

    /// <summary>
    /// A dispatched module-function group (spec 013 FR-032): one exported
    /// fast path per overload, named as the barrel's claim pass claimed it,
    /// plus one exported dispatcher carrying the overload signatures and
    /// branching to the fast paths. The old shape for the same C# — two
    /// <c>export function twice</c> — was TS2393 at every import.
    /// <para>
    /// The group plans and dispatches through its own copy of the
    /// dispatcher's branch builder, over <see cref="IrModuleFunction"/>s,
    /// until the deferred <c>TsDispatcherShape</c> unification folds the two
    /// bridges into one.
    /// </para>
    /// </summary>
    public static void ConvertDispatchedGroup(
        IReadOnlyList<IrModuleFunction> overloads,
        TsModuleFunctionClaims claims,
        List<TsTopLevel> statements,
        DeclarativeMappingRegistry? bclRegistry = null
    )
    {
        var plan = PlanModuleFunctionGroup(overloads);

        var dispatcherName =
            claims.ClaimOf(overloads[0].OverloadRef!)?.Dispatcher
            ?? throw new InvalidOperationException(
                $"The barrel claim pass named no dispatcher for '{SignatureOf(overloads[0])}'."
            );

        var fastPathNames = plan
            .Sorted.Select(fn =>
                claims.ClaimOf(fn.OverloadRef!)?.FastPath
                ?? throw new InvalidOperationException(
                    $"The barrel claim pass named no fast path for '{SignatureOf(fn)}'."
                )
            )
            .ToList();

        foreach (var (fn, fastPathName) in plan.Sorted.Zip(fastPathNames))
            statements.Add(ConvertFunction(fn, bclRegistry, fastPathName));

        statements.Add(
            new TsFunction(
                dispatcherName,
                IrToTsOverloadDispatcherBridge.DispatcherParameters(),
                plan.CommonReturn,
                BuildModuleDispatcherBody(plan.Sorted, fastPathNames),
                Async: plan.IsAsync,
                Overloads: plan.Signatures
            )
        );
    }

    /// <summary>The C# signature an overload is named by in an error.</summary>
    private static string SignatureOf(IrModuleFunction function) =>
        function.SignatureText ?? function.Name;

    /// <summary>The group in dispatch order with one signature per overload.</summary>
    private static ModuleFunctionGroupPlan PlanModuleFunctionGroup(
        IReadOnlyList<IrModuleFunction> overloads
    )
    {
        var sorted = OverloadDispatchOrder.Sort(overloads, fn => fn.Parameters);
        var isAsync = sorted.Any(fn => fn.Semantics.IsAsync);

        var signatures = sorted
            .Select(fn => new TsMethodOverload(
                SignatureParametersOf(fn),
                IrToTsTypeMapper.Map(fn.ReturnType),
                IrToTsTypeParameterMapper.Convert(fn.TypeParameters)
            ))
            .ToList();

        return new(sorted, isAsync, signatures, CommonReturnTypeOf(sorted, isAsync));
    }

    /// <summary>
    /// The parameters of one overload signature, where TypeScript forbids an
    /// initializer: a parameter a call may omit is optional instead.
    /// </summary>
    private static List<TsParameter> SignatureParametersOf(IrModuleFunction fn) =>
        fn
            .Parameters.Select(p => new TsParameter(
                IrToTsNamingPolicy.ToParameterName(p.Name),
                IrToTsTypeMapper.MapParameterType(p),
                Optional: p.IsOptional,
                Rest: p.IsParams
            ))
            .ToList();

    /// <summary>
    /// The dispatcher implementation's return type: the overloads' shared one
    /// when they agree and none is generic, else <c>unknown</c> (or a
    /// <c>Promise</c> for an async group), mirroring the class dispatchers.
    /// </summary>
    private static TsType CommonReturnTypeOf(IReadOnlyList<IrModuleFunction> sorted, bool isAsync)
    {
        var returns = sorted.Select(fn => IrToTsTypeMapper.Map(fn.ReturnType)).ToList();
        var isGeneric = sorted.Any(fn => fn.TypeParameters is { Count: > 0 });

        if (!isGeneric && returns.All(returnType => returnType.Equals(returns[0])))
            return returns[0];

        return isAsync ? new TsNamedType("Promise", [new TsNamedType("unknown")]) : new TsAnyType();
    }

    /// <summary>
    /// The dispatcher body over module fast paths: one guarded branch per
    /// overload in dispatch order, each delegating to its fast path, then
    /// the legacy unmatched-arity throw. The module group has no class scope,
    /// so the type checks resolve against the overload's own type parameters.
    /// </summary>
    private static List<TsStatement> BuildModuleDispatcherBody(
        IReadOnlyList<IrModuleFunction> sorted,
        IReadOnlyList<string> fastPathNames
    )
    {
        var body = new List<TsStatement>();

        for (var i = 0; i < sorted.Count; i++)
            body.Add(BranchFor(sorted[i], fastPathNames[i]));

        body.Add(
            IrToTsOverloadDispatcherBridge.NoMatchingOverload(
                IrToTsNamingPolicy.ToFunctionName(sorted[0].Name, sorted[0].Attributes)
            )
        );

        return body;
    }

    /// <summary>One dispatcher branch: the overload's guard delegating to its fast path.</summary>
    private static TsIfStatement BranchFor(IrModuleFunction fn, string fastPathName)
    {
        var argumentShape = new OverloadArgumentShape(fn.Parameters);

        var delegated = DelegatedArguments(fn, argumentShape);
        var delegateCall = new TsCallExpression(new TsIdentifier(fastPathName), delegated);

        var branchStatements = fn.ReturnType is IrPrimitiveTypeRef { Primitive: IrPrimitive.Void }
            ? [new TsExpressionStatement(delegateCall), new TsReturnStatement()]
            : new List<TsStatement> { new TsReturnStatement(delegateCall) };

        return new TsIfStatement(
            argumentShape.Guard(new TypeCheckScope(fn.TypeParameters ?? [], null)),
            branchStatements
        );
    }

    /// <summary>
    /// The fast path's arguments: each <c>args[i]</c> cast to its parameter
    /// type, and the trailing arguments of a <c>params</c> parameter spread
    /// back into its rest parameter.
    /// </summary>
    private static List<TsExpression> DelegatedArguments(
        IrModuleFunction fn,
        OverloadArgumentShape argumentShape
    ) =>
        fn
            .Parameters.Select(
                (parameter, index) =>
                {
                    var value = new TsCastExpression(
                        argumentShape.ValueOf(index),
                        IrToTsTypeMapper.MapParameterType(parameter)
                    );

                    return parameter.IsParams ? new TsSpreadExpression(value) : (TsExpression)value;
                }
            )
            .ToList();

    private sealed record ModuleFunctionGroupPlan(
        IReadOnlyList<IrModuleFunction> Sorted,
        bool IsAsync,
        IReadOnlyList<TsMethodOverload> Signatures,
        TsType CommonReturn
    );

    private static TsFunction ConvertFunction(
        IrModuleFunction fn,
        DeclarativeMappingRegistry? bclRegistry,
        string? emittedName = null
    )
    {
        var name = emittedName ?? IrToTsNamingPolicy.ToFunctionName(fn.Name, fn.Attributes);

        if (IrToTsObjectArgsBridge.HasObjectArgs(fn.Attributes))
            return ConvertObjectArgsFunction(fn, name, bclRegistry);

        var parameters = fn
            .Parameters.Select(p => new TsParameter(
                IrToTsNamingPolicy.ToParameterName(p.Name),
                IrToTsTypeMapper.MapParameterType(p),
                Optional: p.IsOptional,
                DefaultValue: p.HasDefaultValue && p.DefaultValue is not null
                    ? IrToTsExpressionBridge.Map(
                        p.DefaultValue,
                        IrToTsBridgeContext.For(bclRegistry)
                    )
                    : null,
                Rest: p.IsParams
            ))
            .ToList();
        var body = IrToTsBodyHelpers.LowerOrNotImplemented(
            fn.Body,
            fn.Name,
            IrToTsBridgeContext.For(bclRegistry)
        );

        return new TsFunction(
            name,
            parameters,
            IrToTsTypeMapper.Map(fn.ReturnType),
            body,
            Exported: true,
            // Generators can't also be async under this backend; the extractor
            // already forced Async off when IsGenerator is on.
            Async: fn.Semantics.IsAsync,
            Generator: fn.Semantics.IsGenerator,
            TypeParameters: MapTypeParameters(fn.TypeParameters)
        );
    }

    private static TsFunction ConvertObjectArgsFunction(
        IrModuleFunction fn,
        string name,
        DeclarativeMappingRegistry? bclRegistry
    )
    {
        var (argsParam, destructure) = IrToTsObjectArgsBridge.BuildArgsParam(
            fn.Parameters,
            bclRegistry
        );

        var body = new List<TsStatement> { destructure };
        body.AddRange(
            IrToTsBodyHelpers.LowerOrNotImplemented(
                fn.Body,
                fn.Name,
                IrToTsBridgeContext.For(bclRegistry)
            )
        );

        return new TsFunction(
            name,
            [argsParam],
            IrToTsTypeMapper.Map(fn.ReturnType),
            body,
            Exported: true,
            Async: fn.Semantics.IsAsync,
            Generator: fn.Semantics.IsGenerator,
            TypeParameters: MapTypeParameters(fn.TypeParameters)
        );
    }

    private static IReadOnlyList<TsTypeParameter>? MapTypeParameters(
        IReadOnlyList<IrTypeParameter>? typeParameters
    ) => IrToTsTypeParameterMapper.Convert(typeParameters);
}
