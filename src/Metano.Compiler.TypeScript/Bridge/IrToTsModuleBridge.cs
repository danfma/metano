using System.Diagnostics;
using Metano.Compiler.Analysis;
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
    internal static void Convert(
        IReadOnlyList<IrModuleFunction> functions,
        List<TsTopLevel> statements,
        IrToTsBridgeContext context
    )
    {
        foreach (var fn in functions)
            statements.Add(ConvertFunction(fn, context));
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
    internal static void ConvertVariables(
        IReadOnlyList<IrMemberDeclaration> variables,
        List<TsTopLevel> statements,
        IrToTsTypeOverrides? typeOverrides,
        IrToTsBridgeContext context
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
                    context
                ),
                IrPropertyDeclaration property => ConvertVariable(
                    property,
                    property.Type,
                    property.Initializer,
                    property.Accessors
                        is IrPropertyAccessors.GetOnly
                            or IrPropertyAccessors.GetInit,
                    typeOverrides,
                    context
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
        IrToTsBridgeContext context
    )
    {
        var value = initializer is not null
            ? IrToTsExpressionBridge.Map(initializer, context)
            : IrToTsClassBridge.ComputeDefaultInitializer(type);

        return new TsVariableDeclaration(
            IrToTsNamingPolicy.ToDeclaredModuleVariableName(member),
            value,
            // A `const` needs an initializer; a member with no literal default is a `let`.
            Const: isReadonly && value is not null,
            Exported: member.Visibility is not IrVisibility.Private,
            Type: IrToTsTypeMapper.Map(type, typeOverrides),
            JSDoc: IrToTsVisibilityMapper.Map(member.Visibility).JSDoc
        )
        {
            InitializesStaticState = HasUnobservableInitializer(initializer),
        };
    }

    /// <summary>
    /// Whether a bundle may drop this module variable's initializer with the variable:
    /// a C# default (no initializer) or a side-effect-free one. A module variable is
    /// dropped on its own, while C# runs every static initializer of the type once any
    /// member of it is touched, so an initializer with effects stays unannotated.
    /// </summary>
    private static bool HasUnobservableInitializer(IrExpression? initializer) =>
        initializer is null || IrEvaluationTraits.IsSideEffectFree(initializer);

    /// <summary>
    /// A dispatched module-function group (spec 013 FR-032): one exported
    /// fast path per overload, named as the barrel's claim pass claimed it,
    /// plus one exported dispatcher carrying the overload signatures and
    /// branching to the fast paths. The old shape for the same C# — two
    /// <c>export function twice</c> — was TS2393 at every import.
    /// <para>
    /// The group plans through its own copy of the dispatcher's branch loop,
    /// over <see cref="IrModuleFunction"/>s, but delegates its arguments to
    /// the shared <see cref="IrToTsOverloadDispatcherBridge.DelegatedArguments"/>
    /// so both dispatchers cast generic overloads identically, until the
    /// deferred <c>TsDispatcherShape</c> unification folds the two bridges
    /// into one.
    /// </para>
    /// </summary>
    internal static void ConvertDispatchedGroup(
        IReadOnlyList<IrModuleFunction> overloads,
        TsModuleFunctionClaims claims,
        List<TsTopLevel> statements,
        IrToTsBridgeContext context
    )
    {
        var plan = PlanModuleFunctionGroup(overloads);

        var dispatcherName = claims.ClaimOf(overloads[0].OverloadRef!)?.Dispatcher
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
            statements.Add(ConvertFunction(fn, context, fastPathName));

        statements.Add(
            new TsFunction(
                dispatcherName,
                IrToTsOverloadDispatcherBridge.DispatcherParameters(),
                plan.CommonReturn,
                BuildModuleDispatcherBody(plan.Sorted, fastPathNames, context),
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
        IrToTsClassBridge.InsertReifiedParameters(
            fn.Parameters.Select(p => new TsParameter(
                    IrToTsNamingPolicy.ToParameterName(p.Name),
                    IrToTsTypeMapper.MapParameterType(p),
                    Optional: p.IsOptional,
                    Rest: p.IsRestParams
                ))
                .ToList(),
            fn.ReifiedTypeParameters,
            asOptionalSignature: true
        );

    /// <summary>
    /// The dispatcher implementation's return type, by the class
    /// dispatchers' rule: the overloads' shared one when they agree and none
    /// is generic, else the erased one.
    /// </summary>
    private static TsType CommonReturnTypeOf(IReadOnlyList<IrModuleFunction> sorted, bool isAsync)
    {
        var returns = sorted.Select(fn => IrToTsTypeMapper.Map(fn.ReturnType)).ToList();
        var isGeneric = sorted.Any(fn => fn.TypeParameters is { Count: > 0 });

        return IrToTsOverloadDispatcherBridge.CommonReturnTypeOf(returns, isGeneric, isAsync);
    }

    /// <summary>
    /// The dispatcher body over module fast paths: one guarded branch per
    /// overload in dispatch order, each delegating to its fast path, then
    /// the legacy unmatched-arity throw. The module group has no class scope,
    /// so the type checks resolve against the overload's own type parameters.
    /// </summary>
    private static List<TsStatement> BuildModuleDispatcherBody(
        IReadOnlyList<IrModuleFunction> sorted,
        IReadOnlyList<string> fastPathNames,
        IrToTsBridgeContext context
    )
    {
        var body = new List<TsStatement>();

        for (var i = 0; i < sorted.Count; i++)
            body.Add(BranchFor(sorted[i], fastPathNames[i], context));

        body.Add(
            IrToTsOverloadDispatcherBridge.NoMatchingOverload(
                IrToTsNamingPolicy.ToFunctionName(sorted[0])
            )
        );

        return body;
    }

    /// <summary>One dispatcher branch: the overload's guard delegating to its fast path.</summary>
    private static TsIfStatement BranchFor(
        IrModuleFunction fn,
        string fastPathName,
        IrToTsBridgeContext context
    )
    {
        var argumentShape = new OverloadArgumentShape(fn.Parameters, fn.ReifiedTypeParameters);

        var delegated = IrToTsOverloadDispatcherBridge.DelegatedArguments(
            fn.Parameters,
            fn.TypeParameters,
            argumentShape,
            typeOverrides: null,
            fn.ReifiedTypeParameters
        );

        var delegateCall = new TsCallExpression(new TsIdentifier(fastPathName), delegated);

        var branchStatements = fn.ReturnType is IrPrimitiveTypeRef { Primitive: IrPrimitive.Void }
            ? [new TsExpressionStatement(delegateCall), new TsReturnStatement()]
            : new List<TsStatement> { new TsReturnStatement(delegateCall) };

        return new TsIfStatement(
            argumentShape.Guard(
                new TypeCheckScope(fn.TypeParameters ?? [], null)
                {
                    RuntimeTypes = context.RuntimeTypes,
                }
            ),
            branchStatements
        );
    }

    private sealed record ModuleFunctionGroupPlan(
        IReadOnlyList<IrModuleFunction> Sorted,
        bool IsAsync,
        IReadOnlyList<TsMethodOverload> Signatures,
        TsType CommonReturn
    );

    private static TsFunction ConvertFunction(
        IrModuleFunction fn,
        IrToTsBridgeContext context,
        string? emittedName = null
    )
    {
        var name = emittedName ?? IrToTsNamingPolicy.ToFunctionName(fn);

        if (IrToTsObjectArgsBridge.HasObjectArgs(fn.Attributes))
            return ConvertObjectArgsFunction(fn, name, context);

        var parameters = fn
            .Parameters.Select(p => new TsParameter(
                IrToTsNamingPolicy.ToParameterName(p.Name),
                IrToTsTypeMapper.MapParameterType(p),
                Optional: p.IsOptional,
                DefaultValue: p.HasDefaultValue && p.DefaultValue is not null
                    ? IrToTsExpressionBridge.Map(p.DefaultValue, context)
                    : null,
                Rest: p.IsRestParams
            ))
            .ToList();

        var allParameters = IrToTsClassBridge.InsertReifiedParameters(
            parameters,
            fn.ReifiedTypeParameters
        );

        var body = IrToTsBodyHelpers.LowerOrNotImplemented(fn.Body, fn.Name, context);

        return new TsFunction(
            name,
            allParameters,
            IrToTsTypeMapper.Map(fn.ReturnType),
            body,
            Exported: IsExported(fn),
            // Generators can't also be async under this backend; the extractor
            // already forced Async off when IsGenerator is on.
            Async: fn.Semantics.IsAsync,
            Generator: fn.Semantics.IsGenerator,
            TypeParameters: MapTypeParameters(fn.TypeParameters),
            JSDoc: IrToTsVisibilityMapper.Map(fn.Visibility).JSDoc
        );
    }

    private static TsFunction ConvertObjectArgsFunction(
        IrModuleFunction fn,
        string name,
        IrToTsBridgeContext context
    )
    {
        var (argsParam, destructure) = IrToTsObjectArgsBridge.BuildArgsParam(
            fn.Parameters,
            context.Bcl
        );

        var body = new List<TsStatement> { destructure };
        body.AddRange(IrToTsBodyHelpers.LowerOrNotImplemented(fn.Body, fn.Name, context));

        return new TsFunction(
            name,
            [argsParam],
            IrToTsTypeMapper.Map(fn.ReturnType),
            body,
            Exported: IsExported(fn),
            Async: fn.Semantics.IsAsync,
            Generator: fn.Semantics.IsGenerator,
            TypeParameters: MapTypeParameters(fn.TypeParameters),
            JSDoc: IrToTsVisibilityMapper.Map(fn.Visibility).JSDoc
        );
    }

    /// <summary>
    /// Whether a module function is exported: a private helper stays local to its
    /// module, as a private module variable does.
    /// </summary>
    private static bool IsExported(IrModuleFunction fn) =>
        fn.Visibility is not IrVisibility.Private;

    private static IReadOnlyList<TsTypeParameter>? MapTypeParameters(
        IReadOnlyList<IrTypeParameter>? typeParameters
    ) => IrToTsTypeParameterMapper.Convert(typeParameters);
}
