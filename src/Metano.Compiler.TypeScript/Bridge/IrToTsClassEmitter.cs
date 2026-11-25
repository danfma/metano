using Metano.Annotations;
using Metano.Compiler;
using Metano.Compiler.Analysis;
using Metano.Compiler.Diagnostics;
using Metano.Compiler.Extraction;
using Metano.Compiler.IR;
using Metano.Compiler.Mappings;
using Metano.Compiler.TypeScript.Analysis;
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
internal sealed class IrToTsClassEmitter(
    TypeScriptTransformContext context,
    ConstructorAccessFacts constructorAccessFacts,
    TsOverloadClaimsRegistry overloadClaims,
    IrToTsBridgeContext groupLowering
)
{
    private readonly TypeScriptTransformContext _context = context;

    /// <summary>
    /// The lowering context of the file group the class belongs to: the
    /// static operators and conversions lower through it, and the class's own
    /// context derives from it.
    /// </summary>
    private readonly IrToTsBridgeContext _groupLowering = groupLowering;
    private readonly TsOverloadClaimsRegistry _overloadClaims = overloadClaims;
    private readonly ConstructorAccessPolicy _constructorAccessPolicy = new(
        constructorAccessFacts,
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
            _context.NestedTypes.ModuleLevelName(type)
        );

        // A class overriding `Message` keeps its base message in a private
        // field; its `base.Message` reads lower to that field for the whole
        // class emission.
        var keepsBaseMessage = IrToTsExceptionConstructionBridge.OverridesMessage(ir);

        var typeName = _context.ResolveTsName(type);

        var closedSelf = IrToTsClosedFamilyBridge.SelfOf(
            type,
            ir,
            typeName,
            _context.ClosedFamilies
        );

        var backingFields = PerClassBackingFields.For(type);
        var lowering = _groupLowering with
        {
            ClassFields = backingFields.ScopeFor(ir),
            TypeOverrides = BclOverrides,
            KeepsBaseMessage = keepsBaseMessage,
            ClosedSelf = closedSelf,
        };

        var extendsType = IrToTsClassBridge.BuildExtends(ir);
        var claims = ClaimAndReport(type, ir);

        var hasSlot = ir.TypeArgumentSlot is { } typeArgSlot && ir.TypeParameters is { Count: > 0 };
        var classTypeParamNames = hasSlot ? ir.TypeParameters!.Select(tp => tp.Name).ToList() : [];
        var ctorLowering = hasSlot
            ? lowering.WithTypeParameters(TsTypeParameterScope.ForCtorParam(classTypeParamNames))
            : lowering;
        var instanceLowering = hasSlot
            ? lowering.WithTypeParameters(
                TsTypeParameterScope.ForClassSlot(ir.TypeArgumentSlot!, classTypeParamNames)
            )
            : lowering;

        var explicitCtors = IrConstructorExtractor.AllExplicitConstructors(type);
        var emitsDispatcher = IrConstructorExtractor.IsDispatchedSet(type);

        var ctorParams = IrToTsClassBridge.BuildPrimaryCtorParams(
            ir.Constructor,
            ip => IrToTsTypeMapper.MapParameterType(ip.Parameter, BclOverrides),
            ctorLowering
        );

        if (hasSlot && !emitsDispatcher)
            ctorParams = IrToTsClassBridge.WithTypeArgsParam(ctorParams, ir.TypeParameters!);

        var promotedMemberDeclaration = ir
            is { Semantics.IsValueType: true, StaticInitializer: not null }
            ? PromotedMemberDeclaration.OwnDeclarations
            : PromotedMemberDeclarationOf(ir.Constructor, emitsDispatcher);

        var constructorAccessibility = _constructorAccessPolicy.ResolveAndReport(type, ir);

        var implicitParameterlessBody = emitsDispatcher
            ? IrToTsStructDefaultConstructorBridge.BuildBody(ir, type, _context.ReportDiagnostic)
            : null;

        var constructor = BuildClassConstructor(
            type,
            ir,
            ctorParams,
            emitsDispatcher,
            new ConstructorSelectors(typeName, claims),
            implicitParameterlessBody,
            constructorAccessibility,
            extendsType,
            keepsBaseMessage,
            ctorLowering
        ) with
        {
            Accessibility = constructorAccessibility,
        };

        var emitsSelectorsAndFactories = emitsDispatcher && constructor.Overloads is { Count: > 0 };
        var classMembers = new List<TsClassMember>();
        var userMembers = new HashSet<TsClassMember>(ReferenceEqualityComparer.Instance);

        var typeInitializer = TypeInitializerLowering.For(
            new TypeInitializerSelection(type, _context.Compilation),
            ir,
            TypeInitializerTargetOf(
                type,
                typeName,
                extendsType,
                ImplicitDefaultOf(
                    ir,
                    explicitCtors,
                    emitsSelectorsAndFactories && implicitParameterlessBody is not null
                ),
                ir
            ),
            ctorLowering
        );

        if (emitsSelectorsAndFactories)
        {
            classMembers.AddRange(
                IrToTsConstructorFactoryBridge.BuildSelectors(ir.Constructor!, claims)
            );
        }

        classMembers.AddRange(IrToTsExceptionIdentityBridge.Members(type, ir));

        if (IrToTsExceptionIdentityBridge.UnmappedBaseDiagnostic(type, ir) is { } unmappedBase)
            _context.ReportDiagnostic(unmappedBase);

        if (keepsBaseMessage)
        {
            classMembers.Add(IrToTsExceptionConstructionBridge.BaseMessageFieldDeclaration());
        }

        EmitPromotedMemberFields(ir.Constructor, promotedMemberDeclaration, classMembers);

        var dataMembersStart = classMembers.Count;

        var ctorFieldInitializers = new List<TsStatement>();

        IndexerDiagnostics.Report(type, ir, _context.ReportDiagnostic);

        EmitClassMembers(
            ir,
            PromotedNamesDeclaredByConstructor(ir.Constructor, promotedMemberDeclaration),
            classMembers,
            keepsBaseMessage,
            ctorLowering,
            typeInitializer,
            hasSlot ? ctorFieldInitializers : null
        );

        if (ctorFieldInitializers.Count > 0)
        {
            var insertAt = constructor.Body.Count;

            for (var i = 0; i < constructor.Body.Count; i++)
            {
                if (
                    constructor.Body[i] is TsExpressionStatement
                    {
                        Expression: TsCallExpression
                        {
                            Callee: TsPropertyAccess
                            {
                                Object: TsIdentifier { Name: "Object" },
                                Property: "defineProperty",
                            },
                        },
                    }
                )
                {
                    insertAt = i + 1;
                    break;
                }
            }

            var updatedBody = new List<TsStatement>(
                constructor.Body.Count + ctorFieldInitializers.Count
            );
            updatedBody.AddRange(constructor.Body.Take(insertAt));
            updatedBody.AddRange(ctorFieldInitializers);
            updatedBody.AddRange(constructor.Body.Skip(insertAt));
            constructor = constructor with { Body = updatedBody };
        }

        MarkInheritedOverrides(classMembers, dataMembersStart, claims);
        userMembers.UnionWith(classMembers.Skip(dataMembersStart));

        if (emitsSelectorsAndFactories)
        {
            classMembers.AddRange(
                IrToTsConstructorFactoryBridge.BuildFactories(
                    ir,
                    typeName,
                    claims,
                    _constructorAccessPolicy.ResolveWidenedFactoriesAndReport(type, ir),
                    lowering
                )
            );
        }

        var methodsStart = classMembers.Count;

        EmitDeferredOperatorsAndMethods(type, ir, claims, classMembers, instanceLowering);
        userMembers.UnionWith(classMembers.Skip(methodsStart));
        EmitSynthesizedMembers(type, ir, explicitCtors, classMembers, instanceLowering);

        if (
            IrToTsClosedFamilyBridge.KindMember(type, closedSelf, _context.ClosedFamilies) is
            { } kindMember
        )
        {
            classMembers.Insert(0, kindMember);
        }

        if (
            ir.Semantics.IsValueType
            && (hasSlot || _context.RuntimeTypes.Reaches(RuntimeTypeReachability.KeyOf(type)))
        )
        {
            classMembers.Add(BuildStructZeroFactory(type, ir, typeName));
        }

        if (
            hasSlot
            && !ReportTypeArgumentSlotCollisionIfAny(type, ir.TypeArgumentSlot!, classMembers)
        )
        {
            classMembers.Insert(
                0,
                IrToTsClassBridge.BuildSlotDeclaration(ir.TypeArgumentSlot!, ir.TypeParameters!)
            );
        }

        var body = typeInitializer.Complete(
            new TsClassBody(constructor, classMembers),
            userMembers
        );

        ReportSlotNameClashes(type, typeInitializer.SlotNameClashes());

        statements.Add(
            new TsClass(
                closedSelf?.RuntimeClassName ?? typeName,
                body.Constructor,
                body.Members,
                Exported: closedSelf is null,
                Extends: extendsType,
                Implements: IrToTsClassBridge.BuildImplements(ir),
                TypeParameters: IrToTsClassBridge.BuildTypeParameters(ir),
                Abstract: ir.Semantics.IsAbstract
            )
        );

        if (closedSelf is not null)
        {
            statements.AddRange(
                IrToTsClosedFamilyBridge.FamilyDeclarations(type, ir, closedSelf, BclOverrides)
            );
        }
    }

    /// <summary>
    /// Marks <c>override</c> on the user fields, accessors and methods from
    /// <paramref name="start"/> whose name an emitted base already declares on
    /// the same side (<see cref="WithInheritedOverride"/>). Runs before the
    /// members join the user-member set, which compares references.
    /// </summary>
    private static void MarkInheritedOverrides(
        List<TsClassMember> classMembers,
        int start,
        TsOverloadClaims claims
    )
    {
        for (var i = start; i < classMembers.Count; i++)
            classMembers[i] = WithInheritedOverride(classMembers[i], claims);
    }

    /// <summary>
    /// <paramref name="member"/> marked <c>override</c> when an emitted base
    /// already declares its name on the same side, so a package compiled with
    /// <c>noImplicitOverride</c> accepts it (TS4114/TS4116) whatever its
    /// accessibility (#759). The members of an overload group are not passed
    /// here: the claim pass decides which of them override.
    /// </summary>
    private static TsClassMember WithInheritedOverride(
        TsClassMember member,
        TsOverloadClaims claims
    ) =>
        member switch
        {
            TsMethodMember { Override: false } method
                when claims.InheritsMember(method.Name, method.Static) => method with
            {
                Override = true,
            },
            TsGetterMember { Override: false } getter
                when claims.InheritsMember(getter.Name, getter.Static) => getter with
            {
                Override = true,
            },
            TsSetterMember { Override: false } setter
                when claims.InheritsMember(setter.Name, setter.Static) => setter with
            {
                Override = true,
            },
            TsFieldMember { Override: false, Accessibility: not TsAccessibility.Private } field
                when claims.InheritsMember(field.Name, field.Static) => field with
            {
                Override = true,
            },
            _ => member,
        };

    /// <summary>
    /// The class's claim results (spec 013 FR-004), reporting their
    /// diagnostics and the interfaces the class makes multiply constructed
    /// (<c>MS0069</c>, FR-036): the class's own emission is the one place that
    /// does. None for a class with no overload group and no emitted base.
    /// </summary>
    private TsOverloadClaims ClaimAndReport(INamedTypeSymbol type, IrClassDeclaration ir)
    {
        foreach (
            var diagnostic in MultiplyConstructedInterfaces.DiagnosticsOf(
                type,
                _context.Extraction.Target
            )
        )
            _context.ReportDiagnostic(diagnostic);

        if (!TsOverloadClaimsRegistry.AppliesTo(ir))
            return TsOverloadClaims.None;

        var claims = _overloadClaims.For(type);

        claims.Report(type, _context.ReportDiagnostic);

        return claims;
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
        ConstructorSelectors selectors,
        IReadOnlyList<TsStatement>? implicitParameterlessBody,
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
                _context.AmbiguousDispatch.ReportConstructors(
                    type,
                    constructor,
                    new TypeCheckScope(ir.TypeParameters ?? [], BclOverrides)
                    {
                        RuntimeTypes = RuntimeTypeReachabilityResult.Unchecked,
                    }
                );
            }

            // The IR pipeline owns multi-constructor dispatch. When the body
            // coverage probe flags any overload as uncovered, surface a
            // build-time diagnostic and emit an empty constructor stub —
            // crashing here would abort the whole transpile for an otherwise
            // valid type, and silently dropping the type would hide the gap.
            return TryBuildConstructorDispatcherFromIr(
                ir,
                new DispatcherBaseCall(extendsType, keepsBaseMessage)
                {
                    CollectionBase = ir.BaseType as IrCollectionTypeRef,
                },
                selectors,
                implicitParameterlessBody,
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
            lowering,
            ir.TypeArgumentSlot
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
        IrToTsBridgeContext lowering,
        TypeInitializerLowering typeInitializer,
        List<TsStatement>? ctorFieldInitializers = null
    )
    {
        foreach (var irMember in ir.Members ?? [])
        {
            switch (irMember)
            {
                case IrFieldDeclaration irField:
                    var field = IrToTsClassBridge.BuildField(
                        irField,
                        IrToTsTypeMapper.Map(irField.Type, BclOverrides),
                        lowering
                    );

                    if (
                        ctorFieldInitializers is not null
                        && field.Initializer is { } fieldInit
                        && !field.Static
                        && TypeScript
                            .Printer.RenderExpression(fieldInit)
                            .Contains(
                                IrToTsClassBridge.TypeArgsParameterName,
                                StringComparison.Ordinal
                            )
                    )
                    {
                        ctorFieldInitializers.Add(
                            new TsExpressionStatement(
                                new TsBinaryExpression(
                                    new TsPropertyAccess(new TsIdentifier("this"), field.Name),
                                    "=",
                                    fieldInit
                                )
                            )
                        );
                        field = field with { Initializer = null };
                    }

                    classMembers.AddRange(typeInitializer.LowerField(irField, field));
                    break;

                case IrPropertyDeclaration { Semantics.IsIndexer: true }:
                    break;

                case IrPropertyDeclaration irProp
                    when !promotedNames.Contains(
                        IrToTsNamingPolicy.ToDeclaredDataMemberName(irProp.Name, irProp.Attributes)
                    ):
                    var propertyMembers = typeInitializer.LowerProperty(
                        irProp,
                        IrToTsClassBridge.BuildProperty(
                            irProp,
                            IrToTsTypeMapper.Map(irProp.Type, BclOverrides),
                            lowering
                        )
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
    /// A static member that shares its TypeScript name with another member's
    /// lazily initialized slot is an error (FR-066): the class would declare the
    /// name twice, and neither member may silently replace the other.
    /// </summary>
    private void ReportSlotNameClashes(INamedTypeSymbol type, IReadOnlyList<SlotNameClash> clashes)
    {
        foreach (var clash in clashes)
        {
            var member = type.GetMembers(clash.Member.Name).FirstOrDefault(m => m.IsStatic);

            _context.ReportDiagnostic(
                new MetanoDiagnostic(
                    MetanoDiagnosticSeverity.Error,
                    DiagnosticCodes.UnsupportedFeature,
                    $"The static member '{type.Name}.{clash.Member.Name}' prints as "
                        + $"'{clash.Name}', the name of the lazily initialized static "
                        + $"'{clash.SlotOwner.Name}' (spec 001 FR-066): rename one of them "
                        + "with [Name(\"...\")].",
                    (member ?? type).Locations.FirstOrDefault()
                )
            );
        }
    }

    /// <summary>
    /// Detects collisions with <c>$typeArguments$&lt;TS class name&gt;</c>
    /// (inherited base slot with the same TS name, or a user member with the
    /// same emitted name) and reports <c>MS0001</c> (spec 011 W5-5(a)).
    /// </summary>
    internal bool ReportTypeArgumentSlotCollisionIfAny(
        INamedTypeSymbol type,
        string slotName,
        IReadOnlyList<TsClassMember> members
    )
    {
        var baseCollision = HasInheritedSlotName(type, slotName);
        var memberCollision = members.Any(member => MemberNameOf(member) == slotName);

        if (!baseCollision && !memberCollision)
            return false;

        var reason = baseCollision
            ? $"an ancestor class emits the same slot name '{slotName}'"
            : $"a member of '{type.Name}' is emitted as '{slotName}'";

        _context.ReportDiagnostic(
            new MetanoDiagnostic(
                MetanoDiagnosticSeverity.Error,
                DiagnosticCodes.UnsupportedFeature,
                $"The reified type-argument slot of '{type.ToDisplayString()}' cannot be emitted: "
                    + $"{reason} (spec 011 W5-5).",
                type.Locations.FirstOrDefault()
            )
        );

        return true;
    }

    private bool HasInheritedSlotName(INamedTypeSymbol type, string slotName)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (
                current.IsGenericType
                && IrToTsClassBridge.SlotNameOf(_context.ResolveTsName(current)) == slotName
            )
                return true;
        }

        return false;
    }

    private static string? MemberNameOf(TsClassMember member) =>
        member switch
        {
            TsFieldMember field => field.Name,
            TsGetterMember getter => getter.Name,
            TsSetterMember setter => setter.Name,
            TsMethodMember method => method.Name,
            _ => null,
        };

    /// <summary>
    /// The facts the type initializer lowering needs about the class: its emitted
    /// name, the name a <c>TypeInitializationException</c> reports
    /// (<see cref="ClrTypeName.OfTypeInitializer"/>), whether it extends a class, and, for a
    /// struct, where its constructor builds the implicit default value.
    /// </summary>
    private static TypeInitializerTarget TypeInitializerTargetOf(
        INamedTypeSymbol type,
        string typeName,
        TsType? extendsType,
        ImplicitDefaultConstruction implicitDefault,
        IrClassDeclaration ir
    ) =>
        new(
            typeName,
            ClrTypeName.OfTypeInitializer(type),
            extendsType is not null,
            ir.Semantics.IsValueType,
            implicitDefault
        );

    /// <summary>
    /// Where the emitted constructor builds a struct's implicit default value:
    /// the whole constructor of a struct that declares none, the dispatcher's
    /// leading branch when the dispatcher was built with one
    /// (<paramref name="leadsWithImplicitDefault"/>; the unsupported-constructor
    /// stub has none).
    /// </summary>
    private static ImplicitDefaultConstruction ImplicitDefaultOf(
        IrClassDeclaration ir,
        IReadOnlyList<IMethodSymbol> explicitCtors,
        bool leadsWithImplicitDefault
    )
    {
        if (!ir.Semantics.IsValueType)
            return ImplicitDefaultConstruction.None;

        if (explicitCtors.Count == 0)
            return ImplicitDefaultConstruction.WholeConstructor;

        return leadsWithImplicitDefault
            ? ImplicitDefaultConstruction.LeadingBranch
            : ImplicitDefaultConstruction.None;
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
        TsOverloadClaims claims,
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
            classMembers.AddRange(EmitOperator(type, ir, op, claims));

        foreach (var m in deferredMethods)
        {
            var baseScope = m.IsStatic ? TsTypeParameterScope.Empty : lowering.TypeParameters;
            var methodScope =
                m.ReifiedTypeParameters.Count > 0
                    ? TsTypeParameterScope.FromBindings(
                        m.ReifiedTypeParameters.Select(rp => new TsTypeParameterBinding(
                            rp.Name,
                            TsTypeParameterScopeKind.Method,
                            rp.Position,
                            HiddenParameterName: rp.HiddenParameterName
                        )),
                        baseScope
                    )
                    : baseScope;
            var members = EmitMethod(type, ir, m, claims, lowering.WithTypeParameters(methodScope));
            if (members is not null)
                classMembers.AddRange(members);
        }

        classMembers.AddRange(
            IrToTsOverloadDispatcherBridge.BuildInterfaceForwarders(ir, claims, lowering)
        );
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
        {
            var userMembers = TsClassMemberTable.UserMembersOf(ir);

            classMembers.AddRange(
                IrToTsRecordSynthesisBridge.Generate(
                    ir,
                    userMembers,
                    PerClassBackingFields.For(type).ScopeFor(ir),
                    lowering.ClosedSelf
                )
            );

            IrToTsRecordSynthesisBridge.ReportClaimedEquals(
                type,
                ir,
                userMembers,
                _context.ReportDiagnostic
            );

            classMembers.AddRange(IrToTsRecordPrintingBridge.Generate(ir, userMembers, lowering));

            IrToTsRecordPrintingBridge.ReportUnsupported(
                type,
                ir,
                userMembers,
                _context.ReportDiagnostic
            );
        }

        classMembers.AddRange(IrToTsIterationProtocolBridge.BuildMembers(ir, BclOverrides));

        IteratorProtocolDiagnostics.ReportConflictingNext(type, ir, _context.ReportDiagnostic);

        IteratorProtocolDiagnostics.ReportNextOverridingInheritedStep(
            type,
            ir,
            _context.ReportDiagnostic
        );
    }

    /// <summary>
    /// When the type's single public constructor carries <c>[ObjectArgs]</c>,
    /// builds the static <c>create({...})</c> factory so call sites collapse
    /// to the object-literal shape while the runtime constructor stays
    /// positional. Surfaces a diagnostic and returns <c>null</c> when the
    /// attribute cannot be honoured (<see cref="IrConstructorExtractor.ObjectArgsProblem"/>).
    /// </summary>
    private TsMethodMember? TryBuildObjectArgsCreateFactory(
        INamedTypeSymbol type,
        IrClassDeclaration ir,
        IReadOnlyList<IMethodSymbol> explicitCtors,
        IrToTsBridgeContext lowering
    )
    {
        if (IrConstructorExtractor.ObjectArgsProblem(type) is { } problem)
        {
            _context.ReportUnsupportedBody(type, problem);

            return null;
        }

        if (!explicitCtors.Any(IrConstructorExtractor.HasObjectArgsSurface))
            return null;

        return IrToTsClassBridge.BuildObjectArgsCreateFactory(
            ir.Constructor,
            _context.ResolveTsName(type),
            IrToTsClassBridge.BuildTypeParameters(ir),
            lowering,
            overridesBaseFactory: IrConstructorExtractor.NearestObjectArgsAncestor(type) is not null
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
            PromotedMemberDeclaration.ParameterProperties => IsPromotedParamsParameter(p)
                || IrToTsClassBridge.HasReservedPromotedName(p),
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
        p.Parameter.IsRestParams && p.Promotion is not IrParameterPromotion.None;

    private TsFieldMember BuildPromotedMemberField(IrConstructorParameter p)
    {
        var (accessibility, jsdoc) = IrToTsClassBridge.PromotedMemberVisibility(p);

        return new TsFieldMember(
            IrToTsClassBridge.PromotedName(p),
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
        TsOverloadClaims claims,
        IrToTsBridgeContext lowering
    )
    {
        if (irMethod.Body is { } body)
            foreach (var stmt in body)
                ReportUnsupportedInBody(stmt, type);

        if (irMethod.OverloadName is not null)
        {
            var overloads = irMethod.Overloads ?? [];

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

            _context.AmbiguousDispatch.ReportMethods(
                type,
                irMethod,
                new TypeCheckScope(ir.TypeParameters ?? [], BclOverrides)
                {
                    RuntimeTypes = RuntimeTypeReachabilityResult.Unchecked,
                }
            );

            var dispatcher = TryBuildOverloadDispatcherFromIr(
                irMethod,
                type.Name,
                ir,
                claims,
                lowering
            );

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

        var method =
            !irMethod.Semantics.IsAbstract
            && IrToTsObjectArgsBridge.HasObjectArgs(irMethod.Attributes)
                ? IrToTsClassBridge.BuildObjectArgsMethod(
                    irMethod,
                    returnType,
                    typeParameters,
                    lowering
                )
                : IrToTsClassBridge.BuildMethod(
                    irMethod,
                    parameters,
                    returnType,
                    typeParameters,
                    lowering
                );

        return [WithInheritedOverride(method, claims)];
    }

    /// <summary>
    /// Lowers a user-defined operator (single or overload-folded) to its TS
    /// helper methods, and a conversion operator group to one static per
    /// conversion (baseline FR-078, #600). A helper whose name an emitted base
    /// already declares on the same side (a derived class redeclaring
    /// <c>explicit operator int</c> or <c>operator +</c>) is emitted
    /// <c>override</c> (TS4114).
    /// </summary>
    private IReadOnlyList<TsClassMember> EmitOperator(
        INamedTypeSymbol type,
        IrClassDeclaration ir,
        IrMethodDeclaration irOp,
        TsOverloadClaims claims
    ) =>
        BuildOperatorMembers(type, ir, irOp, claims)
            .Select(member =>
                member is TsMethodMember method && claims.InheritsMember(method.Name, method.Static)
                    ? method with
                    {
                        Override = true,
                    }
                    : member
            )
            .ToList();

    /// <summary>
    /// The TS helper methods of a user-defined operator, deriving the emitted
    /// name from the operator's <c>[Name]</c> override or
    /// <see cref="IrMethodSemantics.OperatorKind"/>
    /// (<see cref="TsOperatorNaming"/>).
    /// </summary>
    private IReadOnlyList<TsClassMember> BuildOperatorMembers(
        INamedTypeSymbol type,
        IrClassDeclaration ir,
        IrMethodDeclaration irOp,
        TsOverloadClaims claims
    )
    {
        var owner = new TsOperatorOwner(
            _context.ResolveTsName(type),
            IrToTsTypeParameterMapper.Convert(ir.TypeParameters)
        );

        if (irOp.Semantics.ConversionName is not null)
            return EmitConversions(irOp, owner);

        if (TsOperatorNaming.OperatorNameOf(irOp) is not { } opName)
            return [];

        if (irOp.Overloads is { Count: > 0 } siblings)
        {
            var allOps = new[] { irOp with { Overloads = null } }.Concat(siblings).ToList();
            var perParams = allOps.Select(OperatorParameters).ToList();
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
                owner,
                opName,
                perParams,
                perReturns,
                perParamIrTypes,
                new TypeCheckScope(ir.TypeParameters ?? [], BclOverrides)
                {
                    RuntimeTypes = _groupLowering.RuntimeTypes,
                },
                claims.FastPathNameOf,
                _groupLowering
            );
        }

        return IrToTsClassBridge.BuildOperator(
            irOp,
            owner,
            opName,
            OperatorParameters(irOp),
            IrToTsTypeMapper.Map(irOp.ReturnType, BclOverrides),
            _groupLowering
        );
    }

    /// <summary>
    /// One static per conversion of a conversion operator group, in
    /// declaration order: the core keeps grouping them (for Dart), the
    /// TypeScript target un-groups them.
    /// </summary>
    private IReadOnlyList<TsClassMember> EmitConversions(
        IrMethodDeclaration primary,
        TsOperatorOwner owner
    ) =>
        IrMethodGroup
            .Of(primary)
            .Members.Select(conversion =>
                (TsClassMember)
                    IrToTsClassBridge.BuildConversion(
                        conversion,
                        owner,
                        OperatorParameters(conversion),
                        IrToTsTypeMapper.Map(conversion.ReturnType, BclOverrides),
                        _groupLowering
                    )
            )
            .ToList();

    private IReadOnlyList<TsParameter> OperatorParameters(IrMethodDeclaration op) =>
        op
            .Parameters.Select(p => new TsParameter(
                IrToTsNamingPolicy.ToParameterName(p.Name),
                IrToTsTypeMapper.MapParameterType(p, BclOverrides),
                Rest: p.IsRestParams
            ))
            .ToList();

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
                Rest: p.IsRestParams
            ))
            .ToList();

        var returnType = IrToTsTypeMapper.Map(irMethod.ReturnType, BclOverrides);

        var promotedStaticTps = PromoteClassTypeParamsForStatic(irMethod, owner.TypeParameters);
        var sourceTypeParams = (promotedStaticTps, irMethod.TypeParameters) switch
        {
            ({ Count: > 0 } p, { Count: > 0 } m) =>
            [
                .. p,
                .. m.Where(mt => p.All(pt => pt.Name != mt.Name)),
            ],
            ({ Count: > 0 } p, _) => p,
            (_, { Count: > 0 } m) => m,
            _ => null,
        };

        var typeParameters = IrToTsTypeParameterMapper
            .Convert(sourceTypeParams, BclOverrides)
            ?.ToList()
            ?? [];

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
    internal static IReadOnlyList<IrTypeParameter>? PromoteClassTypeParamsForStatic(
        IrMethodDeclaration method,
        IReadOnlyList<IrTypeParameter>? classTypeParameters
    )
    {
        if (!method.IsStatic || classTypeParameters is not { Count: > 0 } classTps)
            return null;

        var referenced = new HashSet<string>(StringComparer.Ordinal);
        CollectTypeParamRefs(method.ReturnType, referenced);
        foreach (var p in method.Parameters)
            CollectTypeParamRefs(p.Type, referenced);
        foreach (var rp in method.ReifiedTypeParameters)
            referenced.Add(rp.Name);

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
            case IrCollectionTypeRef collection:
                CollectTypeParamRefs(collection.LegacyForm, acc);
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
    private TsMethodMember BuildStructZeroFactory(
        INamedTypeSymbol type,
        IrClassDeclaration ir,
        string typeName
    )
    {
        var typeParameters = IrToTsTypeParameterMapper.Convert(ir.TypeParameters);

        var returnType = new TsNamedType(
            typeName,
            typeParameters?.Select(tp => (TsType)new TsNamedType(tp.Name)).ToList()
        );

        var hasSlot = ir.TypeArgumentSlot is not null && ir.TypeParameters is { Count: > 0 };
        var parameters = hasSlot
            ? new List<TsParameter>
            {
                new(
                    IrToTsClassBridge.TypeArgsParameterName,
                    IrToTsClassBridge.TypeArgsTupleType(ir.TypeParameters!),
                    Optional: true
                ),
            }
            : new List<TsParameter>();

        var zeroObject = BuildStructZeroObjectLiteral(
            type,
            ir,
            IrToTsExpressionBridge.TypeValueReference(typeName, origin: null),
            ir.TypeArgumentSlot is not null ? ir.TypeParameters : null
        );

        List<TsStatement> body;

        if (hasSlot)
        {
            body =
            [
                new TsVariableDeclaration("instance", zeroObject),
                new TsExpressionStatement(
                    new TsCallExpression(
                        new TsPropertyAccess(new TsIdentifier("Object"), "defineProperty"),
                        [
                            new TsIdentifier("instance"),
                            new TsStringLiteral(ir.TypeArgumentSlot!),
                            new TsObjectLiteral([
                                new TsObjectProperty(
                                    "value",
                                    new TsIdentifier(IrToTsClassBridge.TypeArgsParameterName)
                                ),
                                new TsObjectProperty("enumerable", new TsLiteral("false")),
                            ]),
                        ]
                    )
                ),
                new TsReturnStatement(new TsIdentifier("instance")),
            ];
        }
        else
        {
            body = [new TsReturnStatement(zeroObject)];
        }

        return new TsMethodMember(
            "$default",
            parameters,
            returnType,
            body,
            Static: true,
            TypeParameters: typeParameters
        );
    }

    private TsExpression BuildStructZeroObjectLiteral(
        INamedTypeSymbol type,
        IrClassDeclaration ir,
        TsExpression self,
        IReadOnlyList<IrTypeParameter>? slotTypeParameters
    )
    {
        var backingFields = PerClassBackingFields.For(type).ScopeFor(ir);
        var properties = new List<TsObjectProperty>();

        foreach (var storage in IrToTsStructDefaultConstructorBridge.StorageOf(ir))
        {
            var fieldName = backingFields.FieldOf(storage.MemberName) ?? storage.Name;
            var fieldSymbolType = FindMemberType(type, storage.MemberName);

            var zeroValue = StructFieldZeroExpression(
                storage.Type,
                fieldSymbolType,
                slotTypeParameters
            );

            properties.Add(new TsObjectProperty(fieldName, zeroValue));
        }

        return new TsCallExpression(
            new TsPropertyAccess(new TsIdentifier("Object"), "assign"),
            [
                new TsCallExpression(
                    new TsPropertyAccess(new TsIdentifier("Object"), "create"),
                    [new TsPropertyAccess(self, "prototype")]
                ),
                new TsObjectLiteral(properties),
            ]
        );
    }

    private static ITypeSymbol? FindMemberType(INamedTypeSymbol type, string memberName)
    {
        foreach (var member in type.GetMembers(memberName))
        {
            switch (member)
            {
                case IFieldSymbol field:
                    return field.Type;
                case IPropertySymbol prop:
                    return prop.Type;
            }
        }

        return null;
    }

    private TsExpression StructFieldZeroExpression(
        IrTypeRef fieldType,
        ITypeSymbol? symbolType,
        IReadOnlyList<IrTypeParameter>? slotTypeParameters
    )
    {
        if (fieldType is IrPrimitiveTypeRef primitive)
        {
            return primitive.Primitive switch
            {
                IrPrimitive.Boolean => new TsLiteral("false"),
                IrPrimitive.Byte
                or IrPrimitive.Int16
                or IrPrimitive.Int32
                or IrPrimitive.Float32
                or IrPrimitive.Float64 => new TsLiteral("0"),
                IrPrimitive.Int64 or IrPrimitive.UInt64 or IrPrimitive.BigInteger => new TsLiteral(
                    "0n"
                ),
                IrPrimitive.Char => new TsStringLiteral("\0"),
                IrPrimitive.Guid => new TsStringLiteral("00000000-0000-0000-0000-000000000000"),
                IrPrimitive.Decimal => new TsCallExpression(
                    new TsPostfixUnaryExpression(
                        new TsPropertyAccess(
                            new TsRuntimeHelperReference("DecimalDefaultType"),
                            "default"
                        ),
                        "!"
                    ),
                    []
                ),
                IrPrimitive.DateTime
                or IrPrimitive.DateTimeOffset
                or IrPrimitive.DateOnly
                or IrPrimitive.TimeOnly
                or IrPrimitive.TimeSpan => new TsCallExpression(
                    new TsPostfixUnaryExpression(
                        new TsPropertyAccess(
                            new TsRuntimeHelperReference($"{primitive.Primitive}DefaultType"),
                            "default"
                        ),
                        "!"
                    ),
                    []
                ),
                _ => new TsLiteral("null"),
            };
        }

        if (fieldType is IrTypeParameterRef tp && slotTypeParameters is not null)
        {
            for (var i = 0; i < slotTypeParameters.Count; i++)
            {
                if (slotTypeParameters[i].Name == tp.Name)
                {
                    return new TsParenthesized(
                        new TsBinaryExpression(
                            new TsCallExpression(
                                new TsPropertyAccess(
                                    new TsElementAccess(
                                        new TsIdentifier(IrToTsClassBridge.TypeArgsParameterName),
                                        new TsLiteral(
                                            i.ToString(
                                                System.Globalization.CultureInfo.InvariantCulture
                                            )
                                        ),
                                        Optional: true
                                    ),
                                    "default"
                                ),
                                [],
                                Optional: true
                            ),
                            "??",
                            new TsLiteral("null")
                        )
                    );
                }
            }

            return new TsLiteral("null");
        }

        if (fieldType is IrNamedTypeRef named && named.Semantics is { } semantics)
        {
            if (semantics.BrandedUnderlyingPrimitive is { } underlying)
                return StructFieldZeroExpression(new IrPrimitiveTypeRef(underlying), null, null);

            if (semantics.Kind is IrNamedTypeKind.NumericEnum)
            {
                var isBigInt =
                    symbolType is INamedTypeSymbol
                    {
                        EnumUnderlyingType.SpecialType: SpecialType.System_Int64
                            or SpecialType.System_UInt64,
                    };

                return new TsLiteral(isBigInt ? "0n" : "0");
            }

            if (semantics.Kind is IrNamedTypeKind.StringEnum)
            {
                if (symbolType is INamedTypeSymbol enumSym)
                {
                    var firstMember = enumSym
                        .GetMembers()
                        .OfType<IFieldSymbol>()
                        .FirstOrDefault(f => f.HasConstantValue);

                    if (firstMember is not null)
                    {
                        return new TsStringLiteral(
                            IrToTsNamingPolicy.ToEnumMemberName(
                                firstMember.Name,
                                IrAttributeExtractor.Extract(firstMember)
                            )
                        );
                    }
                }

                return new TsLiteral("null");
            }

            if (
                semantics.IsValueType
                && semantics.IsTranspilable
                && symbolType is INamedTypeSymbol nestedStruct
            )
            {
                var nestedRef = IrToTsRuntimeTypeReferences.ClassReference(named);

                if (
                    named.TypeArguments is { Count: > 0 } typeArgs
                    && _context.ReifiedTypeAnalysis.TypeArgumentSlotOf(
                        nestedStruct.OriginalDefinition
                    )
                        is not null
                )
                {
                    var scope = slotTypeParameters is not null
                        ? TsTypeParameterScope.ForCtorParam([
                            .. slotTypeParameters.Select(tp => tp.Name),
                        ])
                        : null;

                    var resolvedArgs = typeArgs
                        .Select(arg =>
                            IrToTsRuntimeTypeReferences.ReifiedArgumentOf(
                                arg,
                                "struct zero factory",
                                _context.RuntimeTypes,
                                scope
                            )
                        )
                        .ToList();

                    return new TsCallExpression(
                        new TsPropertyAccess(nestedRef, "$default"),
                        [new TsArrayLiteral(resolvedArgs)],
                        TypeArguments: typeArgs.Select(a => IrToTsTypeMapper.Map(a)).ToList()
                    );
                }

                var nestedIr = IrClassExtractor.Extract(nestedStruct, _context.Extraction);
                return BuildStructZeroObjectLiteral(nestedStruct, nestedIr, nestedRef, null);
            }
        }

        return new TsLiteral("null");
    }

    private TsConstructor? TryBuildConstructorDispatcherFromIr(
        IrClassDeclaration owner,
        DispatcherBaseCall baseCall,
        ConstructorSelectors selectors,
        IReadOnlyList<TsStatement>? implicitParameterlessBody,
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
            selectors,
            implicitParameterlessBody,
            lowering,
            TsConstructorModifier.SignatureJSDocSelectorFor(owner, groupAccessibility),
            owner.TypeArgumentSlot
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
        TsOverloadClaims claims,
        IrToTsBridgeContext lowering
    )
    {
        if (!_context.UseIrBodiesWhenCovered)
            return null;

        foreach (var m in IrMethodGroup.Of(primary).Members)
        {
            if (!HasCoveredBody(m))
                return null;
        }

        return IrToTsOverloadDispatcherBridge.BuildMethod(
            primary,
            typeName,
            owner.TypeParameters ?? [],
            claims,
            lowering,
            owner
        );
    }

    /// <summary>
    /// Whether the IR models the overload's body: an abstract overload has
    /// none to model (spec 013 FR-030), and neither has a synthesized
    /// value-equality member whose body the target synthesizes (FR-038).
    /// </summary>
    private static bool HasCoveredBody(IrMethodDeclaration method) =>
        method.Body is { } body
            ? IrBodyCoverageProbe.IsFullyCovered(body)
            : method.Semantics.IsAbstract || method.SynthesizedKind is not null;

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
            ? IrToTsClassBridge.BuildBaseCall(ctor, extendsType, keepsBaseMessage, lowering)
            : null;

        var call = baseCall ?? new TsBaseConstructorCall([], [], keepsBaseMessage);

        if (ir.BaseTypeArguments is { Count: > 0 } baseTypeArgs)
        {
            var baseArgsArray = new TsArrayLiteral([
                .. baseTypeArgs.Select(arg =>
                    TypeParameterTsResolver.ResolveArgument(
                        arg,
                        "base type argument",
                        lowering.RuntimeTypes,
                        lowering.TypeParameters
                    )
                ),
            ]);
            call = call with { Arguments = [.. call.Arguments, baseArgsArray] };
        }

        return ir.BaseType is IrCollectionTypeRef collection
            ? CollectionTypeArgumentSlots.AppendToSuper(call, collection, lowering.RuntimeTypes)
            : call;
    }

    /// <summary>How the members a constructor promotes are declared.</summary>
    private enum PromotedMemberDeclaration
    {
        ParameterProperties,
        Fields,
        OwnDeclarations,
    }
}
