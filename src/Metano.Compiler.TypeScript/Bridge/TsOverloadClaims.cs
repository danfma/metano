using Metano.Compiler.Diagnostics;
using Metano.Compiler.Extraction;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// The names the TypeScript claim pass (<see cref="TsOverloadClaimPass"/>)
/// gave one class's generated overload members — selectors, factories, fast
/// paths and the forwarding members of further roots (spec 013 FR-004,
/// DECIDED-18) — with the static and instance sides an inheriting class
/// claims against and the diagnostics the pass found. Computed
/// once per declaring type and cached (<see cref="TsOverloadClaimsRegistry"/>),
/// so every bridge spells a generated member the way its class declares it.
/// </summary>
internal sealed class TsOverloadClaims
{
    private readonly IReadOnlyDictionary<int, TsConstructorOverloadNames> _constructors;
    private readonly IReadOnlyDictionary<TsMethodOverloadKey, TsMethodOverloadNames> _methods;
    private readonly IReadOnlyList<TsClaimFinding> _findings;

    public TsOverloadClaims(
        IReadOnlyDictionary<int, TsConstructorOverloadNames> constructors,
        IReadOnlyDictionary<TsMethodOverloadKey, TsMethodOverloadNames> methods,
        TsClaimSides sides,
        IReadOnlyList<TsClaimFinding> findings
    )
    {
        _constructors = constructors;
        _methods = methods;
        _findings = findings;
        Statics = sides.Statics;
        Instances = sides.Instances;
    }

    /// <summary>A class with no overload group and nothing to inherit.</summary>
    public static TsOverloadClaims None { get; } =
        new(
            new Dictionary<int, TsConstructorOverloadNames>(),
            new Dictionary<TsMethodOverloadKey, TsMethodOverloadNames>(),
            TsClaimSides.Empty,
            []
        );

    /// <summary>
    /// The class's whole TypeScript static side, its own members over the ones
    /// it inherits: what a derived class's claim pass checks (step 8, FR-026).
    /// </summary>
    public IReadOnlyDictionary<string, TsClaimedMember> Statics { get; }

    /// <summary>
    /// The class's whole TypeScript instance side, of every visibility, its own
    /// members over the ones it inherits: what a derived class's non-root fast
    /// paths claim against (step 7) and its user members are checked against
    /// (<c>MS0068</c>).
    /// </summary>
    public IReadOnlyDictionary<string, TsClaimedMember> Instances { get; }

    /// <summary>The selector and factory names of one constructor overload.</summary>
    public TsConstructorOverloadNames NamesOf(IrConstructorDeclaration constructor) =>
        ConstructorNamesOf(constructor.DeclarationOrdinal)
        ?? throw new InvalidOperationException(
            $"The claim pass named no constructor overload '{constructor.SignatureText}'."
        );

    /// <summary>
    /// The selector and factory names of the constructor overload at
    /// <paramref name="ordinal"/> (<see cref="IrOverloadRef.Ordinal"/>);
    /// <c>null</c> when the class has no dispatched constructor there.
    /// </summary>
    public TsConstructorOverloadNames? ConstructorNamesOf(int ordinal) =>
        _constructors.GetValueOrDefault(ordinal);

    /// <summary>The fast-path name of one method or operator overload.</summary>
    public string FastPathNameOf(IrMethodDeclaration overload) => MethodNamesOf(overload).FastPath;

    /// <summary>
    /// The forwarding members of the further interface roots
    /// <paramref name="overload"/> implements (naming.md § Root anchoring).
    /// </summary>
    public IReadOnlyList<string> ForwardingNamesOf(IrMethodDeclaration overload) =>
        MethodNamesOf(overload).Forwarders;

    /// <summary>
    /// Whether an emitted base already declares the overload's fast path —
    /// an interface implementation re-implemented by a derived type — so it
    /// is emitted <c>override</c> (TS4114).
    /// </summary>
    public bool OverridesInheritedFastPath(IrMethodDeclaration overload) =>
        OverridesInheritedMember(overload, FastPathNameOf(overload));

    /// <summary>
    /// Whether <paramref name="name"/> — the overload's fast path or one of its
    /// forwarders — overrides a fast path an emitted base declares.
    /// </summary>
    public bool OverridesInheritedMember(IrMethodDeclaration overload, string name) =>
        MethodNamesOf(overload).OverriddenNames.Contains(name);

    /// <summary>
    /// The fast-path name of the method overload the class declares under
    /// <paramref name="memberName"/> at <paramref name="ordinal"/>
    /// (<see cref="IrOverloadRef"/>); <c>null</c> when it declares no
    /// dispatched overload there.
    /// </summary>
    public string? FastPathNameAt(string memberName, int ordinal) =>
        _methods.GetValueOrDefault(new TsMethodOverloadKey(memberName, ordinal))?.FastPath;

    private TsMethodOverloadNames MethodNamesOf(IrMethodDeclaration overload) =>
        _methods.GetValueOrDefault(
            new TsMethodOverloadKey(overload.Name, overload.DeclarationOrdinal)
        )
        ?? throw new InvalidOperationException(
            $"The claim pass named no fast path for '{overload.SignatureText}'."
        );

    /// <summary>
    /// Reports what the claim pass found, located at the declarations of
    /// <paramref name="type"/>. The class's own emission calls it once.
    /// </summary>
    public void Report(INamedTypeSymbol type, Action<MetanoDiagnostic> report) =>
        Report(_findings, type, report);

    /// <summary>Reports <paramref name="findings"/>, located at the declarations of <paramref name="type"/>.</summary>
    public static void Report(
        IEnumerable<TsClaimFinding> findings,
        INamedTypeSymbol type,
        Action<MetanoDiagnostic> report
    )
    {
        foreach (var finding in findings)
            report(DiagnosticOf(finding, type));
    }

    private static MetanoDiagnostic DiagnosticOf(TsClaimFinding finding, INamedTypeSymbol type) =>
        new(finding.Severity, finding.Code, finding.Message, LocationOf(type, finding.Site));

    /// <summary>
    /// The declaration a finding is about: the constructor at its declaration
    /// ordinal, the method overload with its signature text, or the first
    /// member with its C# name; the type when none matches.
    /// </summary>
    private static Location? LocationOf(INamedTypeSymbol type, TsClaimSite site)
    {
        var declaration = site.ConstructorOrdinal is { } ordinal
            ? ConstructorAt(type, ordinal)
            : MemberAt(type, site);

        return (declaration ?? type).Locations.FirstOrDefault();
    }

    private static IMethodSymbol ConstructorAt(INamedTypeSymbol type, int ordinal) =>
        IrConstructorExtractor.AllExplicitConstructors(type).ElementAtOrDefault(ordinal)
        ?? throw new InvalidOperationException(
            $"'{type.Name}' has no declared constructor at ordinal {ordinal}."
        );

    private static ISymbol? MemberAt(INamedTypeSymbol type, TsClaimSite site)
    {
        var members = type.GetMembers(site.MemberName);

        if (site.Signature is not { } signature)
            return members.FirstOrDefault();

        return members
            .OfType<IMethodSymbol>()
            .FirstOrDefault(method => OverloadSignatureText.Of(method) == signature);
    }
}

/// <summary>
/// The claimed names of one constructor overload: its selector, its factory
/// (<c>null</c> for an abstract class, FR-012), and whether the factory keeps
/// an inherited factory's name, emitted <c>static override</c> (FR-026).
/// </summary>
internal sealed record TsConstructorOverloadNames(
    string Selector,
    string? Factory,
    bool OverridesInheritedFactory
);

/// <summary>
/// One method or operator overload of a class: its C# group name and its
/// declaration ordinal there (<see cref="IrOverloadRef"/>).
/// </summary>
internal sealed record TsMethodOverloadKey(string MemberName, int Ordinal);

/// <summary>
/// The claimed names of one method overload: its fast path, the forwarding
/// members of the further roots it implements, and the ones among them that
/// override a fast path an emitted base declares.
/// </summary>
internal sealed record TsMethodOverloadNames(
    string FastPath,
    IReadOnlyList<string> Forwarders,
    IReadOnlySet<string> OverriddenNames
);

/// <summary>A class's whole static and instance sides, inherited members included.</summary>
internal sealed record TsClaimSides(
    IReadOnlyDictionary<string, TsClaimedMember> Statics,
    IReadOnlyDictionary<string, TsClaimedMember> Instances
)
{
    public static TsClaimSides Empty { get; } =
        new(
            new Dictionary<string, TsClaimedMember>(StringComparer.Ordinal),
            new Dictionary<string, TsClaimedMember>(StringComparer.Ordinal)
        );
}

/// <summary>A diagnostic the claim pass found, and the declaration it is about.</summary>
internal sealed record TsClaimFinding(
    MetanoDiagnosticSeverity Severity,
    string Code,
    string Message,
    TsClaimSite Site
);

/// <summary>
/// The C# declaration a claim finding is about: a constructor by its
/// declaration ordinal, otherwise a member by its C# name — a method overload
/// also by its signature text (<see cref="OverloadSignatureText"/>), since the
/// overloads of a group may share every parameter name.
/// </summary>
internal sealed record TsClaimSite(
    string MemberName,
    int? ConstructorOrdinal = null,
    string? Signature = null
);
