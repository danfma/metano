using Metano.Annotations;
using Metano.Compiler.Diagnostics;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Analysis;

/// <summary>
/// Classifies the self-typed statics of a compilation (spec 001 FR-113) over the
/// reified type parameter analysis's triggers and edges, and reports the
/// <c>MS0054</c> of the calls and declarations they cannot serve.
/// </summary>
internal sealed class SelfTypedStaticClassifier
{
    private readonly IReadOnlyList<TriggerSite> _triggers;
    private readonly IReadOnlyList<CallEdge> _callEdges;
    private readonly IReadOnlyList<NewEdge> _newEdges;
    private readonly IReadOnlyList<GroupEdge> _groupEdges;
    private readonly IReadOnlyList<IMethodSymbol> _methods;
    private readonly Func<IMethodSymbol, bool> _isUnreifiable;
    private readonly IAssemblySymbol _currentAssembly;
    private readonly bool _isEmitPackage;
    private readonly TargetLanguage _target;

    public SelfTypedStaticClassifier(
        IReadOnlyList<TriggerSite> triggers,
        IReadOnlyList<CallEdge> callEdges,
        IReadOnlyList<NewEdge> newEdges,
        IReadOnlyList<GroupEdge> groupEdges,
        IReadOnlyList<IMethodSymbol> methods,
        Func<IMethodSymbol, bool> isUnreifiable,
        IAssemblySymbol currentAssembly,
        bool isEmitPackage,
        TargetLanguage target
    )
    {
        _triggers = triggers;
        _callEdges = callEdges;
        _newEdges = newEdges;
        _groupEdges = groupEdges;
        _methods = methods;
        _isUnreifiable = isUnreifiable;
        _currentAssembly = currentAssembly;
        _isEmitPackage = isEmitPackage;
        _target = target;
    }

    /// <summary>
    /// The self-typed static methods (FR-113 item 2): an exported static method of a
    /// CRTP-shaped class of an <c>[EmitPackage]</c> library, which could not carry a
    /// hidden parameter (W5-5d), whose only reified type parameter is the self parameter,
    /// needed only by <c>new TSelf()</c> or by a call forwarding it to another self-typed
    /// static of its class or a base; and those of them that forward.
    /// </summary>
    public (IReadOnlySet<IMethodSymbol> Members, IReadOnlySet<IMethodSymbol> Forwarders) Classify(
        IReadOnlyDictionary<ISymbol, HashSet<ITypeParameterSymbol>> reifiedByOwner
    )
    {
        var forwardedTo = new Dictionary<IMethodSymbol, List<IMethodSymbol>>(
            SymbolEqualityComparer.Default
        );

        if (_isEmitPackage)
        {
            foreach (var method in _methods)
            {
                if (SelfTypedForwardsOf(method.OriginalDefinition, reifiedByOwner) is { } callees)
                    forwardedTo[method.OriginalDefinition] = callees;
            }
        }

        var members = forwardedTo.Keys.ToHashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
        var changed = true;

        while (changed)
        {
            changed =
                members.RemoveWhere(member =>
                    forwardedTo[member].Any(callee => !members.Contains(callee))
                ) > 0;
        }

        var forwarders = members
            .Where(member => forwardedTo[member].Count > 0)
            .ToHashSet<IMethodSymbol>(SymbolEqualityComparer.Default);

        return (members, forwarders);
    }

    /// <summary>
    /// Reports <c>MS0054</c> at a call that cannot bind the self parameter to a receiving
    /// class (FR-113 item 5): an error for a self-typed static of this compilation; a
    /// warning for a referenced CRTP static whose self parameter is constructible, which
    /// may build its self type and would then construct the base class.
    /// </summary>
    public void ReportCallSites(
        IReadOnlySet<IMethodSymbol> selfTyped,
        IReadOnlyDictionary<
            INamedTypeSymbol,
            IReadOnlyList<IrReifiedTypeParameter>
        > reifiedClassParams,
        List<MetanoDiagnostic> diagnostics
    )
    {
        var reported = new HashSet<Location>();

        foreach (var call in _callEdges)
        {
            if (SelfCallDiagnosticOf(call, selfTyped, reifiedClassParams) is not { } diagnostic)
                continue;

            if (reported.Add(call.Location))
                diagnostics.Add(diagnostic);
        }
    }

    /// <summary>
    /// Reports <c>MS0054</c> at a static method of a class derived from a self-typed
    /// static's class that takes its emitted name (FR-113 item 5): a call on the derived
    /// class would reach the derived static. An error for a self-typed static of this
    /// compilation; a warning for a referenced package's CRTP static whose self parameter
    /// is constructible, which may be self-typed.
    /// </summary>
    public void ReportHidingStatics(
        IReadOnlySet<IMethodSymbol> selfTyped,
        List<MetanoDiagnostic> diagnostics
    )
    {
        foreach (var method in _methods.Where(method => method.IsStatic))
        {
            if (HiddenStaticOf(method, selfTyped) is not { } hidden)
                continue;

            var name = $"{hidden.ContainingType.Name}.{hidden.Name}";
            var owner = method.ContainingType.Name;

            diagnostics.Add(
                selfTyped.Contains(hidden)
                    ? new MetanoDiagnostic(
                        MetanoDiagnosticSeverity.Error,
                        DiagnosticCodes.UnreifiableTypeParameter,
                        $"Static '{owner}.{method.Name}' takes the name of '{name}', which "
                            + "builds the self type from the receiving class (spec 001 FR-113): "
                            + $"a call on '{owner}' reaches this static instead; rename it.",
                        method.Locations.FirstOrDefault() ?? Location.None
                    )
                    : new MetanoDiagnostic(
                        MetanoDiagnosticSeverity.Warning,
                        DiagnosticCodes.UnreifiableTypeParameter,
                        $"Static '{owner}.{method.Name}' takes the name of '{name}', declared in "
                            + $"a referenced package: if '{name}' builds the self type from the "
                            + "receiving class (spec 001 FR-113), a call on "
                            + $"'{owner}' reaches this static instead; rename it.",
                        method.Locations.FirstOrDefault() ?? Location.None
                    )
            );
        }
    }

    /// <summary>
    /// The self-typed static, or referenced CRTP static with a constructible self parameter,
    /// of a base class of <paramref name="method"/>'s class that <paramref name="method"/>
    /// takes the emitted name of; <c>null</c> when none.
    /// </summary>
    private IMethodSymbol? HiddenStaticOf(
        IMethodSymbol method,
        IReadOnlySet<IMethodSymbol> selfTyped
    )
    {
        for (
            var current = method.ContainingType.BaseType;
            current is not null;
            current = current.BaseType
        )
        {
            var hidden = current
                .OriginalDefinition.GetMembers()
                .OfType<IMethodSymbol>()
                .FirstOrDefault(member =>
                    IsHideable(member, selfTyped)
                    && CrtpSelfParameter.TakesNameOf(method, member, _target)
                );

            if (hidden is not null)
                return hidden;
        }

        return null;
    }

    private bool IsHideable(IMethodSymbol member, IReadOnlySet<IMethodSymbol> selfTyped) =>
        selfTyped.Contains(member)
        || (
            CrtpSelfParameter.IsReferencedCandidate(member, _currentAssembly)
            && CrtpSelfParameter.Of(member.ContainingType) is { HasConstructorConstraint: true }
        );

    private MetanoDiagnostic? SelfCallDiagnosticOf(
        CallEdge call,
        IReadOnlySet<IMethodSymbol> selfTyped,
        IReadOnlyDictionary<
            INamedTypeSymbol,
            IReadOnlyList<IrReifiedTypeParameter>
        > reifiedClassParams
    )
    {
        var callee = call.Callee.OriginalDefinition;
        var name = $"{callee.ContainingType.Name}.{callee.Name}";

        if (selfTyped.Contains(callee))
        {
            return UnsupportedSelfCallReason(call, selfTyped, reifiedClassParams) is { } reason
                ? new MetanoDiagnostic(
                    MetanoDiagnosticSeverity.Error,
                    DiagnosticCodes.UnreifiableTypeParameter,
                    $"'{name}' builds the self type from the receiving class (spec 001 FR-113), "
                        + $"but {reason}.",
                    call.Location
                )
                : null;
        }

        if (
            !CrtpSelfParameter.IsReferencedCandidate(call.Callee, _currentAssembly)
            || CrtpSelfParameter.Of(callee.ContainingType) is not { HasConstructorConstraint: true }
            || call.IsMethodGroup
            || UnboundSelfReason(call, reifiedClassParams) is not { } unbound
        )
            return null;

        return new MetanoDiagnostic(
            MetanoDiagnosticSeverity.Warning,
            DiagnosticCodes.UnreifiableTypeParameter,
            $"'{name}' is declared in a referenced package and {unbound}: if '{name}' builds "
                + "the self type from the receiving class (spec 001 FR-113), this call "
                + "constructs the base class at run time; call it on a concrete class.",
            call.Location
        );
    }

    private string? UnsupportedSelfCallReason(
        CallEdge call,
        IReadOnlySet<IMethodSymbol> selfTyped,
        IReadOnlyDictionary<
            INamedTypeSymbol,
            IReadOnlyList<IrReifiedTypeParameter>
        > reifiedClassParams
    )
    {
        if (call.IsMethodGroup)
            return "a method-group conversion names no receiving class; use a lambda that calls it on a concrete class";

        if (
            CrtpSelfParameter.ArgumentOf(call.Callee) is ITypeParameterSymbol
            && ReifiedSymbols.MemberOf(call.Caller) is IMethodSymbol caller
            && selfTyped.Contains(caller.OriginalDefinition)
            && IsSelfForwarding(call, caller.OriginalDefinition)
        )
            return null;

        return UnboundSelfReason(call, reifiedClassParams);
    }

    /// <summary>
    /// Why the call's self argument names no class to receive on: an open type parameter,
    /// a class whose static of that name hides the callee, or a generic class with reified
    /// type arguments; <c>null</c> when it names one.
    /// </summary>
    private string? UnboundSelfReason(
        CallEdge call,
        IReadOnlyDictionary<
            INamedTypeSymbol,
            IReadOnlyList<IrReifiedTypeParameter>
        > reifiedClassParams
    )
    {
        var argument = CrtpSelfParameter.ArgumentOf(call.Callee)!;

        if (argument is ITypeParameterSymbol parameter)
            return $"the call binds the self type to the type parameter '{parameter.Name}', which names no class";

        if (CrtpSelfParameter.IsHiddenOn(argument, call.Callee, _target))
            return $"'{argument.Name}' or a class between it and '{call.Callee.ContainingType.Name}' declares a static that takes the name '{call.Callee.Name}'";

        return
            argument is INamedTypeSymbol { IsGenericType: true } generic
            && reifiedClassParams.ContainsKey(generic.OriginalDefinition)
            ? $"'{argument.ToDisplayString()}' carries reified type arguments that construction from the receiving class does not pass"
            : null;
    }

    /// <summary>
    /// The callees <paramref name="method"/> forwards its self parameter to when it is a
    /// self-typed candidate, or <c>null</c> when it is none.
    /// </summary>
    private List<IMethodSymbol>? SelfTypedForwardsOf(
        IMethodSymbol method,
        IReadOnlyDictionary<ISymbol, HashSet<ITypeParameterSymbol>> reifiedByOwner
    )
    {
        if (
            OnlyReifiedSelfParameterOf(method, reifiedByOwner) is not { } self
            || !NeedsSelfOnlyToConstruct(method, self)
            || CreatesReifiedGeneric(method, reifiedByOwner)
        )
            return null;

        var callees = new List<IMethodSymbol>();

        foreach (var call in _callEdges)
        {
            if (
                !IsOwnedBy(ReifiedSymbols.MemberOf(call.Caller), method)
                || !IsReified(call.Callee.OriginalDefinition, reifiedByOwner)
                || !BindsTypeParameters(call.Callee)
            )
                continue;

            if (!IsSelfForwarding(call, method))
                return null;

            callees.Add(call.Callee.OriginalDefinition);
        }

        return callees;
    }

    /// <summary>
    /// The self parameter of <paramref name="method"/>'s class, when the method is an
    /// exported, reifiable static outside any override group whose only reified type
    /// parameter it is (FR-113 items 2a and 2b).
    /// </summary>
    private ITypeParameterSymbol? OnlyReifiedSelfParameterOf(
        IMethodSymbol method,
        IReadOnlyDictionary<ISymbol, HashSet<ITypeParameterSymbol>> reifiedByOwner
    )
    {
        if (
            method is not { IsStatic: true, MethodKind: MethodKind.Ordinary }
            || !ReifiedSymbols.IsExportedMember(method)
            || _isUnreifiable(method)
            || IsInGroup(method)
            || CrtpSelfParameter.Of(method.ContainingType) is not { } self
        )
            return null;

        return
            reifiedByOwner.TryGetValue(method, out var reified)
            && reified.Count == 1
            && reified.Contains(self)
            ? self
            : null;
    }

    /// <summary>
    /// Whether every runtime question <paramref name="method"/> asks about a type
    /// parameter is <c>new TSelf()</c> of its self parameter, outside a local function.
    /// </summary>
    private bool NeedsSelfOnlyToConstruct(IMethodSymbol method, ITypeParameterSymbol self) =>
        _triggers
            .Where(trigger => IsOwnedBy(trigger.Owner, method))
            .All(trigger =>
                trigger.IsNewConstraint
                && SymbolEqualityComparer.Default.Equals(trigger.TypeParameter, self)
                && !IsInLocalFunction(trigger.EnclosingMember)
            );

    /// <summary>
    /// Whether <paramref name="method"/> constructs a generic class with reified type
    /// arguments over its type parameters, which needs their descriptors.
    /// </summary>
    private bool CreatesReifiedGeneric(
        IMethodSymbol method,
        IReadOnlyDictionary<ISymbol, HashSet<ITypeParameterSymbol>> reifiedByOwner
    ) =>
        _newEdges.Any(edge =>
            IsOwnedBy(ReifiedSymbols.MemberOf(edge.Caller), method)
            && IsReified(edge.TargetType.OriginalDefinition, reifiedByOwner)
            && edge.TargetType.TypeArguments.Any(argument =>
                ReifiedSymbols.CollectTypeParameters(argument).Any()
            )
        );

    /// <summary>
    /// A call in <paramref name="caller"/>'s body that passes its own self parameter on to
    /// a static of its class or a base (<c>Use2() =&gt; Use(…)</c>): the callee is reached
    /// through the caller's receiving class (FR-113 item 2c).
    /// </summary>
    private static bool IsSelfForwarding(CallEdge call, IMethodSymbol caller) =>
        !call.IsMethodGroup
        && !IsInLocalFunction(call.Caller)
        && CrtpSelfParameter.ArgumentOf(call.Callee) is ITypeParameterSymbol argument
        && SymbolEqualityComparer.Default.Equals(
            argument,
            CrtpSelfParameter.Of(caller.ContainingType)
        )
        && IsSameOrBaseClass(call.Callee.ContainingType, caller.ContainingType)
        && !call.Callee.TypeArguments.Any(type => ReifiedSymbols.CollectTypeParameters(type).Any());

    private static bool IsSameOrBaseClass(INamedTypeSymbol candidate, INamedTypeSymbol type)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (
                SymbolEqualityComparer.Default.Equals(
                    current.OriginalDefinition,
                    candidate.OriginalDefinition
                )
            )
                return true;
        }

        return false;
    }

    private bool IsInGroup(IMethodSymbol method) =>
        _groupEdges.Any(edge =>
            SymbolEqualityComparer.Default.Equals(edge.Member.OriginalDefinition, method)
            || SymbolEqualityComparer.Default.Equals(edge.BaseOrIface.OriginalDefinition, method)
        );

    private static bool IsOwnedBy(ISymbol? owner, IMethodSymbol method) =>
        owner is IMethodSymbol ownerMethod
        && SymbolEqualityComparer.Default.Equals(ownerMethod.OriginalDefinition, method);

    private bool IsReified(
        ISymbol definition,
        IReadOnlyDictionary<ISymbol, HashSet<ITypeParameterSymbol>> reifiedByOwner
    ) =>
        SymbolEqualityComparer.Default.Equals(definition.ContainingAssembly, _currentAssembly)
        && reifiedByOwner.TryGetValue(definition, out var reified)
        && reified.Count > 0;

    private static bool BindsTypeParameters(IMethodSymbol callee) =>
        callee
            .TypeArguments.Concat(callee.IsStatic ? callee.ContainingType.TypeArguments : [])
            .Any(type => ReifiedSymbols.CollectTypeParameters(type).Any());

    private static bool IsInLocalFunction(ISymbol symbol) =>
        !SymbolEqualityComparer.Default.Equals(
            ReifiedSymbols.MemberOf(symbol, throughLocalFunctions: false),
            ReifiedSymbols.MemberOf(symbol)
        );
}
