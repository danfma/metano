using Metano.Compiler.Diagnostics;
using Metano.Compiler.IR;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// The TypeScript claim pass of spec 013 (FR-004, DECIDED-18, contracts/naming.md
/// § Claim pass) over one class: resolves the candidate names the core's
/// naming policy derived from each overload's suffix against the class's own
/// static and instance tables and the sides it inherits, in one order — user
/// members and dispatchers (<see cref="TsClassMemberTable"/>), selectors,
/// factories, method fast paths, operator fast paths, the inherited instance
/// members, then the inherited statics. An anchored overload takes its root's
/// name (FR-025) and claims nothing. A taken name takes <c>$</c> until free
/// and reports <c>MS0067</c>;
/// a constructor overload's selector and factory are claimed per suffix, so
/// they stay aligned (<c>fromCents$</c> / <c>byMoneyCents$</c>). A factory that
/// meets an inherited factory keeps its name when FR-026 allows it
/// (<see cref="TsFactoryInheritance"/>), otherwise it is qualified with the
/// type name. A user static that meets an inherited factory, and a user
/// member that meets an inherited fast path, report <c>MS0068</c> (FR-034).
/// </summary>
internal sealed class TsOverloadClaimPass
{
    private const string SelectorPrefix = "by";
    private const string FactoryPrefix = "from";

    private readonly TsClaimPassInput _input;
    private readonly TsClassMemberTable _userMembers;
    private readonly Dictionary<string, TsClaimedMember> _statics;
    private readonly Dictionary<string, TsClaimedMember> _instances;
    private readonly Dictionary<int, TsConstructorOverloadNames> _constructors = [];
    private readonly Dictionary<TsMethodOverloadKey, TsMethodOverloadNames> _methods = [];
    private readonly List<TsClaimFinding> _findings = [];

    private TsOverloadClaimPass(TsClaimPassInput input)
    {
        _input = input;
        _userMembers = TsClassMemberTable.Of(input.Ir);
        _statics = UserClaims(_userMembers.Statics, input.TypeName);
        _instances = UserClaims(_userMembers.Instances, input.TypeName);
    }

    private IReadOnlyDictionary<string, TsClaimedMember> Inherited => _input.InheritedStatics;

    private IReadOnlyDictionary<string, TsClaimedMember> InheritedInstances =>
        _input.InheritedInstances;

    private string TypeName => _input.TypeName;

    public static TsOverloadClaims Run(TsClaimPassInput input) =>
        new TsOverloadClaimPass(input).Claim();

    private TsOverloadClaims Claim()
    {
        ReportRedeclaredFactories();
        ReportHijackedFastPaths();
        ClaimConstructors();
        ClaimMethodFastPaths();
        ClaimOperatorFastPaths();

        return new TsOverloadClaims(
            _constructors,
            _methods,
            new TsClaimSides(SideOf(Inherited, _statics), SideOf(InheritedInstances, _instances)),
            _findings
        );
    }

    /// <summary>
    /// <c>MS0068</c> reason 1: a user member whose name is an inherited fast
    /// path would override it, and the inherited dispatcher, which calls
    /// <c>this.&lt;fastPath&gt;</c>, would run the user member.
    /// </summary>
    private void ReportHijackedFastPaths()
    {
        foreach (var (name, declaredBy) in _userMembers.Instances)
        {
            if (InheritedFastPathAt(name) is { } fastPath)
                ReportHijack(declaredBy, name, fastPath);
        }
    }

    private TsClaimedMember? InheritedFastPathAt(string name) =>
        InheritedInstances.GetValueOrDefault(name)
            is { Kind: TsClaimedMemberKind.FastPath } fastPath
            ? fastPath
            : null;

    private void ReportHijack(string declaredBy, string name, TsClaimedMember fastPath) =>
        _findings.Add(
            new TsClaimFinding(
                MetanoDiagnosticSeverity.Error,
                DiagnosticCodes.OverloadFastPathHijacked,
                $"'{_input.Ir.Name}.{declaredBy}' would override the generated fast path "
                    + $"'{name}' of '{fastPath.DeclaredBy}.{fastPath.Declaration}'; rename the "
                    + "member, or rename the root overload's parameter or pin it with [Name] "
                    + "(spec 013 FR-034).",
                new TsClaimSite(declaredBy)
            )
        );

    /// <summary>
    /// <c>MS0068</c> reason 2: a user static whose name is an inherited
    /// generated factory would break the static side (TS2417), and neither name
    /// can move.
    /// </summary>
    private void ReportRedeclaredFactories()
    {
        foreach (var (name, declaredBy) in _userMembers.Statics)
        {
            if (Inherited.GetValueOrDefault(name)?.Factory is not { } factory)
                continue;

            _findings.Add(
                new TsClaimFinding(
                    MetanoDiagnosticSeverity.Error,
                    DiagnosticCodes.OverloadFastPathHijacked,
                    $"'{_input.Ir.Name}.{declaredBy}' would redeclare the generated factory "
                        + $"'{name}' of '{factory.Declaration}'; rename the member or give it "
                        + "[Name], or pin the constructor's suffix with [Name] (spec 013 FR-034).",
                    new TsClaimSite(declaredBy)
                )
            );
        }
    }

    /// <summary>
    /// Steps 3, 4 and 8 for each overload of a dispatched constructor, in
    /// declaration order. An abstract class gets selectors but no factories
    /// (FR-012).
    /// </summary>
    private void ClaimConstructors()
    {
        if (_input.Ir.Constructor is not { Overloads.Count: > 0 } constructor)
            return;

        var emitsFactories = !_input.Ir.Semantics.IsAbstract;

        foreach (var overload in constructor.DeclarationsInOrder)
        {
            var factory = emitsFactories ? _input.FactorySignatureOf(overload) : null;

            ClaimConstructor(overload, factory);
        }
    }

    private void ClaimConstructor(IrConstructorDeclaration overload, TsFactorySignature? factory)
    {
        var suffix = DispatchedOverload.SuffixOf(overload);
        var firstCollision = ConstructorCollisionOf(ConstructorNamesOf(suffix, suffix, factory));

        var claimed = TsFreeName.FirstFree(
            suffix,
            candidate =>
                ConstructorCollisionOf(ConstructorNamesOf(suffix, candidate, factory)) is not null
        );

        var names = ConstructorNamesOf(suffix, claimed, factory);

        RecordConstructor(overload, names, factory);

        if (firstCollision is not null)
            ReportRenamedConstructor(overload, firstCollision, names);
    }

    /// <summary>
    /// The candidate names of a suffix: its selector and, for a concrete class,
    /// its factory as FR-026 names it.
    /// </summary>
    private TsConstructorOverloadNames ConstructorNamesOf(
        string suffix,
        string claimed,
        TsFactorySignature? factory
    )
    {
        var selector = SelectorName(claimed);

        if (factory is null)
            return new TsConstructorOverloadNames(
                selector,
                Factory: null,
                OverridesInheritedFactory: false
            );

        var form = FactoryFormFor(suffix, claimed, factory);

        var factoryName =
            form is FactoryForm.Qualified
                ? FactoryPrefix + TypeName + claimed
                : FactoryPrefix + claimed;

        return new TsConstructorOverloadNames(
            selector,
            factoryName,
            OverridesInheritedFactory: form is FactoryForm.KeepsInherited
        );
    }

    /// <summary>
    /// The FR-026 form of the factory claimed under <paramref name="claimed"/>.
    /// A factory qualified under its own suffix stays qualified once the suffix
    /// takes <c>$</c>: the qualified name goes through the claim pass like any
    /// other (<c>fromIntBoxValue$</c>).
    /// </summary>
    private FactoryForm FactoryFormFor(string suffix, string claimed, TsFactorySignature factory) =>
        FactoryFormOf(suffix, factory) is FactoryForm.Qualified
            ? FactoryForm.Qualified
            : FactoryFormOf(claimed, factory);

    /// <summary>
    /// What holds the selector or the factory candidate, if anything: the
    /// class's own statics, or the inherited static side — the inherited
    /// factory a kept factory overrides excepted.
    /// </summary>
    private TsCollision? ConstructorCollisionOf(TsConstructorOverloadNames names)
    {
        if (HolderOfStatic(names.Selector, includeInherited: true) is { } selectorHolder)
            return new TsCollision("Selector", names.Selector, selectorHolder);

        if (names.Factory is not { } factory)
            return null;

        var holder = HolderOfStatic(factory, includeInherited: !names.OverridesInheritedFactory);

        return holder is null ? null : new TsCollision("Factory", factory, holder);
    }

    /// <summary>
    /// FR-026: <c>from</c> + suffix, kept as an override when the inherited
    /// static of that name is a factory the derived one can redeclare;
    /// qualified, <c>from</c> + type name + suffix, when another inherited
    /// static holds it.
    /// </summary>
    private FactoryForm FactoryFormOf(string claimed, TsFactorySignature factory)
    {
        if (!Inherited.TryGetValue(FactoryPrefix + claimed, out var inherited))
            return FactoryForm.Plain;

        return
            inherited.Factory is { } inheritedFactory
            && TsFactoryInheritance.CanRedeclare(inheritedFactory, factory)
            ? FactoryForm.KeepsInherited
            : FactoryForm.Qualified;
    }

    private void RecordConstructor(
        IrConstructorDeclaration overload,
        TsConstructorOverloadNames names,
        TsFactorySignature? factory
    )
    {
        var signature = DispatchedOverload.SignatureOf(overload);

        _constructors[overload.DeclarationOrdinal] = names;

        _statics[names.Selector] = new TsClaimedMember(
            TsClaimedMemberKind.Selector,
            $"the selector of '{signature}'",
            TypeName
        );

        if (names.Factory is { } factoryName)
        {
            _statics[factoryName] = new TsClaimedMember(
                TsClaimedMemberKind.Factory,
                $"the factory of '{signature}'",
                TypeName,
                factory
            );
        }
    }

    private void ReportRenamedConstructor(
        IrConstructorDeclaration overload,
        TsCollision collision,
        TsConstructorOverloadNames names
    )
    {
        var emitted = names.Factory is { } factory
            ? $"the generated factory and selector are emitted as '{factory}' and '{names.Selector}'"
            : $"the generated selector is emitted as '{names.Selector}'";

        ReportRename(
            $"{collision.Kind} '{TypeName}.{collision.Candidate}' collides with "
                + $"{DescriptionOf(collision.Holder)}; {emitted} (spec 013 FR-004).",
            new TsClaimSite(DispatchedOverload.SignatureOf(overload), overload.DeclarationOrdinal)
        );
    }

    /// <summary>Step 5: the fast paths of each dispatched method group.</summary>
    private void ClaimMethodFastPaths()
    {
        foreach (var group in OverloadGroups(isOperator: false))
            ClaimFastPaths(group, TsClassMemberTable.MethodNameOf(group.Primary));
    }

    /// <summary>Step 6: the fast paths of each overloaded operator group.</summary>
    private void ClaimOperatorFastPaths()
    {
        foreach (var group in OverloadGroups(isOperator: true))
        {
            if (TsClassMemberTable.OperatorNameOf(group.Primary) is { } operatorName)
                ClaimFastPaths(group, TsClassMemberTable.OperatorDispatcherName(operatorName));
        }
    }

    private IEnumerable<IrMethodGroup> OverloadGroups(bool isOperator) =>
        (_input.Ir.Members ?? [])
            .OfType<IrMethodDeclaration>()
            .Where(method =>
                method.OverloadName is not null && method.Semantics.IsOperator == isOperator
            )
            .Select(IrMethodGroup.Of);

    /// <summary>
    /// Names one fast path per overload of a group: an anchored overload its
    /// root's, the others claimed in its group's table — a static fast path
    /// also against the inherited static side (TS2417), an instance one
    /// against the inherited instance members of every visibility (step 7,
    /// TS2416).
    /// </summary>
    private void ClaimFastPaths(IrMethodGroup group, string baseName)
    {
        // The root names first: an own candidate never takes a name an
        // anchored overload must keep.
        foreach (var overload in group.Members)
        {
            if (overload.Anchor is not { } anchor)
                continue;

            var fastPath = AnchoredNameOf(overload, anchor, baseName);

            RecordFastPath(overload, fastPath, ForwardersOf(overload, fastPath));
        }

        foreach (var overload in group.Members.Where(member => member.Anchor is null))
            RecordFastPath(overload, ClaimOwnFastPath(overload, baseName), []);
    }

    /// <summary>
    /// The root's emitted name (FR-025): it is meant to override or
    /// implement, so it claims nothing. A user member of the class that holds
    /// it reports <c>MS0068</c>.
    /// </summary>
    private string AnchoredNameOf(
        IrMethodDeclaration overload,
        IrOverloadAnchor anchor,
        string baseName
    )
    {
        var name =
            _input.RootFastPathOf(anchor.Root) ?? baseName + DispatchedOverload.SuffixOf(overload);

        if (_userMembers.Instances.GetValueOrDefault(name) is { } declaredBy)
        {
            ReportHijack(
                declaredBy,
                name,
                new TsClaimedMember(
                    TsClaimedMemberKind.FastPath,
                    $"the fast path of '{DispatchedOverload.SignatureOf(overload)}'",
                    TypeName,
                    Declaration: DispatchedOverload.SignatureOf(overload)
                )
            );
        }

        return name;
    }

    private string ClaimOwnFastPath(IrMethodDeclaration overload, string baseName)
    {
        var isStatic = overload.IsStatic;
        var candidate = baseName + DispatchedOverload.SuffixOf(overload);
        var firstCollision = FastPathCollisionOf(candidate, isStatic);

        var claimed = TsFreeName.FirstFree(
            candidate,
            name => HolderOfFastPath(name, isStatic) is not null
        );

        if (firstCollision is not null)
            ReportRenamedFastPath(overload, firstCollision, claimed);

        return claimed;
    }

    /// <summary>
    /// The names of the further interface roots an overload implements, each
    /// a forwarding member to its fast path, when it differs from it.
    /// </summary>
    private IReadOnlyList<TsRootName> ForwardersOf(IrMethodDeclaration overload, string fastPath) =>
        (overload.Anchor?.FurtherRoots ?? [])
            .Select(root => (Root: root, Name: _input.RootFastPathOf(root)))
            .Where(entry => entry.Name is not null && entry.Name != fastPath)
            .Select(entry => new TsRootName(entry.Root, entry.Name!))
            .DistinctBy(entry => entry.Name, StringComparer.Ordinal)
            .ToList();

    private void RecordFastPath(
        IrMethodDeclaration overload,
        string fastPath,
        IReadOnlyList<TsRootName> forwarders
    )
    {
        var signature = DispatchedOverload.SignatureOf(overload);
        var table = TableOf(overload.IsStatic);
        var rootNames = forwarders
            .Prepend(new TsRootName(overload.Anchor?.Root, fastPath))
            .ToList();
        var names = rootNames.Select(rootName => rootName.Name).ToList();

        var overriddenNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var rootName in rootNames)
        {
            if (InheritedFastPathMetBy(overload, rootName.Name) is not { } inherited)
                continue;

            if (ReimplementsRoot(overload, rootName.Root))
                overriddenNames.Add(rootName.Name);
            else
                ReportHijack(overload.Name, rootName.Name, inherited);
        }

        foreach (var name in names)
        {
            table[name] = new TsClaimedMember(
                TsClaimedMemberKind.FastPath,
                $"the fast path of '{signature}'",
                TypeName,
                Declaration: signature
            );
        }

        _methods[new TsMethodOverloadKey(overload.Name, overload.DeclarationOrdinal)] =
            new TsMethodOverloadNames(
                fastPath,
                forwarders.Select(forwarder => forwarder.Name).ToList(),
                overriddenNames
            );
    }

    /// <summary>
    /// The inherited fast path an interface implementation's name for one of
    /// its roots meets; <c>null</c> when it meets none. A re-implementation of
    /// that root (a <c>new</c> method, an explicit re-implementation,
    /// <see cref="IrOverloadAnchor.ReimplementedRoots"/>) overrides it in
    /// TypeScript; any other overload of that name is one the
    /// anchored name cannot move from, <c>MS0068</c>. A constructed root's
    /// runtime dispatcher overrides the inherited name itself (FR-036).
    /// </summary>
    private TsClaimedMember? InheritedFastPathMetBy(IrMethodDeclaration overload, string name)
    {
        if (overload is not { Anchor.Kind: IrOverloadAnchorKind.Implementation, IsStatic: false })
            return null;

        if (_input.Ir.ConstructedRootOf(overload) is not null)
            return null;

        return InheritedFastPathAt(name);
    }

    private static bool ReimplementsRoot(IrMethodDeclaration overload, IrOverloadRef? root) =>
        root is not null && (overload.Anchor?.ReimplementedRoots ?? []).Contains(root);

    private TsCollision? FastPathCollisionOf(string name, bool isStatic) =>
        HolderOfFastPath(name, isStatic) is { } holder
            ? new TsCollision("Fast path", name, holder)
            : null;

    private void ReportRenamedFastPath(
        IrMethodDeclaration overload,
        TsCollision collision,
        string claimed
    ) =>
        ReportRename(
            $"Fast path '{TypeName}.{collision.Candidate}' of "
                + $"'{DispatchedOverload.SignatureOf(overload)}' "
                + $"collides with {DescriptionOf(collision.Holder)}; it is emitted as '{claimed}' "
                + "(spec 013 FR-004).",
            new TsClaimSite(overload.Name, Signature: DispatchedOverload.SignatureOf(overload))
        );

    /// <summary>
    /// <c>MS0067</c>. Its reason — 1 for a user member, 2 for an earlier
    /// synthesized name, 3 for an inherited instance member — is the holder
    /// the message names, not a separate field.
    /// </summary>
    private void ReportRename(string message, TsClaimSite site) =>
        _findings.Add(
            new TsClaimFinding(
                MetanoDiagnosticSeverity.Warning,
                DiagnosticCodes.OverloadNameShadowed,
                message,
                site
            )
        );

    private TsClaimedMember? HolderOfFastPath(string name, bool isStatic) =>
        isStatic
            ? HolderOfStatic(name, includeInherited: true)
            : _instances.GetValueOrDefault(name) ?? InheritedInstances.GetValueOrDefault(name);

    private TsClaimedMember? HolderOfStatic(string name, bool includeInherited) =>
        _statics.GetValueOrDefault(name)
        ?? (includeInherited ? Inherited.GetValueOrDefault(name) : null);

    private Dictionary<string, TsClaimedMember> TableOf(bool isStatic) =>
        isStatic ? _statics : _instances;

    private string SelectorName(string claimed) => SelectorPrefix + TypeName + claimed;

    /// <summary>
    /// How a message names a holder: as it is, or, for an inherited one, with
    /// the base type that declares it.
    /// </summary>
    private string DescriptionOf(TsClaimedMember holder) =>
        holder.DeclaredBy == TypeName
            ? holder.Description
            : $"{holder.Description} inherited from '{holder.DeclaredBy}'";

    /// <summary>An inherited side under the class's own members.</summary>
    private static Dictionary<string, TsClaimedMember> SideOf(
        IReadOnlyDictionary<string, TsClaimedMember> inherited,
        Dictionary<string, TsClaimedMember> own
    )
    {
        var side = new Dictionary<string, TsClaimedMember>(inherited, StringComparer.Ordinal);

        foreach (var (name, member) in own)
            side[name] = member;

        return side;
    }

    private static Dictionary<string, TsClaimedMember> UserClaims(
        IReadOnlyDictionary<string, string> userMembers,
        string typeName
    ) =>
        userMembers.ToDictionary(
            entry => entry.Key,
            entry => new TsClaimedMember(
                TsClaimedMemberKind.User,
                $"the user member '{entry.Key}'",
                typeName
            ),
            StringComparer.Ordinal
        );

    /// <summary>How a factory is named against the static side it inherits (FR-026).</summary>
    private enum FactoryForm
    {
        Plain,
        KeepsInherited,
        Qualified,
    }

    /// <summary>A candidate name and what already holds it.</summary>
    private sealed record TsCollision(string Kind, string Candidate, TsClaimedMember Holder);
}

/// <summary>
/// What the claim pass of one class reads: its IR, its emitted name, the
/// static side it inherits (already rewritten into its own type parameters,
/// <see cref="TsFactoryInheritance.Inherit"/>), the instance side it inherits,
/// the factory signature of each constructor overload and the emitted
/// fast-path name of a root overload (<c>null</c> when this compilation
/// cannot see its declaring type).
/// </summary>
internal sealed record TsClaimPassInput(
    IrClassDeclaration Ir,
    string TypeName,
    IReadOnlyDictionary<string, TsClaimedMember> InheritedStatics,
    IReadOnlyDictionary<string, TsClaimedMember> InheritedInstances,
    Func<IrConstructorDeclaration, TsFactorySignature> FactorySignatureOf,
    Func<IrOverloadRef, string?> RootFastPathOf
);

/// <summary>The name an overload declares for one of its roots.</summary>
internal sealed record TsRootName(IrOverloadRef? Root, string Name);
