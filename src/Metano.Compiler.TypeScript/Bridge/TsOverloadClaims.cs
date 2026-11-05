using Metano.Compiler.Diagnostics;
using Metano.Compiler.Extraction;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// The names the TypeScript claim pass (<see cref="TsOverloadClaimPass"/>)
/// gave one class's generated overload members — selectors, factories and
/// fast paths (spec 013 FR-004, DECIDED-18) — with the static side an
/// inheriting class claims against and the diagnostics the pass found. Computed
/// once per declaring type and cached (<see cref="TsOverloadClaimsRegistry"/>),
/// so every bridge spells a generated member the way its class declares it.
/// </summary>
internal sealed class TsOverloadClaims
{
    private readonly IReadOnlyDictionary<string, TsConstructorOverloadNames> _constructors;
    private readonly IReadOnlyDictionary<TsFastPathKey, string> _fastPaths;
    private readonly IReadOnlyList<TsClaimFinding> _findings;

    public TsOverloadClaims(
        IReadOnlyDictionary<string, TsConstructorOverloadNames> constructors,
        IReadOnlyDictionary<TsFastPathKey, string> fastPaths,
        IReadOnlyDictionary<string, TsClaimedMember> statics,
        IReadOnlyList<TsClaimFinding> findings
    )
    {
        _constructors = constructors;
        _fastPaths = fastPaths;
        _findings = findings;
        Statics = statics;
    }

    /// <summary>A class with no overload group and nothing to inherit.</summary>
    public static TsOverloadClaims None { get; } =
        new(
            new Dictionary<string, TsConstructorOverloadNames>(),
            new Dictionary<TsFastPathKey, string>(),
            new Dictionary<string, TsClaimedMember>(StringComparer.Ordinal),
            []
        );

    /// <summary>
    /// The class's whole TypeScript static side, its own members over the ones
    /// it inherits: what a derived class's claim pass checks (step 8, FR-026).
    /// </summary>
    public IReadOnlyDictionary<string, TsClaimedMember> Statics { get; }

    /// <summary>The selector and factory names of one constructor overload.</summary>
    public TsConstructorOverloadNames NamesOf(IrConstructorDeclaration constructor) =>
        _constructors.GetValueOrDefault(SuffixOf(constructor.OverloadName, "constructor"))
        ?? throw new InvalidOperationException(
            $"The claim pass named no constructor overload '{constructor.SignatureText}'."
        );

    /// <summary>
    /// The fast-path name of one overload of the group declared under
    /// <paramref name="baseName"/> (<c>add</c>, <c>__add</c>).
    /// </summary>
    public string FastPathNameOf(bool isStatic, string baseName, IrMethodDeclaration overload) =>
        _fastPaths.GetValueOrDefault(
            new TsFastPathKey(isStatic, baseName, SuffixOf(overload.OverloadName, overload.Name))
        )
        ?? throw new InvalidOperationException(
            $"The claim pass named no fast path for '{overload.SignatureText}'."
        );

    /// <summary>
    /// Reports what the claim pass found, located at the declarations of
    /// <paramref name="type"/>. The class's own emission calls it once.
    /// </summary>
    public void Report(INamedTypeSymbol type, Action<MetanoDiagnostic> report)
    {
        foreach (var finding in _findings)
            report(DiagnosticOf(finding, type));
    }

    private static MetanoDiagnostic DiagnosticOf(TsClaimFinding finding, INamedTypeSymbol type) =>
        new(finding.Severity, finding.Code, finding.Message, LocationOf(type, finding.Site));

    /// <summary>
    /// The declaration a finding is about: the constructor at its declaration
    /// ordinal, the method overload with its parameter names, or the first
    /// member with its C# name; the type when none matches.
    /// </summary>
    private static Location? LocationOf(INamedTypeSymbol type, TsClaimSite site)
    {
        var declaration = site.ConstructorOrdinal is { } ordinal
            ? IrConstructorExtractor.AllExplicitConstructors(type)[ordinal]
            : MemberAt(type, site);

        return (declaration ?? type).Locations.FirstOrDefault();
    }

    private static ISymbol? MemberAt(INamedTypeSymbol type, TsClaimSite site)
    {
        var members = type.GetMembers(site.MemberName);

        if (site.ParameterNames is not { } parameterNames)
            return members.FirstOrDefault();

        return members
            .OfType<IMethodSymbol>()
            .FirstOrDefault(method =>
                method.Parameters.Select(parameter => parameter.Name).SequenceEqual(parameterNames)
            );
    }

    private static string SuffixOf(IrOverloadName? name, string member) =>
        name?.Suffix
        ?? throw new InvalidOperationException(
            $"Overload '{member}' of a dispatched group carries no overload name."
        );
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
/// One fast path of a class: its table, the name its group is declared under
/// and the overload's suffix.
/// </summary>
internal sealed record TsFastPathKey(bool IsStatic, string BaseName, string Suffix);

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
/// also by its parameter names.
/// </summary>
internal sealed record TsClaimSite(
    string MemberName,
    int? ConstructorOrdinal = null,
    IReadOnlyList<string>? ParameterNames = null
);
