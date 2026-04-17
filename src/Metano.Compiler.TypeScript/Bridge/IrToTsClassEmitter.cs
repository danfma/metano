using Metano.Annotations;
using Metano.Compiler.Diagnostics;
using Metano.Compiler.Extraction;
using Metano.Compiler.IR;
using Metano.Transformation;
using Metano.TypeScript.AST;
using Microsoft.CodeAnalysis;

namespace Metano.TypeScript.Bridge;

/// <summary>
/// Lowers an ordinary C# record / class / struct into a TypeScript class
/// declaration via the IR. This is the catch-all per-shape emitter —
/// anything that isn't an enum, interface, exception, inline-wrapper, or
/// static module ends up here.
///
/// Responsibilities:
/// <list type="bullet">
///   <item>Resolves the base class (when transpilable) and the inherited primary-constructor params</item>
///   <item>Builds the TS constructor (single ctor inline; multiple ctors → <see cref="IrToTsConstructorDispatcherBridge"/>)</item>
///   <item>Detects captured primary-ctor params (DI-style) and emits <c>this._field = param</c> assignments</item>
///   <item>Walks fields, properties (auto/computed/getter+setter), ordinary methods (with overloads), and user-defined operators</item>
///   <item>For records: appends <c>equals</c> / <c>hashCode</c> / <c>with</c> via <see cref="IrToTsRecordSynthesisBridge"/></item>
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
public sealed class IrToTsClassEmitter(TypeScriptTransformContext context)
{
    private readonly TypeScriptTransformContext _context = context;

    public void Transform(INamedTypeSymbol type, List<TsTopLevel> statements)
    {
        // The IR class extractor already populates BaseType / Interfaces /
        // TypeParameters with full Transpilable / Kind metadata, so the bridge
        // helpers do all the filtering. Inherited primary-ctor params still
        // need a Roslyn round-trip (the IR constructor on the base type isn't
        // in scope here).
        var ir = IrClassExtractor.Extract(
            type,
            originResolver: _context.OriginResolver,
            compilation: _context.Compilation,
            target: TargetLanguage.TypeScript
        );

        var extendsType = IrToTsClassBridge.BuildExtends(ir);
        var baseParams =
            type.BaseType is not null && extendsType is not null
                ? GetInheritedCtorParamsFromIr(type.BaseType.OriginalDefinition)
                : Array.Empty<TsConstructorParam>();

        // Promoted (record-style / primary-constructor) and DI-captured
        // params both flow from the IR. The TS type goes through
        // BclOverrides so [ExportFromBcl] mappings (decimal → Decimal, etc.)
        // apply; visibility, [Name], readonly flag, and default value all
        // come straight from IrConstructorParameter.
        var ownParams = IrToTsClassBridge.BuildPromotedCtorParams(
            ir.Constructor,
            ip => ResolveCtorParamTsType(ip.Parameter.Type),
            _context.DeclarativeMappings
        );
        // All params for equals/hashCode/with (conceptual fields — both inherited and own)
        var allParams = baseParams.Concat(ownParams).ToList();
        // Constructor signature: only own params (base properties are declared in parent)
        var ctorParamsForSignature = ownParams.ToList();

        var capturedParams = IrToTsClassBridge.BuildCapturedCtorParams(
            ir.Constructor,
            ip => ResolveCtorParamTsType(ip.Parameter.Type),
            ownParams.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase)
        );
        ctorParamsForSignature.AddRange(capturedParams);

        // Detect multiple constructors
        var explicitCtors = type
            .Constructors.Where(c => c.DeclaredAccessibility == Accessibility.Public)
            .Where(c => !c.IsImplicitlyDeclared || c.Parameters.Length > 0)
            .ToList();

        TsConstructor constructor;

        if (explicitCtors.Count > 1)
        {
            // The IR pipeline owns multi-constructor dispatch. When the body
            // coverage probe flags any overload as uncovered, surface a
            // build-time diagnostic and emit an empty constructor stub —
            // crashing here would abort the whole transpile for an otherwise
            // valid type, and silently dropping the type would hide the gap.
            constructor =
                TryBuildConstructorDispatcherFromIr(ir.Constructor)
                ?? EmitUnsupportedConstructor(
                    type,
                    $"Constructor overload group on '{type.Name}' contains constructs the IR "
                        + "pipeline doesn't yet model; an empty constructor was emitted."
                );
        }
        // Non-record class with an explicit constructor whose params don't match
        // any property (e.g., DI-injected services assigned to private fields in
        // the body). The record-style and captured-param paths miss this because
        // "view" ≠ "_view" and the assignment is in the body, not a field initializer.
        else if (HasUnmatchedExplicitConstructor(type, ctorParamsForSignature, explicitCtors))
        {
            // Accessibility None → plain parameter, no TS shorthand property
            // promotion. The param is assigned to a private field in the body,
            // so it must NOT become `public view: ICounterView` on the class.
            var ctorParams = (ir.Constructor?.Parameters ?? [])
                .Select(p => new TsConstructorParam(
                    IrToTsNamingPolicy.ToParameterName(p.Parameter.Name),
                    IrToTsTypeMapper.Map(p.Parameter.Type, BclOverrides),
                    Accessibility: TsAccessibility.None
                ))
                .ToList();
            constructor = IrToTsClassBridge.BuildSimpleConstructor(
                ir.Constructor,
                ctorParams,
                ResolveSuperArgs(extendsType, ir, baseParams),
                _context.DeclarativeMappings
            );
        }
        else
        {
            constructor = IrToTsClassBridge.BuildSimpleConstructor(
                ir.Constructor,
                ctorParamsForSignature,
                ResolveSuperArgs(extendsType, ir, baseParams),
                _context.DeclarativeMappings
            );
        }

        var classMembers = new List<TsClassMember>();
        var ctorParamNames = ctorParamsForSignature
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);

        // Walk the IR member list directly — IrClassExtractor already filtered
        // `[Ignore]`, implicit accessors, backing fields, enum members, and
        // `[Emit]` templates, and folded sibling overloads onto a primary
        // method's Overloads slot. Fields, properties, and events emit
        // in-place; operators and methods get deferred so the final layout
        // matches the legacy "operators before methods" ordering.
        var deferredOperators = new List<IrMethodDeclaration>();
        var deferredMethods = new List<IrMethodDeclaration>();
        foreach (var irMember in ir.Members ?? [])
        {
            switch (irMember)
            {
                case IrFieldDeclaration irField:
                    var fieldMember = IrToTsClassBridge.BuildField(
                        irField,
                        IrToTsTypeMapper.Map(irField.Type, BclOverrides),
                        _context.DeclarativeMappings
                    );
                    if (fieldMember is not null)
                        classMembers.Add(fieldMember);
                    break;

                case IrPropertyDeclaration irProp
                    when !ctorParamNames.Contains(
                        IrToTsNamingPolicy.ToInterfaceMemberName(irProp.Name, irProp.Attributes)
                    ):
                    classMembers.AddRange(
                        IrToTsClassBridge.BuildProperty(
                            irProp,
                            IrToTsTypeMapper.Map(irProp.Type, BclOverrides),
                            _context.DeclarativeMappings
                        )
                    );
                    break;

                case IrMethodDeclaration irMethod when irMethod.Semantics.IsOperator:
                    deferredOperators.Add(irMethod);
                    break;

                case IrMethodDeclaration irMethod when irMethod.Visibility != IrVisibility.Internal:
                    deferredMethods.Add(irMethod);
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

        foreach (var op in deferredOperators)
            classMembers.AddRange(EmitOperator(type, ir, op));

        foreach (var m in deferredMethods)
        {
            var members = EmitMethod(type, ir, m);
            if (members is not null)
                classMembers.AddRange(members);
        }

        // Generate equals, hashCode, with for records via the IR-driven bridge
        // so the output flows through the same path the Dart target uses.
        // Gate: records yes, [PlainObject] records no.
        if (ir.Semantics.IsRecord && !ir.Semantics.IsPlainObject)
            classMembers.AddRange(IrToTsRecordSynthesisBridge.Generate(ir, allParams));

        statements.Add(
            new TsClass(
                TypeTransformer.GetTsTypeName(type),
                constructor,
                classMembers,
                Extends: extendsType,
                Implements: IrToTsClassBridge.BuildImplements(ir),
                TypeParameters: IrToTsClassBridge.BuildTypeParameters(ir)
            )
        );
    }

    // ─── Constructor parameter discovery ──────────────────────

    /// <summary>
    /// Resolves the inherited primary-constructor parameters of a base type
    /// by extracting its IR and routing through
    /// <see cref="IrToTsClassBridge.BuildPromotedCtorParams"/>. Used to
    /// build the union of own + inherited params that record synthesis
    /// (equals / hashCode / with) consumes.
    /// </summary>
    private TsConstructorParam[] GetInheritedCtorParamsFromIr(INamedTypeSymbol baseType)
    {
        var baseIr = IrClassExtractor.Extract(
            baseType,
            originResolver: _context.OriginResolver,
            compilation: _context.Compilation,
            target: TargetLanguage.TypeScript
        );
        return IrToTsClassBridge
            .BuildPromotedCtorParams(
                baseIr.Constructor,
                ip => ResolveCtorParamTsType(ip.Parameter.Type),
                _context.DeclarativeMappings
            )
            .ToArray();
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
        IrMethodDeclaration irMethod
    )
    {
        if (irMethod.Body is { } body)
            foreach (var stmt in body)
                ReportUnsupportedInBody(stmt, type);

        if (irMethod.Overloads is { Count: > 0 })
        {
            var dispatcher = TryBuildOverloadDispatcherFromIr(irMethod, type.Name);
            if (dispatcher is null)
            {
                _context.ReportUnsupportedBody(
                    type,
                    $"Overload group '{type.Name}.{irMethod.Name}' contains constructs "
                        + "the IR pipeline doesn't yet model; the methods were skipped."
                );
                return null;
            }
            return dispatcher;
        }

        var (parameters, returnType, typeParameters) = LowerMethodSignature(irMethod, ir);
        return
        [
            IrToTsClassBridge.BuildMethod(
                irMethod,
                parameters,
                returnType,
                typeParameters,
                _context.DeclarativeMappings
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

        var typeName = TypeTransformer.GetTsTypeName(type);

        if (irOp.Overloads is { Count: > 0 } siblings)
        {
            var allOps = new[] { irOp with { Overloads = null } }.Concat(siblings).ToList();
            var perParams = allOps
                .Select<IrMethodDeclaration, IReadOnlyList<TsParameter>>(o =>
                    o.Parameters.Select(p => new TsParameter(
                            IrToTsNamingPolicy.ToParameterName(p.Name),
                            IrToTsTypeMapper.Map(p.Type, BclOverrides)
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
                _context.DeclarativeMappings
            );
        }

        var parameters = irOp
            .Parameters.Select(p => new TsParameter(
                IrToTsNamingPolicy.ToParameterName(p.Name),
                IrToTsTypeMapper.Map(p.Type, BclOverrides)
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
    ) LowerMethodSignature(IrMethodDeclaration irMethod, IrClassDeclaration owner)
    {
        var parameters = irMethod
            .Parameters.Select(p => new TsParameter(
                IrToTsNamingPolicy.ToParameterName(p.Name),
                IrToTsTypeMapper.Map(p.Type, BclOverrides)
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
            case IrFunctionTypeRef fn:
                CollectTypeParamRefs(fn.ReturnType, acc);
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
    /// an unsupported stub and surfaces the gap as a diagnostic.
    /// </summary>
    private TsConstructor? TryBuildConstructorDispatcherFromIr(IrConstructorDeclaration? primary)
    {
        if (!_context.UseIrBodiesWhenCovered)
            return null;
        if (primary is null || primary.Overloads is not { Count: > 0 })
            return null;

        foreach (var ctor in new[] { primary }.Concat(primary.Overloads))
        {
            if (ctor.Body is null || !IrBodyCoverageProbe.IsFullyCovered(ctor.Body))
                return null;
        }

        return IrToTsConstructorDispatcherBridge.Build(primary, _context.DeclarativeMappings);
    }

    /// <summary>
    /// Renders a method overload group through
    /// <see cref="IrToTsOverloadDispatcherBridge"/>. Returns <c>null</c> when
    /// the body coverage probe rejects any overload — the caller surfaces
    /// the gap as a diagnostic and skips the group.
    /// </summary>
    private IReadOnlyList<TsClassMember>? TryBuildOverloadDispatcherFromIr(
        IrMethodDeclaration primary,
        string typeName
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
            _context.DeclarativeMappings
        );
    }

    /// <summary>
    /// Reports MS0001 (UnsupportedFeature) when the IR statement extractor
    /// produced an <see cref="IrUnsupportedStatement"/> for a method body —
    /// the bridge would emit a "Not implemented" stub for the same node, so
    /// surfacing the diagnostic alongside keeps the build noisy about gaps.
    /// The IR doesn't carry per-statement source locations, so the diagnostic
    /// points at the containing type — granular enough for the user to find
    /// the offending member.
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
    }

    // ─── Constructor helpers ────────────────────────────────────

    /// <summary>
    /// Detects a non-record class with a single explicit constructor whose parameters
    /// weren't captured by the record-style or captured-param discovery paths.
    /// </summary>
    private static bool HasUnmatchedExplicitConstructor(
        INamedTypeSymbol type,
        IReadOnlyList<TsConstructorParam> resolvedParams,
        IReadOnlyList<IMethodSymbol> explicitCtors
    ) =>
        !type.IsRecord
        && resolvedParams.Count == 0
        && explicitCtors.Count == 1
        && explicitCtors[0].Parameters.Length > 0
        && !explicitCtors[0].IsImplicitlyDeclared;

    /// <summary>
    /// Maps a constructor parameter's IR type to TS, applying the
    /// <c>[ExportFromBcl]</c> overrides (decimal → Decimal from
    /// "decimal.js", etc.) via <see cref="BclExportTypeOverrides"/>. Used by
    /// <see cref="GetInheritedCtorParamsFromIr"/> and the promoted/captured
    /// param builders that take an IR-type-to-TS-type callback.
    /// </summary>
    private TsType ResolveCtorParamTsType(IrTypeRef irType) =>
        IrToTsTypeMapper.Map(irType, BclOverrides);

    private BclExportTypeOverrides BclOverrides => _context.BclOverrides;

    /// <summary>
    /// Resolves the <c>super(...)</c> argument list for the bridge. Returns
    /// <c>null</c> when there's no transpilable base — the bridge then skips
    /// the super call entirely. Otherwise prefers the IR-extracted base
    /// arguments (populated for primary-constructor base initializers like
    /// <c>class Foo(x) : Bar(x)</c>) and falls back to forwarding the base
    /// constructor's parameter names by identifier when no explicit base
    /// arguments were declared (chained inheritance through promoted
    /// properties).
    /// </summary>
    private IReadOnlyList<TsExpression>? ResolveSuperArgs(
        TsType? extendsType,
        IrClassDeclaration ir,
        IReadOnlyList<TsConstructorParam> baseParams
    )
    {
        if (extendsType is null)
            return null;
        if (ir.Constructor?.BaseArguments is { Count: > 0 } baseArgs)
        {
            return baseArgs
                .Select(a => IrToTsExpressionBridge.Map(a.Value, _context.DeclarativeMappings))
                .ToList();
        }
        return baseParams
            .Select<TsConstructorParam, TsExpression>(p => new TsIdentifier(p.Name))
            .ToList();
    }
}
