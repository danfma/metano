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
    private const string ClaimMark = "$";

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
        _statics = UserClaims(_userMembers.Statics);
        _instances = UserClaims(_userMembers.Instances);
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
        var suffix = SuffixOf(overload.OverloadName, overload.SignatureText);
        var claim = ClaimFirstFree(suffix, claimed => ConstructorCollisionOf(claimed, factory));
        var names = ConstructorNamesOf(claim.Name, factory);

        RecordConstructor(overload, suffix, names, factory);

        if (claim.FirstCollision is { } collision)
            ReportRenamedConstructor(overload, collision, names);
    }

    /// <summary>
    /// What holds the selector or the factory candidate of the suffix, if
    /// anything: the class's own statics, or the inherited static side — the
    /// inherited factory a kept factory overrides excepted.
    /// </summary>
    private TsCollision? ConstructorCollisionOf(string claimed, TsFactorySignature? factory)
    {
        var selector = SelectorName(claimed);

        if (HolderOfStatic(selector, includeInherited: true) is { } selectorHolder)
            return new TsCollision("Selector", selector, selectorHolder);

        if (factory is null)
            return null;

        var (factoryName, overrides) = FactoryNameOf(claimed, factory);

        return HolderOfStatic(factoryName, includeInherited: !overrides) is { } factoryHolder
            ? new TsCollision("Factory", factoryName, factoryHolder)
            : null;
    }

    private TsConstructorOverloadNames ConstructorNamesOf(
        string claimed,
        TsFactorySignature? factory
    )
    {
        if (factory is null)
            return new TsConstructorOverloadNames(SelectorName(claimed), null, false);

        var (factoryName, overrides) = FactoryNameOf(claimed, factory);

        return new TsConstructorOverloadNames(SelectorName(claimed), factoryName, overrides);
    }

    /// <summary>
    /// FR-026: <c>from</c> + suffix, kept as an override when the inherited
    /// static of that name is a factory the derived one can redeclare;
    /// <c>from</c> + type name + suffix when another inherited static holds it.
    /// </summary>
    private (string Name, bool Overrides) FactoryNameOf(string claimed, TsFactorySignature factory)
    {
        var plain = FactoryPrefix + claimed;

        if (!Inherited.TryGetValue(plain, out var inherited))
            return (plain, false);

        if (
            inherited.Factory is { } inheritedFactory
            && TsFactoryInheritance.CanRedeclare(inheritedFactory, factory)
        )
            return (plain, true);

        return (FactoryPrefix + TypeName + claimed, false);
    }

    private void RecordConstructor(
        IrConstructorDeclaration overload,
        string suffix,
        TsConstructorOverloadNames names,
        TsFactorySignature? factory
    )
    {
        _constructors[suffix] = names;
        _statics[names.Selector] = new TsClaimedMember(
            TsClaimedMemberKind.Selector,
            $"the selector of '{overload.SignatureText}'"
        );

        if (names.Factory is { } factoryName)
        {
            _statics[factoryName] = new TsClaimedMember(
                TsClaimedMemberKind.Factory,
                $"the factory of '{overload.SignatureText}'",
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
                + $"{collision.Holder.Description}; {emitted} (spec 013 FR-004).",
            new TsClaimSite(overload.SignatureText ?? "", overload.DeclarationOrdinal)
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
            var suffix = SuffixOf(overload.OverloadName, overload.SignatureText);
            var claim = ClaimFirstFree(
                baseName + suffix,
                name => FastPathCollisionOf(name, isStatic)
            );

            TableOf(isStatic)[claim.Name] = new TsClaimedMember(
                TsClaimedMemberKind.FastPath,
                $"the fast path of '{overload.SignatureText}'"
            );
            _fastPaths[new TsFastPathKey(isStatic, baseName, suffix)] = claim.Name;

            if (claim.FirstCollision is { } collision)
                ReportRenamedFastPath(overload, collision, claim.Name);
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
            $"Fast path '{TypeName}.{collision.Candidate}' of '{overload.SignatureText}' "
                + $"collides with {collision.Holder.Description}; it is emitted as '{claimed}' "
                + "(spec 013 FR-004).",
            new TsClaimSite(
                overload.Name,
                ParameterNames: overload.Parameters.Select(parameter => parameter.Name).ToList()
            )
        );

    /// <summary>
    /// <c>MS0067</c>: reason 1 when the holder is a user member, reason 2 when
    /// it is an earlier synthesized name; the message names the holder.
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

    /// <summary>The inherited static side under the class's own statics.</summary>
    private Dictionary<string, TsClaimedMember> StaticSide()
    {
        var side = new Dictionary<string, TsClaimedMember>(Inherited, StringComparer.Ordinal);

        foreach (var (name, member) in _statics)
            side[name] = member;

        return side;
    }

    /// <summary>
    /// The first spelling of <paramref name="candidate"/> nothing holds,
    /// appending <c>$</c> until free, and the collision that made it move
    /// (<c>null</c> when the candidate itself was free).
    /// </summary>
    private static TsClaim ClaimFirstFree(string candidate, Func<string, TsCollision?> collisionOf)
    {
        var firstCollision = collisionOf(candidate);
        var claimed = candidate;

        for (
            var collision = firstCollision;
            collision is not null;
            collision = collisionOf(claimed)
        )
            claimed += ClaimMark;

        return new TsClaim(claimed, firstCollision);
    }

    private static Dictionary<string, TsClaimedMember> UserClaims(
        IReadOnlyDictionary<string, string> userMembers
    ) =>
        userMembers.ToDictionary(
            entry => entry.Key,
            entry => new TsClaimedMember(
                TsClaimedMemberKind.User,
                $"the user member '{entry.Key}'"
            ),
            StringComparer.Ordinal
        );

    private static string SuffixOf(IrOverloadName? name, string? signature) =>
        name?.Suffix
        ?? throw new InvalidOperationException(
            $"Overload '{signature}' of a dispatched group carries no overload name."
        );

    /// <summary>A candidate name and what already holds it.</summary>
    private sealed record TsCollision(string Kind, string Candidate, TsClaimedMember Holder);

    /// <summary>A claimed name and the collision that moved it, if any.</summary>
    private sealed record TsClaim(string Name, TsCollision? FirstCollision);
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
