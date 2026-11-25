using Metano.Compiler.IR;
using Metano.Compiler.Mappings;
using Metano.Compiler.TypeScript.AST;
using Metano.Compiler.TypeScript.Transformation;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// Lowers a dispatched method group (<see cref="IrMethodGroup"/>) into the
/// canonical TypeScript dispatcher shape (spec 013 FR-015, FR-030):
/// <list type="number">
///   <item>An addressable fast-path method per overload, holding its body
///   (<see cref="IrToTsStatementBridge"/>), named as the class's claim pass
///   named it — an anchored overload under its root's name — with its own
///   overload's visibility (#445's member mapping), <c>override</c> for an
///   override and <c>abstract</c> for an abstract overload. A fast path that
///   implements further interface roots gets one forwarding member per
///   further name.</item>
///   <item>A dispatcher method taking <c>...args: unknown[]</c> that
///   branches on argument count + runtime type checks and delegates to the
///   matching fast path with cast arguments. It takes the accessibility of the
///   group's most permissive overload; each overload signature carries the
///   JSDoc its group gives it
///   (<see cref="IrToTsVisibilityMapper.SignatureJSDocOf"/>), so
///   <c>stripInternal</c> drops only the signatures no other package can
///   reach: the <c>internal</c> ones and the <c>private</c> ones of a wider
///   group. A group that only overrides an inherited dispatcher's overloads
///   emits none; one that adds overloads to it emits an <c>override</c>
///   dispatcher whose signatures are the inherited ones plus its own and
///   whose unmatched calls go to <c>super</c>.</item>
/// </list>
/// The branches follow <see cref="OverloadDispatchOrder"/> and match the
/// arguments as <see cref="OverloadArgumentShape"/> describes. An omitted
/// optional argument reaches its fast path as <c>undefined</c>, where the
/// parameter's own default applies; the overload signatures mark it
/// optional (<c>a?: number</c>), since they cannot carry the default.
/// </summary>
public static class IrToTsOverloadDispatcherBridge
{
    /// <summary>
    /// Builds the fast-path members and the dispatcher of a dispatched group;
    /// nothing for a method outside one, which the caller lowers as a single
    /// method.
    /// </summary>
    /// <param name="primary">
    /// The method that carries the group (<see cref="IrMethodGroup"/>).
    /// </param>
    /// <param name="containingTypeName">
    /// Name of the class that hosts the overloads — used as the static
    /// receiver (<c>ClassName.fastPath(…)</c>). For instance overloads the
    /// dispatcher uses <c>this</c>, so the argument only matters when
    /// <paramref name="primary"/> is static.
    /// </param>
    /// <param name="classTypeParameters">
    /// The containing class's type parameters, which a parameter's type check
    /// resolves its type parameters against after the method's own.
    /// </param>
    /// <param name="names">
    /// The fast-path and forwarding names of each overload, as the class's
    /// claim pass named them (<see cref="TsOverloadClaims"/>).
    /// </param>
    /// <param name="owner">
    /// The class that declares the group, whose constructed roots
    /// (<see cref="IrClassDeclaration.ConstructedRoots"/>) take the overloads
    /// anchored on them as branches of a runtime dispatcher
    /// (<see cref="IrToTsConstructedRootDispatcherBridge"/>), not fast paths.
    /// </param>
    internal static IReadOnlyList<TsClassMember> BuildMethod(
        IrMethodDeclaration primary,
        string containingTypeName,
        IReadOnlyList<IrTypeParameter> classTypeParameters,
        TsOverloadClaims names,
        IrToTsBridgeContext context,
        IrClassDeclaration? owner = null
    )
    {
        var group = IrMethodGroup.Of(LiftClassTypeParameters(primary, classTypeParameters));

        if (!group.IsDispatched)
            return [];

        if (StaticAndInstanceSidesOf(group) is { } sides)
            return
            [
                .. sides.SelectMany(side =>
                    BuildSide(side, containingTypeName, classTypeParameters, names, context, owner)
                ),
            ];

        return BuildGroup(group, containingTypeName, classTypeParameters, names, context, owner);
    }

    /// <summary>
    /// One side of a group that mixes static and instance overloads: a group
    /// of its own, whose lone overload, when it has one, is also reachable
    /// under the C# name through a forwarding member in place of a
    /// dispatcher.
    /// </summary>
    private static List<TsClassMember> BuildSide(
        IrMethodGroup side,
        string containingTypeName,
        IReadOnlyList<IrTypeParameter> classTypeParameters,
        TsOverloadClaims names,
        IrToTsBridgeContext context,
        IrClassDeclaration? owner
    )
    {
        var members = BuildGroup(
            side,
            containingTypeName,
            classTypeParameters,
            names,
            context,
            owner
        );

        if (side.Members is [var lone])
            members.Add(
                BuildForwardingMember(
                    lone,
                    TsClassMemberTable.MethodNameOf(lone),
                    names.FastPathNameOf(lone),
                    overridesInherited: false,
                    context
                )
            );

        return members;
    }

    /// <summary>
    /// The fast paths, forwarders and dispatchers of one group whose overloads
    /// all sit on the same side (static or instance).
    /// </summary>
    private static List<TsClassMember> BuildGroup(
        IrMethodGroup group,
        string containingTypeName,
        IReadOnlyList<IrTypeParameter> classTypeParameters,
        TsOverloadClaims names,
        IrToTsBridgeContext context,
        IrClassDeclaration? owner
    )
    {
        var isStatic = group.Primary.IsStatic;
        var members = new List<TsClassMember>();

        foreach (var overload in OverloadDispatchOrder.Sort(group.Members, m => m.Parameters))
        {
            if (owner?.ConstructedRootOf(overload) is not null)
                continue;

            var fastPathName = names.FastPathNameOf(overload);

            members.Add(
                IsTargetSynthesized(overload) && owner is not null
                    ? IrToTsRecordSynthesisBridge.BuildFastPath(
                        owner,
                        overload,
                        fastPathName,
                        context
                    )
                    : BuildFastPathMember(
                        overload,
                        fastPathName,
                        isStatic,
                        names.OverridesInheritedFastPath(overload),
                        context
                    )
            );

            members.AddRange(
                names
                    .ForwardingNamesOf(overload)
                    .Select(forwarderName =>
                        BuildForwardingMember(
                            overload,
                            forwarderName,
                            fastPathName,
                            names.OverridesInheritedMember(overload, forwarderName),
                            context
                        )
                    )
            );
        }

        if (owner is not null)
            members.AddRange(ConstructedRootDispatchers(group, owner, names, context));

        var dispatcher = BuildDispatcher(
            group,
            containingTypeName,
            classTypeParameters,
            names,
            context,
            OverridesBaseValueEquality(group, owner)
        );

        if (dispatcher is not null)
            members.Add(dispatcher);

        return members;
    }

    /// <summary>
    /// A static group of a generic class with every overload re-declaring the
    /// class type parameters it names, as a single static method does
    /// (<see cref="IrToTsClassEmitter.PromoteClassTypeParamsForStatic"/>):
    /// TypeScript forbids a static member to name the class's own (TS2302,
    /// #449). An instance overload is unchanged.
    /// </summary>
    private static IrMethodDeclaration LiftClassTypeParameters(
        IrMethodDeclaration primary,
        IReadOnlyList<IrTypeParameter> classTypeParameters
    )
    {
        if (classTypeParameters.Count == 0)
            return primary;

        return LiftedOverload(primary, classTypeParameters) with
        {
            Overloads = primary
                .Overloads?.Select(overload => LiftedOverload(overload, classTypeParameters))
                .ToList(),
        };
    }

    private static IrMethodDeclaration LiftedOverload(
        IrMethodDeclaration overload,
        IReadOnlyList<IrTypeParameter> classTypeParameters
    )
    {
        if (
            IrToTsClassEmitter.PromoteClassTypeParamsForStatic(overload, classTypeParameters)
            is not { } promoted
        )
            return overload;

        var own = (overload.TypeParameters ?? []).Where(typeParameter =>
            promoted.All(classTypeParameter => classTypeParameter.Name != typeParameter.Name)
        );

        return overload with
        {
            TypeParameters = [.. promoted, .. own],
        };
    }

    /// <summary>
    /// The static and the instance overloads of a declared group that mixes
    /// both (C# groups <c>Add(int)</c> with <c>static Add(string)</c>), each
    /// lowered as a group of its own: an instance dispatcher cannot reach a
    /// static fast path through <c>this</c>, nor the reverse (#566). A side
    /// with one overload forwards to its fast path instead. <c>null</c>
    /// when the group keeps to one side.
    /// </summary>
    private static IReadOnlyList<IrMethodGroup>? StaticAndInstanceSidesOf(IrMethodGroup group)
    {
        if (group.DispatcherKind is not IrDispatcherKind.Declared)
            return null;

        var sides = group.Members.GroupBy(member => member.IsStatic).ToList();

        if (sides.Count < 2)
            return null;

        return sides
            .Select(side => IrMethodGroup.Of(side.First() with { Overloads = [.. side.Skip(1)] }))
            .ToList();
    }

    /// <summary>
    /// The forwarding members a class declares for the names its interfaces
    /// require of it, which its own and inherited members do not declare
    /// (<see cref="TsOverloadClaims.InterfaceForwarders"/>, #565, #595), and
    /// the dispatchers of the constructed roots it implements only through
    /// methods outside them (<see cref="TsOverloadClaims.ForwardingRoots"/>,
    /// #594).
    /// </summary>
    internal static IEnumerable<TsMethodMember> BuildInterfaceForwarders(
        IrClassDeclaration owner,
        TsOverloadClaims names,
        IrToTsBridgeContext context
    ) =>
        names
            .InterfaceForwarders.Select(forwarder =>
                BuildForwardingMember(
                    forwarder.Signature,
                    forwarder.Name,
                    forwarder.Target,
                    overridesInherited: false,
                    context
                )
            )
            .Concat(ForwardingRootDispatchers(owner, names, context));

    /// <summary>
    /// A synthesized value-equality member whose body the target supplies
    /// (spec 013 FR-038): no user declaration was folded into it and it routes
    /// to none.
    /// </summary>
    private static bool IsTargetSynthesized(IrMethodDeclaration overload) =>
        overload is { SynthesizedKind: not null, Body: null };

    /// <summary>
    /// Whether the group's dispatcher overrides the base record's member of
    /// its name: a group holding a synthesized member of a record that extends
    /// an emitted record, whose <c>equals</c> / <c>hashCode</c> / <c>with</c> the
    /// dispatcher takes over (under <c>noImplicitOverride</c>).
    /// </summary>
    private static bool OverridesBaseValueEquality(
        IrMethodGroup group,
        IrClassDeclaration? owner
    ) =>
        group.Synthesized is not null
        && owner is not null
        && IrToTsClassBridge.BuildExtends(owner) is not null;

    /// <summary>
    /// One runtime dispatcher per constructed root the group's overloads are
    /// anchored on (FR-036); every implementation of one root shares its name.
    /// </summary>
    private static IEnumerable<TsMethodMember> ConstructedRootDispatchers(
        IrMethodGroup group,
        IrClassDeclaration owner,
        TsOverloadClaims names,
        IrToTsBridgeContext context
    ) =>
        group
            .Members.Select(overload =>
                (Overload: overload, Root: owner.ConstructedRootOf(overload))
            )
            .Where(entry => entry.Root is not null)
            .GroupBy(entry => entry.Root!)
            .Select(implementations =>
                IrToTsConstructedRootDispatcherBridge.Build(
                    names.FastPathNameOf(implementations.First().Overload),
                    implementations.Key,
                    implementations.Select(entry => entry.Overload).ToList(),
                    owner.TypeParameters ?? [],
                    context
                )
            );

    /// <summary>
    /// The runtime dispatchers of the constructed roots no method of the class
    /// is anchored on, whose constructions it implements only through
    /// inherited methods or overrides of base methods (#594): each forwards
    /// every construction to its implementation.
    /// </summary>
    private static IEnumerable<TsMethodMember> ForwardingRootDispatchers(
        IrClassDeclaration owner,
        TsOverloadClaims names,
        IrToTsBridgeContext context
    ) =>
        names.ForwardingRoots.Select(root =>
            IrToTsConstructedRootDispatcherBridge.Build(
                root.Name,
                root.Root,
                implementations: [],
                owner.TypeParameters ?? [],
                context
            )
        );

    /// <summary>
    /// The group's dispatcher as <see cref="IrMethodGroup.DispatcherKind"/>
    /// decides it; <c>null</c> when it emits none. A group holding a
    /// synthesized value-equality member tries it first for an argument of the
    /// receiver's own type, then the checked branches, then the synthesized
    /// member again for any argument count it accepts, then the overloads it
    /// shadows (spec 013 FR-042).
    /// </summary>
    private static TsMethodMember? BuildDispatcher(
        IrMethodGroup group,
        string containingTypeName,
        IReadOnlyList<IrTypeParameter> classTypeParameters,
        TsOverloadClaims names,
        IrToTsBridgeContext context,
        bool overridesBaseValueEquality
    )
    {
        var kind = group.DispatcherKind;

        if (kind is IrDispatcherKind.None)
            return null;

        var typeOverrides = context.TypeOverrides;
        var inherited = group.Primary.InheritedOverloads ?? [];

        var classScope = new TypeCheckScope(classTypeParameters, typeOverrides)
        {
            RuntimeTypes = context.RuntimeTypes,
        }.WithDelegateAritiesOf(group.Branches.Select(branch => branch.Parameters));

        var shadowed = new HashSet<IrMethodDeclaration>(
            ShadowedIn(group, classScope).Select(found => found.Overload),
            ReferenceEqualityComparer.Instance
        );

        var branches = OverloadDispatchOrder
            .Sort(group.Branches, m => m.Parameters)
            .Where(branch => !shadowed.Contains(branch))
            .ToList();

        var signatures = OverloadDispatchOrder
            .Sort(inherited, m => m.Parameters)
            .Concat(OverloadDispatchOrder.Sort(SignatureMembersOf(group), m => m.Parameters))
            .Where(member => !shadowed.Contains(member))
            .ToList();

        var plan = PlanOverloadGroup(signatures, typeOverrides, sort: false);
        var overridesInherited = kind is IrDispatcherKind.OverridesInherited;
        var name = TsClassMemberTable.MethodNameOf(group.Primary);
        var receiver = DispatchReceiver(group.Primary.IsStatic, containingTypeName);

        var unmatched = overridesInherited
            ? ForwardToBase(name, inherited)
            : NoMatchingOverload(name);

        var body = group.Synthesized is { } synthesized
            ? BuildSynthesizedDispatcherBody(
                group,
                synthesized,
                branches,
                names,
                receiver,
                classScope,
                unmatched
            )
            : BuildDispatcherBody(
                branches,
                branches.Select(names.FastPathNameOf).ToList(),
                receiver,
                classScope,
                unmatched
            );

        return new TsMethodMember(
            name,
            DispatcherParameters(),
            plan.CommonReturn,
            body,
            Static: group.Primary.IsStatic,
            Async: plan.IsAsync,
            // The implementation is untagged: each overload signature
            // carries the JSDoc its group gives it (SignatureJSDocOf).
            Accessibility: plan.Accessibility,
            Overloads: plan.Signatures,
            Override: overridesInherited || overridesBaseValueEquality
        );
    }

    /// <summary>
    /// The overloads of <paramref name="primary"/>'s dispatchers whose
    /// runtime test repeats an earlier overload's (#778), which the
    /// dispatchers drop: the emitter reports them through this same rule.
    /// </summary>
    internal static IReadOnlyList<ShadowedOverload<IrMethodDeclaration>> ShadowedOverloadsOf(
        IrMethodDeclaration primary,
        IReadOnlyList<IrTypeParameter> classTypeParameters,
        IrToTsBridgeContext context
    )
    {
        var group = IrMethodGroup.Of(LiftClassTypeParameters(primary, classTypeParameters));

        if (!group.IsDispatched || group.DispatcherKind is IrDispatcherKind.None)
            return [];

        var classScope = new TypeCheckScope(classTypeParameters, context.TypeOverrides)
        {
            RuntimeTypes = context.RuntimeTypes,
        };

        return
        [
            .. (StaticAndInstanceSidesOf(group) ?? [group]).SelectMany(side =>
                ShadowedIn(side, classScope)
            ),
        ];
    }

    /// <summary>
    /// The branches of <paramref name="group"/>'s dispatcher that an earlier
    /// branch with the same test shadows. A dispatcher that also dispatches to
    /// a synthesized member keeps its own order rules and is left as it is.
    /// </summary>
    private static IReadOnlyList<ShadowedOverload<IrMethodDeclaration>> ShadowedIn(
        IrMethodGroup group,
        TypeCheckScope classScope
    ) =>
        group.Synthesized is not null || group.DispatcherKind is IrDispatcherKind.None
            ? []
            : ShadowedDispatchOverloads.Find(
                OverloadDispatchOrder.Sort(group.Branches, m => m.Parameters),
                method =>
                    BranchTestOf(
                        method,
                        classScope.WithDelegateAritiesOf(group.Branches.Select(b => b.Parameters))
                    ),
                method => ShadowedDispatchOverloads.TakesValueReturningDelegate(method.Parameters)
            );

    /// <summary>
    /// The members whose signatures the dispatcher declares besides the
    /// inherited ones: every member of a declared dispatcher, the ones a type
    /// adds to an inherited dispatcher.
    /// </summary>
    private static IEnumerable<IrMethodDeclaration> SignatureMembersOf(IrMethodGroup group) =>
        (
            group.DispatcherKind is IrDispatcherKind.Declared
                ? group.Members
                : group.Members.Where(member => !IrMethodGroup.IsOverride(member))
        ).Where(member => !RepeatsSynthesizedSignature(member, group.Synthesized));

    /// <summary>
    /// A shadowed overload with the synthesized member's parameter and return
    /// types (a user <c>HashCode()</c> beside the synthesized one): its signature
    /// would repeat the synthesized member's.
    /// </summary>
    private static bool RepeatsSynthesizedSignature(
        IrMethodDeclaration member,
        IrMethodDeclaration? synthesized
    ) =>
        synthesized is not null
        && member.IsShadowedBySynthesized
        && IrTypeRefEquivalence.AreEquivalent(member.ReturnType, synthesized.ReturnType)
        && IrTypeRefEquivalence.AreEquivalent(
            [.. member.Parameters.Select(parameter => parameter.Type)],
            [.. synthesized.Parameters.Select(parameter => parameter.Type)]
        );

    /// <summary>
    /// The body of a dispatcher whose group holds a synthesized value-equality
    /// member (spec 013 FR-042): the synthesized member for an argument of the
    /// receiver's own type, the checked branches, the synthesized member for
    /// any other call at its argument counts (so a foreign argument reaches
    /// <c>equals</c> and answers <c>false</c>), the shadowed overloads at the
    /// counts it does not accept, then the unmatched end.
    /// </summary>
    private static List<TsStatement> BuildSynthesizedDispatcherBody(
        IrMethodGroup group,
        IrMethodDeclaration synthesized,
        IReadOnlyList<IrMethodDeclaration> branches,
        TsOverloadClaims names,
        TsExpression receiver,
        TypeCheckScope classScope,
        TsStatement unmatched
    )
    {
        var shadowed = OverloadDispatchOrder.Sort(group.ShadowedBranches, m => m.Parameters);
        var fastPath = names.FastPathNameOf(synthesized);
        var typeOverrides = classScope.TypeOverrides;

        var body = new List<TsStatement>
        {
            SynthesizedBranches.First(synthesized, receiver, fastPath, typeOverrides),
        };

        body.AddRange(
            BuildBranches(
                branches,
                branches.Select(names.FastPathNameOf).ToList(),
                receiver,
                classScope
            )
        );

        if (
            SynthesizedBranches.Closing(synthesized, receiver, fastPath, typeOverrides) is
            { } closing
        )
            body.Add(closing);

        body.AddRange(
            BuildBranches(
                shadowed,
                shadowed.Select(names.FastPathNameOf).ToList(),
                receiver,
                classScope
            )
        );

        body.Add(unmatched);

        return body;
    }

    /// <summary>
    /// Builds the dispatcher + fast paths for a group of static overloads
    /// that lowers to free functions of a companion object (the
    /// <c>[Branded]</c> companion) instead of class members, under the names
    /// its plan claimed. The dispatcher branches to the fast paths through
    /// <paramref name="companion"/> (<c>Money.fromValue(…)</c>) and carries the
    /// overload signatures; generated call sites name the fast paths too (#569).
    /// </summary>
    /// <param name="group">A dispatched group: its overloads in dispatch
    /// order, each with its claimed fast path.</param>
    /// <param name="companion">The companion object the fast paths are members of.</param>
    /// <param name="context">
    /// The group's lowering context, threaded through to the body bridge.
    /// </param>
    internal static IReadOnlyList<TsFunction> BuildCompanionFunctions(
        TsCompanionDispatcher group,
        TsExpression companion,
        IrToTsBridgeContext context
    )
    {
        var methods = group.FastPaths.Select(fastPath => fastPath.Method).ToList();
        var plan = PlanOverloadGroup(methods, typeOverrides: null, sort: false);

        var functions = group
            .FastPaths.Select(fastPath =>
                BuildFastPathFunction(fastPath, plan.Accessibility, context)
            )
            .ToList();

        functions.Add(
            new TsFunction(
                group.FunctionName,
                DispatcherParameters(),
                plan.CommonReturn,
                BuildDispatcherBody(
                    methods,
                    group.FastPaths.Select(fastPath => fastPath.Name).ToList(),
                    receiver: companion,
                    (
                        TypeCheckScope.Empty with
                        {
                            RuntimeTypes = context.RuntimeTypes,
                        }
                    ).WithDelegateAritiesOf(methods.Select(method => method.Parameters)),
                    NoMatchingOverload(ErrorNameOf(group, methods[0]))
                ),
                Exported: true,
                Async: plan.IsAsync,
                Overloads: plan.Signatures
            )
        );

        return functions;
    }

    // ── Phases ───────────────────────────────────────────────────────────

    /// <summary>
    /// The target-shape-independent part of a dispatcher: the overloads in
    /// dispatch order, the async flag, the
    /// implementation return type, the overload signatures and the
    /// accessibility they share: the most permissive of the group.
    /// </summary>
    private sealed record OverloadGroupPlan(
        IReadOnlyList<IrMethodDeclaration> Sorted,
        bool IsAsync,
        TsType CommonReturn,
        IReadOnlyList<TsMethodOverload> Signatures,
        TsAccessibility Accessibility
    );

    /// <summary>
    /// Plans the dispatcher over <paramref name="overloads"/>, sorted into
    /// dispatch order unless <paramref name="sort"/> is off (the caller
    /// ordered them already).
    /// </summary>
    private static OverloadGroupPlan PlanOverloadGroup(
        IReadOnlyList<IrMethodDeclaration> overloads,
        IrToTsTypeOverrides? typeOverrides,
        bool sort
    )
    {
        var sorted = sort ? OverloadDispatchOrder.Sort(overloads, m => m.Parameters) : overloads;
        var isAsync = sorted.Any(m => m.Semantics.IsAsync);

        var accessibility = IrToTsVisibilityMapper.MostPermissiveOf(
            sorted.Select(method => IrToTsVisibilityMapper.Map(method.Visibility).Accessibility)
        );

        return new OverloadGroupPlan(
            sorted,
            isAsync,
            ResolveCommonReturnType(sorted, isAsync, typeOverrides),
            sorted
                .Select(method => BuildOverloadSignature(method, accessibility, typeOverrides))
                .ToList(),
            accessibility
        );
    }

    /// <summary>
    /// The implementation signature takes <c>...args: unknown[]</c> and can't
    /// name the overloads' method type parameters, so a group with any
    /// generic overload — or differing return types — falls back to an
    /// erased return type; the overload signatures keep the precise one.
    /// </summary>
    internal static TsType ResolveCommonReturnType(
        IReadOnlyList<IrMethodDeclaration> sorted,
        bool isAsync,
        IrToTsTypeOverrides? typeOverrides
    )
    {
        var returns = sorted
            .Select(m => IrToTsTypeMapper.Map(m.ReturnType, typeOverrides))
            .ToList();

        var isGeneric = sorted.Any(method => IsGeneric(method.TypeParameters));

        return CommonReturnTypeOf(returns, isGeneric, isAsync);
    }

    /// <summary>
    /// The one return type a dispatcher implementation can declare for its
    /// overloads' <paramref name="returns"/>: theirs when they all agree and
    /// none is generic, else the erased one. Each overload maps its own type
    /// instance, so equal types are told by their printed form
    /// (<c>Box&lt;T&gt;</c> and <c>Box&lt;T&gt;</c> hold distinct argument
    /// lists).
    /// </summary>
    internal static TsType CommonReturnTypeOf(
        IReadOnlyList<TsType> returns,
        bool isGeneric,
        bool isAsync
    )
    {
        var printed = returns.Select(Printer.RenderType).Distinct(StringComparer.Ordinal);
        var returnsOneType = printed.Take(2).Count() == 1;

        if (!isGeneric && returnsOneType)
            return returns[0];

        return isAsync
            ? new TsNamedType("Promise", [new TsNamedType("unknown")])
            : (TsType)new TsAnyType();
    }

    /// <summary>
    /// Whether the member declares method type parameters: the dispatcher
    /// body can't name them, so a generic overload erases its return type
    /// and casts its delegated arguments to <c>any</c>.
    /// </summary>
    private static bool IsGeneric(IReadOnlyList<IrTypeParameter>? typeParameters) =>
        typeParameters is { Count: > 0 };

    private static TsMethodOverload BuildOverloadSignature(
        IrMethodDeclaration method,
        TsAccessibility groupAccessibility,
        IrToTsTypeOverrides? typeOverrides
    ) =>
        new(
            SignatureParameters(method, typeOverrides),
            IrToTsTypeMapper.Map(method.ReturnType, typeOverrides),
            MapTypeParameters(method.TypeParameters),
            IrToTsVisibilityMapper.SignatureJSDocOf(
                IrToTsVisibilityMapper.Map(method.Visibility),
                groupAccessibility
            )
        );

    internal static List<TsParameter> DispatcherParameters() =>
        [new TsParameter("args", new TsNamedType("unknown[]"), Rest: true)];

    private static TsExpression DispatchReceiver(bool isStatic, string containingTypeName) =>
        isStatic ? new TsIdentifier(containingTypeName) : new TsIdentifier("this");

    /// <summary>
    /// One overload's fast path, with its overload's own visibility (FR-015):
    /// an override's is its root's, which the extractor already maps
    /// (<c>IrVisibilityMapper.MapMember</c>). An abstract overload declares an
    /// <c>abstract</c> signature, whose parameters can carry no initializer.
    /// </summary>
    private static TsMethodMember BuildFastPathMember(
        IrMethodDeclaration method,
        string fastPathName,
        bool isStatic,
        bool overridesInheritedFastPath,
        IrToTsBridgeContext context
    )
    {
        var visibility = IrToTsVisibilityMapper.Map(method.Visibility);
        var isAbstract = method.Semantics.IsAbstract;

        return new TsMethodMember(
            fastPathName,
            isAbstract
                ? SignatureParameters(method, context.TypeOverrides)
                : FastPathParameters(method, context),
            IrToTsTypeMapper.Map(method.ReturnType, context.TypeOverrides),
            isAbstract ? [] : IrToTsStatementBridge.MapBody(method.Body ?? [], context),
            Static: isStatic,
            Async: method.Semantics.IsAsync && !isAbstract,
            Accessibility: visibility.Accessibility,
            // Propagate method type parameters so a generic overload like
            // `Identity<T>(T value)` emits as `identityT<T>(value: T): T`
            // instead of referencing an undeclared T.
            TypeParameters: MapTypeParameters(method.TypeParameters),
            Abstract: isAbstract,
            Override: IrMethodGroup.IsOverride(method) || overridesInheritedFastPath,
            JSDoc: visibility.JSDoc
        );
    }

    /// <summary>
    /// A further root's name of an overload that implements several
    /// (naming.md § Root anchoring): it forwards to the fast path, which
    /// holds the body (NFR-003).
    /// </summary>
    private static TsMethodMember BuildForwardingMember(
        IrMethodDeclaration method,
        string forwarderName,
        string fastPathName,
        bool overridesInherited,
        IrToTsBridgeContext context
    )
    {
        var parameters = SignatureParameters(method, context.TypeOverrides);

        var call = new TsCallExpression(
            new TsPropertyAccess(new TsIdentifier("this"), fastPathName),
            parameters
                .Select(parameter =>
                    parameter.Rest
                        ? new TsSpreadExpression(new TsIdentifier(parameter.Name))
                        : (TsExpression)new TsIdentifier(parameter.Name)
                )
                .ToList()
        );

        return new TsMethodMember(
            forwarderName,
            parameters,
            IrToTsTypeMapper.Map(method.ReturnType, context.TypeOverrides),
            [new TsReturnStatement(call)],
            Static: method.IsStatic,
            TypeParameters: MapTypeParameters(method.TypeParameters),
            Override: overridesInherited
        );
    }

    /// <summary>
    /// A namespace fast path is always emitted — the dispatcher branches to it
    /// by bare name — so a body-less overload lowers to the same
    /// not-implemented stub a single companion function gets. It is exported
    /// when generated call sites name it; a namespace has no member
    /// accessibility, so an exported overload that is not public in C# is
    /// tagged as its dispatcher signature is
    /// (<see cref="IrToTsVisibilityMapper.SignatureJSDocOf"/>).
    /// </summary>
    private static TsFunction BuildFastPathFunction(
        TsCompanionFastPath fastPath,
        TsAccessibility groupAccessibility,
        IrToTsBridgeContext context
    )
    {
        var method = fastPath.Method;

        var jsDoc = IrToTsVisibilityMapper.SignatureJSDocOf(
            IrToTsVisibilityMapper.Map(method.Visibility),
            groupAccessibility
        );

        return new(
            fastPath.Name,
            FastPathParameters(method, context),
            IrToTsTypeMapper.Map(method.ReturnType),
            IrToTsBodyHelpers.LowerOrNotImplemented(method.Body, method.Name, context),
            Exported: true,
            Async: method.Semantics.IsAsync,
            TypeParameters: MapTypeParameters(method.TypeParameters),
            JSDoc: jsDoc
        );
    }

    /// <summary>Maps a method's parameters to TS parameters (local-binding names, rest for <c>params</c>).</summary>
    internal static List<TsParameter> MapParameters(
        IrMethodDeclaration method,
        IrToTsTypeOverrides? typeOverrides = null
    ) =>
        method
            .Parameters.Select(p => new TsParameter(
                IrToTsNamingPolicy.ToParameterName(p.Name),
                ParameterTypeOf(method, p, typeOverrides),
                Rest: p.IsRestParams
            ))
            .ToList();

    /// <summary>
    /// A parameter's TS type; the synthesized copy's overrides are a
    /// <c>Partial</c> of the record it returns, as the runtime's
    /// <c>copyRecordWith</c> takes them.
    /// </summary>
    private static TsType ParameterTypeOf(
        IrMethodDeclaration method,
        IrParameter parameter,
        IrToTsTypeOverrides? typeOverrides
    ) =>
        method.SynthesizedKind is IrSynthesizedValueMember.Copy
            ? IrToTsRecordSynthesisBridge.OverridesTypeOf(
                IrToTsTypeMapper.Map(method.ReturnType, typeOverrides)
            )
            : IrToTsTypeMapper.MapParameterType(parameter, typeOverrides);

    /// <summary>
    /// The parameters of an overload signature, where TypeScript forbids an
    /// initializer: a parameter a call may omit is optional instead.
    /// </summary>
    internal static List<TsParameter> SignatureParameters(
        IrMethodDeclaration method,
        IrToTsTypeOverrides? typeOverrides
    ) =>
        IrToTsClassBridge.InsertReifiedParameters(
            MapParameters(method, typeOverrides)
                .Select(
                    (parameter, index) =>
                        parameter with
                        {
                            Optional = method.Parameters[index].CanBeOmitted,
                        }
                )
                .ToList(),
            method.ReifiedTypeParameters,
            asOptionalSignature: true
        );

    /// <summary>
    /// The parameters of a fast path, carrying the C# defaults the dispatcher
    /// leaves to it when a call omits an argument; also the parameters of a
    /// branded companion's lone static function (#449).
    /// </summary>
    internal static List<TsParameter> FastPathParameters(
        IrMethodDeclaration method,
        IrToTsBridgeContext context
    ) =>
        IrToTsClassBridge.InsertReifiedParameters(
            MapParameters(method, context.TypeOverrides)
                .Select(
                    (parameter, index) =>
                        parameter with
                        {
                            Optional = method.Parameters[index].CanBeOmitted,
                            DefaultValue = FastPathDefault(method.Parameters[index], context),
                        }
                )
                .ToList(),
            method.ReifiedTypeParameters
        );

    /// <summary>
    /// The parameter's C# default as a TS initializer; <c>null</c> without one.
    /// </summary>
    private static TsExpression? FastPathDefault(
        IrParameter parameter,
        IrToTsBridgeContext context
    ) =>
        parameter.DefaultValue is { } defaultValue
            ? IrToTsExpressionBridge.Map(defaultValue, context)
            : null;

    internal static IReadOnlyList<TsTypeParameter>? MapTypeParameters(
        IReadOnlyList<IrTypeParameter>? typeParameters
    ) => IrToTsTypeParameterMapper.Convert(typeParameters);

    private static List<TsStatement> BuildDispatcherBody(
        IReadOnlyList<IrMethodDeclaration> sorted,
        IReadOnlyList<string> fastPathNames,
        TsExpression? receiver,
        TypeCheckScope classScope,
        TsStatement unmatched
    ) => [.. BuildBranches(sorted, fastPathNames, receiver, classScope), unmatched];

    /// <summary>One branch per overload, in the given order, each delegating to its fast path.</summary>
    private static List<TsStatement> BuildBranches(
        IReadOnlyList<IrMethodDeclaration> sorted,
        IReadOnlyList<string> fastPathNames,
        TsExpression? receiver,
        TypeCheckScope classScope
    )
    {
        var body = new List<TsStatement>();

        for (var i = 0; i < sorted.Count; i++)
            body.Add(BuildDelegatingBranch(sorted[i], fastPathNames[i], receiver, classScope));

        return body;
    }

    /// <summary>
    /// The branch that tests the arguments against <paramref name="method"/>'s
    /// parameters and delegates to <paramref name="fastPathName"/>.
    /// </summary>
    internal static TsIfStatement BuildDelegatingBranch(
        IrMethodDeclaration method,
        string fastPathName,
        TsExpression? receiver,
        TypeCheckScope classScope
    )
    {
        var arguments = ArgumentShapeOf(method);

        var delegateCall = new TsCallExpression(
            FastPathCallee(receiver, fastPathName),
            DelegatedArguments(
                method.Parameters,
                method.TypeParameters,
                arguments,
                classScope.TypeOverrides,
                method.ReifiedTypeParameters
            )
        );

        return new TsIfStatement(
            BranchTestOf(method, classScope),
            BuildBranchStatements(method, delegateCall)
        );
    }

    /// <summary>The runtime test of <paramref name="method"/>'s dispatcher branch.</summary>
    private static TsExpression BranchTestOf(
        IrMethodDeclaration method,
        TypeCheckScope classScope
    ) => ArgumentShapeOf(method).Guard(classScope.Within(method));

    private static OverloadArgumentShape ArgumentShapeOf(IrMethodDeclaration method) =>
        new(method.Parameters, method.ReifiedTypeParameters);

    /// <summary>
    /// The name a companion dispatcher's error spells, as its class
    /// counterpart does: an operator's name (<c>add</c>), else the camelCased
    /// C# method name.
    /// </summary>
    private static string ErrorNameOf(TsCompanionDispatcher group, IrMethodDeclaration first) =>
        TsOperatorNaming.OperatorNameOf(first)
        ?? TypeScriptNaming.ToCamelCaseMember(group.MemberName);

    /// <summary>
    /// The end of a dispatcher no branch matched: the legacy error, which
    /// names the camelCased public method the way callers spell it.
    /// </summary>
    internal static TsStatement NoMatchingOverload(string methodName) =>
        new TsThrowStatement(
            new TsNewExpression(
                new TsIdentifier("Error"),
                [new TsStringLiteral($"No matching overload for {methodName}")]
            )
        );

    /// <summary>
    /// The end of an overriding dispatcher: every call it does not handle
    /// goes to the inherited one (FR-030),
    /// <c>return super.add(...(args as [never]));</c>. The arguments are cast
    /// to a tuple of one inherited overload's arity, which TypeScript accepts
    /// as a spread into it; an overload with only a rest parameter takes
    /// <c>never[]</c>.
    /// </summary>
    internal static TsStatement ForwardToBase(
        string dispatcherName,
        IReadOnlyList<IrMethodDeclaration> inherited
    ) =>
        new TsReturnStatement(
            new TsCallExpression(
                new TsPropertyAccess(new TsIdentifier("super"), dispatcherName),
                [
                    new TsSpreadExpression(
                        new TsCastExpression(
                            new TsIdentifier("args"),
                            ForwardedArgumentsType(inherited)
                        )
                    ),
                ]
            )
        );

    private static TsType ForwardedArgumentsType(IReadOnlyList<IrMethodDeclaration> inherited)
    {
        var fixedArity = inherited.FirstOrDefault(overload =>
            !overload.Parameters.Any(parameter => parameter.IsRestParams)
        );

        if (fixedArity is null)
            return new TsArrayType(new TsNamedType("never"));

        return new TsTupleType(
            fixedArity.Parameters.Select(_ => (TsType)new TsNamedType("never")).ToList()
        );
    }

    /// <summary>
    /// The branch that delegates to the fast path: <c>call; return;</c> for a
    /// <c>void</c> overload, <c>return call;</c> otherwise.
    /// </summary>
    private static List<TsStatement> BuildBranchStatements(
        IrMethodDeclaration method,
        TsCallExpression delegateCall
    ) =>
        method.ReturnType is IrPrimitiveTypeRef { Primitive: IrPrimitive.Void }
            ? [new TsExpressionStatement(delegateCall), new TsReturnStatement()]
            : [new TsReturnStatement(delegateCall)];

    /// <summary>
    /// The fast path's arguments: each <c>args[i]</c> cast to its parameter
    /// type, and the trailing arguments of a <c>params</c> parameter spread
    /// back into its rest parameter (<c>...(args.slice(i) as T[])</c>).
    /// Shared by the class and the module dispatcher bodies, so a generic
    /// overload's arguments cast to <c>any</c> the same way at either call
    /// site.
    /// </summary>
    internal static List<TsExpression> DelegatedArguments(
        IReadOnlyList<IrParameter> parameters,
        IReadOnlyList<IrTypeParameter>? typeParameters,
        OverloadArgumentShape arguments,
        IrToTsTypeOverrides? typeOverrides,
        IReadOnlyList<IrReifiedTypeParameter>? reifiedTypeParameters = null
    )
    {
        var result = new List<TsExpression>(parameters.Count + (reifiedTypeParameters?.Count ?? 0));
        var declaredFixedCount = parameters is [.., { IsRestParams: true }]
            ? parameters.Count - 1
            : parameters.Count;

        for (var index = 0; index < declaredFixedCount; index++)
        {
            var parameter = parameters[index];
            result.Add(
                new TsCastExpression(
                    arguments.ValueOf(index),
                    DispatchArgumentType(typeParameters, parameter, typeOverrides)
                )
            );
        }

        if (reifiedTypeParameters is { Count: > 0 })
        {
            for (var i = 0; i < reifiedTypeParameters.Count; i++)
            {
                result.Add(
                    new TsCastExpression(arguments.ValueOf(declaredFixedCount + i), new TsAnyType())
                );
            }
        }

        if (declaredFixedCount < parameters.Count)
        {
            var restParam = parameters[declaredFixedCount];
            var value = new TsCastExpression(
                arguments.ValueOf(arguments.FixedCount),
                DispatchArgumentType(typeParameters, restParam, typeOverrides)
            );
            result.Add(new TsSpreadExpression(value));
        }

        return result;
    }

    /// <summary>
    /// The cast applied to <c>args[i]</c> before delegating. The dispatcher
    /// body can't name a generic overload's method type parameters, so those
    /// arguments are cast to <c>any</c> and the fast path infers its own. A
    /// <c>params</c> parameter takes its rest-parameter type, never
    /// <c>null</c>, since its arguments are spread.
    /// </summary>
    private static TsType DispatchArgumentType(
        IReadOnlyList<IrTypeParameter>? typeParameters,
        IrParameter parameter,
        IrToTsTypeOverrides? typeOverrides
    ) =>
        IsGeneric(typeParameters)
            ? new TsAnyType()
            : IrToTsTypeMapper.MapParameterType(parameter, typeOverrides);

    /// <summary>
    /// A null receiver means the fast path is a function in the dispatcher's
    /// own scope, reachable by bare name.
    /// </summary>
    private static TsExpression FastPathCallee(TsExpression? receiver, string fastPathName) =>
        receiver is null
            ? new TsIdentifier(fastPathName)
            : new TsPropertyAccess(receiver, fastPathName);
}
