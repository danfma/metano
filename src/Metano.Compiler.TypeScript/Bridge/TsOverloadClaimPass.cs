using Metano.Compiler.Diagnostics;
using Metano.Compiler.IR;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// The TypeScript claim pass of spec 013 (FR-004, DECIDED-18, contracts/naming.md
/// § Claim pass) over one class: resolves the candidate names the core's
/// naming policy derived from each overload's suffix against the class's own
/// static and instance tables and the static side it inherits, in one order —
/// user members and dispatchers (<see cref="TsClassMemberTable"/>), selectors,
/// factories, method fast paths, operator fast paths, then the inherited
/// statics. A taken name takes <c>$</c> until free and reports <c>MS0067</c>;
/// a constructor overload's selector and factory are claimed per suffix, so
/// they stay aligned (<c>fromCents$</c> / <c>byMoneyCents$</c>). A factory that
/// meets an inherited factory keeps its name when FR-026 allows it
/// (<see cref="TsFactoryInheritance"/>), otherwise it is qualified with the
/// type name. A user static that meets an inherited factory reports
/// <c>MS0068</c> (FR-034).
/// </summary>
internal sealed class TsOverloadClaimPass
{
    private const string SelectorPrefix = "by";
    private const string FactoryPrefix = "from";

    private readonly TsClaimPassInput _input;
    private readonly TsClassMemberTable _userMembers;
    private readonly Dictionary<string, TsClaimedMember> _statics;
    private readonly Dictionary<string, TsClaimedMember> _instances;
    private readonly Dictionary<string, TsConstructorOverloadNames> _constructors = new(
        StringComparer.Ordinal
    );
    private readonly Dictionary<TsFastPathKey, string> _fastPaths = [];
    private readonly List<TsClaimFinding> _findings = [];

    private TsOverloadClaimPass(TsClaimPassInput input)
    {
        _input = input;
        _userMembers = TsClassMemberTable.Of(input.Ir);
        _statics = UserClaims(_userMembers.Statics, input.TypeName);
        _instances = UserClaims(_userMembers.Instances, input.TypeName);
    }

    private IReadOnlyDictionary<string, TsClaimedMember> Inherited => _input.InheritedStatics;

    private string TypeName => _input.TypeName;

    public static TsOverloadClaims Run(TsClaimPassInput input) =>
        new TsOverloadClaimPass(input).Claim();

    private TsOverloadClaims Claim()
    {
        ReportRedeclaredFactories();
        ClaimConstructors();
        ClaimMethodFastPaths();
        ClaimOperatorFastPaths();

        return new TsOverloadClaims(_constructors, _fastPaths, StaticSide(), _findings);
    }

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

        RecordConstructor(overload, suffix, names, factory);

        if (firstCollision is not null)
            ReportRenamedConstructor(overload, firstCollision, names);
    }

    /// <summary>
    /// The candidate names of a suffix: its selector and, for a concrete class,
    /// its factory as FR-026 names it.
    /// </summary>
    /// <remarks>
    /// A factory FR-026 qualifies stays qualified once its suffix takes
    /// <c>$</c>: the qualified name goes through the claim pass like any other
    /// (<c>fromIntBoxValue$</c>).
    /// </remarks>
    private TsConstructorOverloadNames ConstructorNamesOf(
        string suffix,
        string claimed,
        TsFactorySignature? factory
    )
    {
        var selector = SelectorName(claimed);

        if (factory is null)
            return new TsConstructorOverloadNames(selector, null, false);

        var form =
            FactoryFormOf(suffix, factory) is FactoryForm.Qualified
                ? FactoryForm.Qualified
                : FactoryFormOf(claimed, factory);

        var factoryName =
            form is FactoryForm.Qualified
                ? FactoryPrefix + TypeName + claimed
                : FactoryPrefix + claimed;

        return new TsConstructorOverloadNames(
            selector,
            factoryName,
            form is FactoryForm.KeepsInherited
        );
    }

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
        string suffix,
        TsConstructorOverloadNames names,
        TsFactorySignature? factory
    )
    {
        var signature = DispatchedOverload.SignatureOf(overload);

        _constructors[suffix] = names;

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

    /// <summary>Step 5: the fast paths of each overloaded method group.</summary>
    private void ClaimMethodFastPaths()
    {
        foreach (var group in OverloadGroups(isOperator: false))
            ClaimFastPaths(group, TsClassMemberTable.MethodNameOf(group));
    }

    /// <summary>Step 6: the fast paths of each overloaded operator group.</summary>
    private void ClaimOperatorFastPaths()
    {
        foreach (var group in OverloadGroups(isOperator: true))
        {
            if (TsClassMemberTable.OperatorNameOf(group) is { } operatorName)
                ClaimFastPaths(group, TsClassMemberTable.OperatorDispatcherName(operatorName));
        }
    }

    private IEnumerable<IrMethodDeclaration> OverloadGroups(bool isOperator) =>
        (_input.Ir.Members ?? [])
            .OfType<IrMethodDeclaration>()
            .Where(method =>
                method.Overloads is { Count: > 0 } && method.Semantics.IsOperator == isOperator
            );

    /// <summary>
    /// Claims one fast path per overload in its group's table; a static fast
    /// path also against the inherited static side, which TypeScript checks
    /// (TS2417).
    /// </summary>
    private void ClaimFastPaths(IrMethodDeclaration group, string baseName)
    {
        var isStatic = group.IsStatic;

        foreach (var overload in new[] { group }.Concat(group.Overloads ?? []))
        {
            var suffix = DispatchedOverload.SuffixOf(overload);
            var candidate = baseName + suffix;
            var firstCollision = FastPathCollisionOf(candidate, isStatic);

            var claimed = TsFreeName.FirstFree(
                candidate,
                name => HolderOfFastPath(name, isStatic) is not null
            );

            TableOf(isStatic)[claimed] = new TsClaimedMember(
                TsClaimedMemberKind.FastPath,
                $"the fast path of '{DispatchedOverload.SignatureOf(overload)}'",
                TypeName
            );

            _fastPaths[new TsFastPathKey(isStatic, baseName, suffix)] = claimed;

            if (firstCollision is not null)
                ReportRenamedFastPath(overload, firstCollision, claimed);
        }
    }

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
    /// synthesized name — is the holder the message names, not a separate
    /// field.
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
            : _instances.GetValueOrDefault(name);

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

    /// <summary>The inherited static side under the class's own statics.</summary>
    private Dictionary<string, TsClaimedMember> StaticSide()
    {
        var side = new Dictionary<string, TsClaimedMember>(Inherited, StringComparer.Ordinal);

        foreach (var (name, member) in _statics)
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
/// <see cref="TsFactoryInheritance.Inherit"/>) and the factory signature of
/// each constructor overload.
/// </summary>
internal sealed record TsClaimPassInput(
    IrClassDeclaration Ir,
    string TypeName,
    IReadOnlyDictionary<string, TsClaimedMember> InheritedStatics,
    Func<IrConstructorDeclaration, TsFactorySignature> FactorySignatureOf
);
