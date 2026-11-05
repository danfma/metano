using Metano.Annotations;
using Metano.Compiler;
using Metano.Compiler.Analysis;
using Metano.Compiler.Diagnostics;
using Metano.Compiler.Extraction;
using Metano.Compiler.IR;
using Metano.Compiler.Mappings;
using Metano.Compiler.TypeScript.AST;
using Metano.Compiler.TypeScript.Transformation;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// Lowers an ordinary C# record / class / struct into a TypeScript class
/// declaration via the IR. This is the catch-all per-shape emitter —
/// anything that isn't an enum, interface, exception, inline-wrapper, or
/// static module ends up here.
///
/// Responsibilities:
/// <list type="bullet">
///   <item>Resolves the base class (when transpilable) and the primary-constructor params in C# order</item>
///   <item>Builds the TS constructor (single ctor inline; multiple ctors → <see cref="IrToTsConstructorDispatcherBridge"/>) with the modifier <see cref="ConstructorAccessPolicy"/> decides</item>
///   <item>Detects captured primary-ctor params (DI-style) and emits <c>this._field = param</c> assignments</item>
///   <item>Walks fields, properties (auto/computed/getter+setter), ordinary methods (with overloads), and user-defined operators</item>
///   <item>For records: appends <c>equals</c> / <c>hashCode</c> / <c>with</c> via <see cref="IrToTsRecordSynthesisBridge"/> (<c>with</c> copies the instance, so abstract records stay abstract)</item>
///   <item>Collects implemented (transpilable) interfaces and type parameters</item>
/// </list>
///
/// <para>
/// Member emission walks <see cref="IrClassDeclaration.Members"/> directly:
/// fields/properties/events emit in place; operators and methods get
/// deferred so the final layout matches the legacy "operators before
/// methods" ordering. Roslyn is still used to discover explicit
/// constructors and the inherited base type — the IR doesn't yet expose
/// either as a target-agnostic shape — but every per-member lowering
/// flows through the IR.
/// </para>
/// </summary>
public sealed class IrToTsClassEmitter(
    TypeScriptTransformContext context,
    ConstructorAccessFacts constructorAccessFacts
)
{
    private readonly TypeScriptTransformContext _context = context;
    private readonly ConstructorAccessPolicy _constructorAccessPolicy = new(
        constructorAccessFacts,
        context.ReportDiagnostic
    );
    private readonly CatchAllOverloadReporter _catchAllOverloadReporter = new(
        context.ReportDiagnostic
    );

    public void Transform(INamedTypeSymbol type, List<TsTopLevel> statements)
    {
        // The IR class extractor already populates BaseType / Interfaces /
        // TypeParameters with full Transpilable / Kind metadata, so the bridge
        // helpers do all the filtering. Inherited primary-ctor params still
        // need a Roslyn round-trip (the IR constructor on the base type isn't
        // in scope here).
        var ir = IrEmissionNormalizer.Normalize(
            IrClassExtractor.Extract(type, _context.Extraction),
            type
        );

        // A class overriding `Message` keeps its base message in a private
        // field; its `base.Message` reads lower to that field for the whole
        // class emission.
        var keepsBaseMessage = IrToTsExceptionConstructionBridge.OverridesMessage(ir);

        using var exceptionClassScope = IrToTsExceptionConstructionBridge.EnterClass(
            keepsBaseMessage
        );

        var backingFields = PerClassBackingFields.For(type);
        var lowering = new IrToTsBridgeContext(
            _context.DeclarativeMappings,
            backingFields.ScopeFor(ir),
            BclOverrides
        );

        var extendsType = IrToTsClassBridge.BuildExtends(ir);

        var ctorParams = IrToTsClassBridge.BuildPrimaryCtorParams(
            ir.Constructor,
            ip => IrToTsTypeMapper.MapParameterType(ip.Parameter, BclOverrides),
            lowering
        );

        var explicitCtors = IrConstructorExtractor.AllExplicitConstructors(type);
        var emitsDispatcher = explicitCtors.Count > 1;
        var promotedMemberDeclaration = PromotedMemberDeclarationOf(
            ir.Constructor,
            emitsDispatcher
        );

        var constructorAccessibility = _constructorAccessPolicy.ResolveAndReport(type, ir);

        var constructor = BuildClassConstructor(
            type,
            ir,
            ctorParams,
            emitsDispatcher,
            constructorAccessibility,
            extendsType,
            keepsBaseMessage,
            lowering
        ) with
        {
            Accessibility = constructorAccessibility,
        };

        var classMembers = new List<TsClassMember>();

        if (keepsBaseMessage)
            classMembers.Add(IrToTsExceptionConstructionBridge.BaseMessageFieldDeclaration());

        EmitPromotedMemberFields(ir.Constructor, promotedMemberDeclaration, classMembers);

        EmitClassMembers(
            ir,
            PromotedNamesDeclaredByConstructor(ir.Constructor, promotedMemberDeclaration),
            classMembers,
            keepsBaseMessage,
            lowering
        );

        EmitDeferredOperatorsAndMethods(type, ir, classMembers, lowering);
        EmitSynthesizedMembers(type, ir, explicitCtors, classMembers, lowering);

        statements.Add(
            new TsClass(
                _context.ResolveTsName(type),
                constructor,
                classMembers,
                Extends: extendsType,
                Implements: IrToTsClassBridge.BuildImplements(ir),
                TypeParameters: IrToTsClassBridge.BuildTypeParameters(ir),
                Abstract: ir.Semantics.IsAbstract
            )
        );
    }

    /// <summary>
    /// Two-branch dispatcher for the constructor body:
    /// <list type="number">
    ///   <item>Multi-constructor → IR-driven overload dispatcher;</item>
    ///   <item>Otherwise → <c>BuildSimpleConstructor</c> with the primary
    ///     constructor's parameters in C# declaration order (promoted ones
    ///     as parameter properties, the rest as plain parameters).</item>
    /// </list>
    /// <paramref name="groupAccessibility"/> is the accessibility the caller
    /// gives the constructor; the dispatcher tags its overload signatures
    /// against it.
    /// </summary>
    private TsConstructor BuildClassConstructor(
        INamedTypeSymbol type,
        IrClassDeclaration ir,
        IReadOnlyList<TsConstructorParam> ctorParams,
        bool emitsDispatcher,
        TsAccessibility groupAccessibility,
        TsType? extendsType,
        bool keepsBaseMessage,
        IrToTsBridgeContext lowering
    )
    {
        if (emitsDispatcher)
        {
            if (ir.Constructor is { } constructor)
            {
                _catchAllOverloadReporter.ReportConstructors(
                    type,
                    constructor,
                    new TypeCheckScope(ir.TypeParameters ?? [], BclOverrides)
                );
            }

            // The IR pipeline owns multi-constructor dispatch. When the body
            // coverage probe flags any overload as uncovered, surface a
            // build-time diagnostic and emit an empty constructor stub —
            // crashing here would abort the whole transpile for an otherwise
            // valid type, and silently dropping the type would hide the gap.
            return TryBuildConstructorDispatcherFromIr(
                    ir,
                    new DispatcherBaseCall(extendsType is not null, keepsBaseMessage),
                    groupAccessibility,
                    lowering
                )
                ?? EmitUnsupportedConstructor(
                    type,
                    $"Constructor overload group on '{type.Name}' contains constructs that the IR "
                        + "pipeline does not model yet; an empty constructor was emitted."
                );
        }

        return IrToTsClassBridge.BuildSimpleConstructor(
            ir.Constructor,
            ctorParams,
            ResolveBaseCall(extendsType, ir, keepsBaseMessage, lowering),
            lowering
        );
    }

    /// <summary>
    /// Walks <see cref="IrClassDeclaration.Members"/> and emits fields,
    /// properties (except the <paramref name="promotedNames"/> the
    /// constructor declares), and
    /// events into <paramref name="classMembers"/>. Operators and
    /// methods are deferred — <see cref="EmitDeferredOperatorsAndMethods"/>
    /// runs them after this pass so the final layout keeps the
    /// "operators before methods" ordering the legacy emitter
    /// established.
    /// </summary>
    private void EmitClassMembers(
        IrClassDeclaration ir,
        IReadOnlySet<string> promotedNames,
        List<TsClassMember> classMembers,
        bool keepsBaseMessage,
        IrToTsBridgeContext lowering
    )
    {
        foreach (var irMember in ir.Members ?? [])
        {
            switch (irMember)
            {
                case IrFieldDeclaration irField:
                    classMembers.Add(
                        IrToTsClassBridge.BuildField(
                            irField,
                            IrToTsTypeMapper.Map(irField.Type, BclOverrides),
                            lowering
                        )
                    );
                    break;

                case IrPropertyDeclaration irProp
                    when !promotedNames.Contains(
                        IrToTsNamingPolicy.ToDeclaredDataMemberName(irProp.Name, irProp.Attributes)
                    ):
                    var propertyMembers = IrToTsClassBridge.BuildProperty(
                        irProp,
                        IrToTsTypeMapper.Map(irProp.Type, BclOverrides),
                        lowering
                    );

                    classMembers.AddRange(
                        keepsBaseMessage
                            ? IrToTsExceptionConstructionBridge.MarkMessageOverride(
                                irProp,
                                propertyMembers
                            )
                            : propertyMembers
                    );

                    break;

                case IrEventDeclaration irEvent:
                    classMembers.AddRange(
                        IrToTsClassBridge.BuildEvent(
                            irEvent,
                            IrToTsTypeMapper.Map(irEvent.HandlerType, BclOverrides)
                        )
                    );
                    break;
            }
        }
    }

    /// <summary>
    /// Second-phase walk that emits operators (before methods) and
    /// methods. Split from <see cref="EmitClassMembers"/> so the final
    /// member ordering matches the legacy "operators-then-methods"
    /// layout — the in-place emission of fields/properties/events
    /// already happened.
    /// </summary>
    private void EmitDeferredOperatorsAndMethods(
        INamedTypeSymbol type,
        IrClassDeclaration ir,
        List<TsClassMember> classMembers,
        IrToTsBridgeContext lowering
    )
    {
        var deferredOperators = new List<IrMethodDeclaration>();
        var deferredMethods = new List<IrMethodDeclaration>();
        foreach (var irMember in ir.Members ?? [])
        {
            switch (irMember)
            {
                case IrMethodDeclaration irMethod when irMethod.Semantics.IsOperator:
                    deferredOperators.Add(irMethod);
                    break;
                case IrMethodDeclaration irMethod:
                    deferredMethods.Add(irMethod);
                    break;
            }
        }

        foreach (var op in deferredOperators)
            classMembers.AddRange(EmitOperator(type, ir, op));

        foreach (var m in deferredMethods)
        {
            var members = EmitMethod(type, ir, m, lowering);
            if (members is not null)
                classMembers.AddRange(members);
        }
    }

    /// <summary>
    /// Adds the compiler-synthesised members the source did not
    /// declare: the <c>create({...})</c> factory paired with an
    /// <c>[ObjectArgs]</c> ctor, plus <c>equals</c> / <c>hashCode</c> /
    /// <c>with</c> for non-<c>[PlainObject]</c> records via
    /// <see cref="IrToTsRecordSynthesisBridge"/>, and the
    /// <c>[Symbol.iterator]()</c> / <c>next()</c> members of an
    /// <c>IEnumerable&lt;T&gt;</c> / <c>IEnumerator&lt;T&gt;</c> implementation via
    /// <see cref="IrToTsIterationProtocolBridge"/>.
    /// </summary>
    private void EmitSynthesizedMembers(
        INamedTypeSymbol type,
        IrClassDeclaration ir,
        IReadOnlyList<IMethodSymbol> explicitCtors,
        List<TsClassMember> classMembers,
        IrToTsBridgeContext lowering
    )
    {
        var objectArgsFactory = TryBuildObjectArgsCreateFactory(type, ir, explicitCtors, lowering);

        if (objectArgsFactory is not null)
            classMembers.Add(objectArgsFactory);

        if (
            PerClassBackingFields.BuildToJson(ir, IrToTsClassBridge.BuildExtends(ir) is not null) is
            { } toJson
        )
            classMembers.Add(toJson);

        if (IrToTsRecordSynthesisBridge.ShouldSynthesize(ir))
            classMembers.AddRange(IrToTsRecordSynthesisBridge.Generate(ir));

        classMembers.AddRange(IrToTsIterationProtocolBridge.BuildMembers(ir, BclOverrides));

        IteratorProtocolDiagnostics.ReportConflictingNext(type, ir, _context.ReportDiagnostic);
    }

    /// <summary>
    /// When the type's single public constructor carries <c>[ObjectArgs]</c>,
    /// builds the static <c>create({...})</c> factory so call sites collapse
    /// to the object-literal shape while the runtime constructor stays
    /// positional. Surfaces a diagnostic and returns <c>null</c> when the
    /// attribute mixes with constructor overloading, which Stage 3 does not
    /// yet model.
    /// </summary>
    private TsMethodMember? TryBuildObjectArgsCreateFactory(
        INamedTypeSymbol type,
        IrClassDeclaration ir,
        IReadOnlyList<IMethodSymbol> explicitCtors,
        IrToTsBridgeContext lowering
    )
    {
        var ctorsWithObjectArgs = explicitCtors.Where(SymbolHelper.HasObjectArgs).ToList();
        if (ctorsWithObjectArgs.Count == 0)
            return null;

        if (explicitCtors.Count > 1)
        {
            _context.ReportUnsupportedBody(
                type,
                $"Constructor on '{type.Name}' combines [ObjectArgs] with overloading, "
                    + "which the transpiler does not yet support; the create({...}) "
                    + "factory was not emitted."
            );
            return null;
        }

        return IrToTsClassBridge.BuildObjectArgsCreateFactory(
            ir.Constructor,
            _context.ResolveTsName(type),
            IrToTsClassBridge.BuildTypeParameters(ir),
            lowering
        );
    }

    /// <summary>
    /// Declares as class fields the promoted members the constructor cannot
    /// declare as parameter properties. With parameter properties, only a
    /// promoted <c>params T[]</c>: TypeScript forbids the modifier on a rest
    /// parameter (<c>readonly ...x: T[]</c>), so the signature keeps the rest
    /// form and the field keeps the member on the public surface (#152). With
    /// <see cref="PromotedMemberDeclaration.Fields"/>, every promoted member:
    /// a dispatcher's <c>(...args)</c> declares no parameter properties, and
    /// its branches assign the fields.
    /// </summary>
    private void EmitPromotedMemberFields(
        IrConstructorDeclaration? ctorIr,
        PromotedMemberDeclaration promotedMemberDeclaration,
        List<TsClassMember> classMembers
    )
    {
        var declaredAsFields = (ctorIr?.Parameters ?? []).Where(p =>
            IsDeclaredAsField(p, promotedMemberDeclaration)
        );

        classMembers.AddRange(declaredAsFields.Select(BuildPromotedMemberField));
    }

    private static bool IsDeclaredAsField(
        IrConstructorParameter p,
        PromotedMemberDeclaration promotedMemberDeclaration
    ) =>
        promotedMemberDeclaration switch
        {
            PromotedMemberDeclaration.ParameterProperties => IsPromotedParamsParameter(p),
            PromotedMemberDeclaration.Fields => p.Promotion is not IrParameterPromotion.None,
            _ => false,
        };

    /// <summary>
    /// The promoted members the constructor declares, which the member walk
    /// skips: none when the members keep their own declarations.
    /// </summary>
    private static IReadOnlySet<string> PromotedNamesDeclaredByConstructor(
        IrConstructorDeclaration? ctorIr,
        PromotedMemberDeclaration promotedMemberDeclaration
    ) =>
        promotedMemberDeclaration is PromotedMemberDeclaration.OwnDeclarations
            ? new HashSet<string>(StringComparer.Ordinal)
            : IrToTsClassBridge.PromotedMemberNames(ctorIr);

    /// <summary>
    /// How the members a constructor promotes are declared: as parameter
    /// properties of a single constructor; as fields when a dispatcher runs a
    /// record positional or C# 12 primary constructor, which every other
    /// constructor chains to (its branch assigns them, and a member that
    /// merely reads the parameter must not be emitted as a self-reading
    /// getter); or by the members' own declarations when a dispatcher runs
    /// explicit constructors whose bodies assign them.
    /// </summary>
    private static PromotedMemberDeclaration PromotedMemberDeclarationOf(
        IrConstructorDeclaration? ctorIr,
        bool emitsDispatcher
    )
    {
        var chainsToPrimary = ctorIr?.IsPrimary is true;

        return (emitsDispatcher, chainsToPrimary) switch
        {
            (false, _) => PromotedMemberDeclaration.ParameterProperties,
            (true, true) => PromotedMemberDeclaration.Fields,
            (true, false) => PromotedMemberDeclaration.OwnDeclarations,
        };
    }

    private static bool IsPromotedParamsParameter(IrConstructorParameter p) =>
        p.Parameter.IsParams && p.Promotion is not IrParameterPromotion.None;

    private TsFieldMember BuildPromotedMemberField(IrConstructorParameter p)
    {
        var (accessibility, jsdoc) = IrToTsClassBridge.PromotedMemberVisibility(p);

        return new TsFieldMember(
            IrToTsNamingPolicy.ToDataMemberName(p.Parameter.Name, p.EmittedName),
            // The field keeps the declared `T[] | null` (`with { Items = null }` is
            // valid C#); only the rest parameter drops it (MapParameterType).
            IrToTsTypeMapper.Map(p.Parameter.Type, BclOverrides),
            Initializer: null,
            Readonly: p.Promotion is IrParameterPromotion.ReadonlyProperty,
            Static: false,
            Accessibility: accessibility,
            JSDoc: jsdoc
        );
    }

    // ─── Method / operator emission ───────────────────────────

    /// <summary>
    /// Lowers a single method (or an overload group folded onto
    /// <see cref="IrMethodDeclaration.Overloads"/>) into TS class members.
    /// Returns <c>null</c> when the overload-dispatcher path can't lower the
    /// group — the caller already surfaced the diagnostic.
    /// </summary>
    private IReadOnlyList<TsClassMember>? EmitMethod(
        INamedTypeSymbol type,
        IrClassDeclaration ir,
        IrMethodDeclaration irMethod,
        IrToTsBridgeContext lowering
    )
    {
        if (irMethod.Body is { } body)
            foreach (var stmt in body)
                ReportUnsupportedInBody(stmt, type);

        if (irMethod.Overloads is { Count: > 0 } overloads)
        {
            if (
                IrToTsObjectArgsBridge.HasObjectArgs(irMethod.Attributes)
                || overloads.Any(o => IrToTsObjectArgsBridge.HasObjectArgs(o.Attributes))
            )
            {
                _context.ReportUnsupportedBody(
                    type,
                    $"Method '{type.Name}.{irMethod.Name}' combines [ObjectArgs] with "
                        + "overloading, which the transpiler does not yet support; "
                        + "the overload group was skipped."
                );
                return null;
            }

            _catchAllOverloadReporter.ReportMethods(
                type,
                irMethod,
                new TypeCheckScope(ir.TypeParameters ?? [], BclOverrides)
            );

            var dispatcher = TryBuildOverloadDispatcherFromIr(irMethod, type.Name, ir, lowering);
            if (dispatcher is null)
            {
                _context.ReportUnsupportedBody(
                    type,
                    $"Overload group '{type.Name}.{irMethod.Name}' contains constructs that "
                        + "the IR pipeline does not model yet; the methods were skipped."
                );
                return null;
            }
            return dispatcher;
        }

        var (parameters, returnType, typeParameters) = LowerMethodSignature(irMethod, ir, lowering);

        if (
            !irMethod.Semantics.IsAbstract
            && IrToTsObjectArgsBridge.HasObjectArgs(irMethod.Attributes)
        )
        {
            return
            [
                IrToTsClassBridge.BuildObjectArgsMethod(
                    irMethod,
                    returnType,
                    typeParameters,
                    lowering
                ),
            ];
        }

        return
        [
            IrToTsClassBridge.BuildMethod(
                irMethod,
                parameters,
                returnType,
                typeParameters,
                lowering
            ),
        ];
    }

    /// <summary>
    /// Lowers a user-defined operator (single or overload-folded) to its TS
    /// helper methods, deriving the emitted name from the operator's
    /// <c>[Name]</c> override or <see cref="IrMethodSemantics.OperatorKind"/>.
    /// </summary>
    private IReadOnlyList<TsClassMember> EmitOperator(
        INamedTypeSymbol type,
        IrClassDeclaration ir,
        IrMethodDeclaration irOp
    )
    {
        var opName =
            IrToTsNamingPolicy.FindNameOverride(irOp.Attributes)
            ?? (
                irOp.Semantics.OperatorKind is { } kind
                    ? IrToTsClassBridge.MapOperatorKindToName(kind)
                    : null
            );
        if (opName is null)
            return [];

        var typeName = _context.ResolveTsName(type);

        if (irOp.Overloads is { Count: > 0 } siblings)
        {
            var allOps = new[] { irOp with { Overloads = null } }.Concat(siblings).ToList();
            var perParams = allOps
                .Select<IrMethodDeclaration, IReadOnlyList<TsParameter>>(o =>
                    o.Parameters.Select(p => new TsParameter(
                            IrToTsNamingPolicy.ToParameterName(p.Name),
                            IrToTsTypeMapper.MapParameterType(p, BclOverrides),
                            Rest: p.IsParams
                        ))
                        .ToList()
                )
                .ToList();
            var perReturns = allOps
                .Select(o => IrToTsTypeMapper.Map(o.ReturnType, BclOverrides))
                .ToList();
            var perParamIrTypes = allOps
                .Select<IrMethodDeclaration, IReadOnlyList<IrTypeRef>>(o =>
                    o.Parameters.Select(p => p.Type).ToList()
                )
                .ToList();

            return IrToTsClassBridge.BuildOperatorDispatcher(
                allOps,
                typeName,
                opName,
                perParams,
                perReturns,
                perParamIrTypes,
                new TypeCheckScope(ir.TypeParameters ?? [], BclOverrides),
                _context.DeclarativeMappings
            );
        }

        var parameters = irOp
            .Parameters.Select(p => new TsParameter(
                IrToTsNamingPolicy.ToParameterName(p.Name),
                IrToTsTypeMapper.MapParameterType(p, BclOverrides),
                Rest: p.IsParams
            ))
            .ToList();
        var returnType = IrToTsTypeMapper.Map(irOp.ReturnType, BclOverrides);
        return IrToTsClassBridge.BuildOperator(
            irOp,
            typeName,
            opName,
            parameters,
            returnType,
            _context.DeclarativeMappings
        );
    }

    /// <summary>
    /// Lowers an IR method's signature (parameters, return type, type
    /// parameters) into TS shapes, applying <c>[ExportFromBcl]</c> overrides
    /// via <see cref="BclOverrides"/>. The IR's
    /// <see cref="IrMethodDeclaration.ReturnType"/> already carries the
    /// generator-aware lowering for iterator blocks (the extractor folds
    /// <c>IEnumerable&lt;T&gt;</c> into <see cref="IrGeneratorTypeRef"/>),
    /// so no separate generator branch is needed here.
    /// <para>
    /// TypeScript forbids static members from referencing the enclosing
    /// class's type parameters (<c>OperationResult&lt;T&gt;.ok(value: T)</c>
    /// would error with TS2302), so when the method has no own type
    /// parameters but is static and uses any of the class's type parameters,
    /// we promote the referenced ones onto the method itself — matching what
    /// the legacy <c>TypeTransformer.ExtractMethodTypeParameters</c> path did.
    /// </para>
    /// </summary>
    private (
        IReadOnlyList<TsParameter> Parameters,
        TsType ReturnType,
        IReadOnlyList<TsTypeParameter> TypeParameters
    ) LowerMethodSignature(
        IrMethodDeclaration irMethod,
        IrClassDeclaration owner,
        IrToTsBridgeContext lowering
    )
    {
        // TypeScript forbids parameter initializers in declaration-
        // only positions (abstract / overload / interface signatures).
        // For abstract methods drop the default expression but keep
        // the parameter optional so callers may still omit it.
        var emitDefaults = !irMethod.Semantics.IsAbstract;
        var parameters = irMethod
            .Parameters.Select(p => new TsParameter(
                IrToTsNamingPolicy.ToParameterName(p.Name),
                IrToTsTypeMapper.MapParameterType(p, BclOverrides),
                Optional: p.IsOptional || (!emitDefaults && p.HasDefaultValue),
                DefaultValue: emitDefaults ? LowerDefaultValue(p, lowering) : null,
                Rest: p.IsParams
            ))
            .ToList();
        var returnType = IrToTsTypeMapper.Map(irMethod.ReturnType, BclOverrides);

        var sourceTypeParams = irMethod.TypeParameters is { Count: > 0 }
            ? irMethod.TypeParameters
            : PromoteClassTypeParamsForStatic(irMethod, owner);

        var typeParameters = (sourceTypeParams ?? [])
            .Select(tp => new TsTypeParameter(
                tp.Name,
                tp.Constraints is { Count: > 0 } cs
                    ? IrToTsTypeMapper.Map(cs[0], BclOverrides)
                    : null
            ))
            .ToList();
        return (parameters, returnType, typeParameters);
    }

    /// <summary>
    /// When a static method on a generic class (e.g.,
    /// <c>OperationResult&lt;T&gt;.Ok(T value)</c>) references the class's
    /// type parameters in its signature, returns the subset that's actually
    /// referenced so the bridge can declare them on the method itself.
    /// Returns <c>null</c> for non-static methods or when the class has no
    /// type parameters.
    /// </summary>
    private static IReadOnlyList<IrTypeParameter>? PromoteClassTypeParamsForStatic(
        IrMethodDeclaration method,
        IrClassDeclaration owner
    )
    {
        if (!method.IsStatic || owner.TypeParameters is not { Count: > 0 } classTps)
            return null;

        var referenced = new HashSet<string>(StringComparer.Ordinal);
        CollectTypeParamRefs(method.ReturnType, referenced);
        foreach (var p in method.Parameters)
            CollectTypeParamRefs(p.Type, referenced);

        var promoted = classTps.Where(tp => referenced.Contains(tp.Name)).ToList();
        return promoted.Count > 0 ? promoted : null;
    }

    private static void CollectTypeParamRefs(IrTypeRef type, HashSet<string> acc)
    {
        switch (type)
        {
            case IrTypeParameterRef tp:
                acc.Add(tp.Name);
                break;
            case IrNamedTypeRef named when named.TypeArguments is { Count: > 0 } args:
                foreach (var a in args)
                    CollectTypeParamRefs(a, acc);
                break;
            case IrArrayTypeRef arr:
                CollectTypeParamRefs(arr.ElementType, acc);
                break;
            case IrNullableTypeRef nullable:
                CollectTypeParamRefs(nullable.Inner, acc);
                break;
            case IrTupleTypeRef tup:
                foreach (var e in tup.Elements)
                    CollectTypeParamRefs(e, acc);
                break;
            case IIrCompositeTypeRef composite:
                foreach (var component in composite.ComponentTypes)
                    CollectTypeParamRefs(component, acc);
                break;
            case IrFunctionTypeRef fn:
                CollectTypeParamRefs(fn.ReturnType, acc);
                if (fn.ThisType is { } thisType)
                    CollectTypeParamRefs(thisType, acc);
                foreach (var p in fn.Parameters)
                    CollectTypeParamRefs(p.Type, acc);
                break;
            case IrMapTypeRef map:
                CollectTypeParamRefs(map.KeyType, acc);
                CollectTypeParamRefs(map.ValueType, acc);
                break;
            case IrSetTypeRef set:
                CollectTypeParamRefs(set.ElementType, acc);
                break;
            case IrPromiseTypeRef promise:
                CollectTypeParamRefs(promise.ResultType, acc);
                break;
            case IrGeneratorTypeRef gen:
                CollectTypeParamRefs(gen.YieldType, acc);
                break;
            case IrIterableTypeRef it:
                CollectTypeParamRefs(it.ElementType, acc);
                break;
            case IrIteratorTypeRef iterator:
                CollectTypeParamRefs(iterator.ElementType, acc);
                break;
            case IrKeyValuePairTypeRef kvp:
                CollectTypeParamRefs(kvp.KeyType, acc);
                CollectTypeParamRefs(kvp.ValueType, acc);
                break;
            case IrGroupingTypeRef grp:
                CollectTypeParamRefs(grp.KeyType, acc);
                CollectTypeParamRefs(grp.ElementType, acc);
                break;
        }
    }

    /// <summary>
    /// Reports MS0001 against the type and returns an empty
    /// <see cref="TsConstructor"/> stub. Used when the IR pipeline cannot
    /// lower an overloaded constructor — the build still produces valid TS
    /// (the type emits with a no-op ctor) and the user sees the gap as a
    /// build-time diagnostic instead of a compiler crash.
    /// </summary>
    private TsConstructor EmitUnsupportedConstructor(INamedTypeSymbol type, string message)
    {
        _context.ReportUnsupportedBody(type, message);
        return new TsConstructor([], []);
    }

    /// <summary>
    /// Renders a multi-constructor class through
    /// <see cref="IrToTsConstructorDispatcherBridge"/>. Returns <c>null</c>
    /// when the IR pipeline cannot lower the constructor group (no overloads,
    /// or any body fails <see cref="IrBodyCoverageProbe"/>); the caller emits
    /// an unsupported stub and surfaces the gap as a diagnostic. A record
    /// positional or C# 12 primary constructor has no body of its own (only
    /// moved initializers) and is covered: its branch assigns the promoted and
    /// captured parameters.
    /// </summary>
    private TsConstructor? TryBuildConstructorDispatcherFromIr(
        IrClassDeclaration owner,
        DispatcherBaseCall baseCall,
        TsAccessibility groupAccessibility,
        IrToTsBridgeContext lowering
    )
    {
        var primary = owner.Constructor;

        if (!_context.UseIrBodiesWhenCovered)
            return null;
        if (primary is null || primary.Overloads is not { Count: > 0 })
            return null;

        foreach (var ctor in new[] { primary }.Concat(primary.Overloads))
        {
            if (ctor.Body is { } body && !IrBodyCoverageProbe.IsFullyCovered(body))
                return null;
        }

        return IrToTsConstructorDispatcherBridge.Build(
            primary,
            baseCall,
            owner.TypeParameters ?? [],
            lowering,
            TsConstructorModifier.SignatureJSDocSelectorFor(owner, groupAccessibility)
        );
    }

    /// <summary>
    /// Renders a method overload group through
    /// <see cref="IrToTsOverloadDispatcherBridge"/>. Returns <c>null</c> when
    /// the body coverage probe rejects any overload — the caller surfaces
    /// the gap as a diagnostic and skips the group.
    /// </summary>
    private IReadOnlyList<TsClassMember>? TryBuildOverloadDispatcherFromIr(
        IrMethodDeclaration primary,
        string typeName,
        IrClassDeclaration owner,
        IrToTsBridgeContext lowering
    )
    {
        if (!_context.UseIrBodiesWhenCovered)
            return null;
        if (primary.Overloads is not { Count: > 0 })
            return null;

        foreach (var m in new[] { primary }.Concat(primary.Overloads))
        {
            if (m.Body is null || !IrBodyCoverageProbe.IsFullyCovered(m.Body))
                return null;
        }

        return IrToTsOverloadDispatcherBridge.BuildMethod(
            primary,
            typeName,
            owner.TypeParameters ?? [],
            lowering
        );
    }

    /// <summary>
    /// Reports MS0001 (UnsupportedFeature) when the IR statement extractor
    /// produced an <see cref="IrUnsupportedStatement"/> for a method body —
    /// the bridge would emit a "Not implemented" stub for the same node, so
    /// surfacing the diagnostic alongside keeps the build noisy about gaps.
    /// The IR doesn't carry per-statement source locations, so the diagnostic
    /// points at the containing type — granular enough for the user to find
    /// the offending member. Nested statements are reported too, so a block
    /// (a lowered <c>checked { }</c>, a branch, a loop body) never hides one.
    /// </summary>
    private void ReportUnsupportedInBody(IrStatement stmt, INamedTypeSymbol type)
    {
        if (stmt is IrUnsupportedStatement u)
        {
            _context.ReportDiagnostic(
                new MetanoDiagnostic(
                    MetanoDiagnosticSeverity.Warning,
                    DiagnosticCodes.UnsupportedFeature,
                    $"Statement '{u.Kind}' is not supported by the transpiler.",
                    type.Locations.FirstOrDefault()
                )
            );
        }

        foreach (var nested in IrNodeChildren.StatementsOf(stmt))
            ReportUnsupportedInBody(nested, type);
    }

    // ─── Constructor helpers ────────────────────────────────────

    private BclExportTypeOverrides BclOverrides => _context.BclOverrides;

    /// <summary>
    /// Lowers an <see cref="IrParameter"/>'s default-value expression
    /// into the TS form, or returns <c>null</c> when the parameter
    /// has no explicit default. Routes through the standard
    /// expression bridge so BCL-mapped initializers (decimal
    /// literals, enum members, etc.) get their target form.
    /// </summary>
    private TsExpression? LowerDefaultValue(IrParameter parameter, IrToTsBridgeContext lowering) =>
        parameter.HasDefaultValue && parameter.DefaultValue is not null
            ? IrToTsExpressionBridge.Map(parameter.DefaultValue, lowering)
            : null;

    /// <summary>
    /// Resolves the <c>super(...)</c> argument list for the bridge. Returns
    /// <c>null</c> when there's no transpilable base — the bridge then skips
    /// the super call entirely. Otherwise lowers the IR-extracted base
    /// initializer arguments (<c>record Foo(int X) : Bar(X)</c> or an
    /// explicit <c>: base(...)</c>), which reference the plain constructor
    /// parameters by name. Without a base initializer C# invokes the base's
    /// parameterless constructor, so the result is an empty list
    /// (<c>super()</c>).
    /// </summary>
    private TsBaseConstructorCall? ResolveBaseCall(
        TsType? extendsType,
        IrClassDeclaration ir,
        bool keepsBaseMessage,
        IrToTsBridgeContext lowering
    )
    {
        if (extendsType is null)
            return null;

        var baseCall = ir.Constructor is { } ctor
            ? IrToTsClassBridge.BuildBaseCall(ctor, keepsBaseMessage, lowering)
            : null;

        return baseCall ?? new TsBaseConstructorCall([], [], keepsBaseMessage);
    }

    /// <summary>How the members a constructor promotes are declared.</summary>
    private enum PromotedMemberDeclaration
    {
        ParameterProperties,
        Fields,
        OwnDeclarations,
    }
}
