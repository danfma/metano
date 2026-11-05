using Metano.Compiler.Diagnostics;
using Metano.Compiler.IR;
using Metano.Compiler.TypeScript.Transformation;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// The fast-path names an interface declares beside the dispatcher signatures
/// of each overload group (spec 013 FR-031): <c>areaS</c>, <c>areaWH</c>.
/// Claimed in the interface's one member table against its own members and
/// the earlier fast paths, in declaration order; a taken name takes <c>$</c>
/// and reports <c>MS0067</c>. A pure function of the interface's IR, so the
/// interface's emission and every implementer read the same names.
/// </summary>
internal sealed class TsInterfaceFastPaths
{
    private readonly Dictionary<TsMethodOverloadKey, string> _names = [];
    private readonly List<TsClaimFinding> _findings = [];

    private TsInterfaceFastPaths() { }

    /// <summary>What the claim found: one <c>MS0067</c> per renamed fast path.</summary>
    public IReadOnlyList<TsClaimFinding> Findings => _findings;

    /// <summary>
    /// The fast-path name of the overload the interface declares under
    /// <paramref name="memberName"/> at <paramref name="ordinal"/>;
    /// <c>null</c> when it declares no dispatched overload there.
    /// </summary>
    public string? NameAt(string memberName, int ordinal) =>
        _names.GetValueOrDefault(new TsMethodOverloadKey(memberName, ordinal));

    public string NameOf(IrMethodDeclaration method) =>
        NameAt(method.Name, method.DeclarationOrdinal)
        ?? throw new InvalidOperationException(
            $"The interface claim named no fast path for '{method.SignatureText}'."
        );

    private void Claim(string interfaceName, IrMethodDeclaration method, HashSet<string> taken)
    {
        var candidate =
            TypeScriptNaming.ToCamelCaseMember(method.Name) + DispatchedOverload.SuffixOf(method);

        var claimed = TsFreeName.FirstFree(candidate, taken.Contains);

        taken.Add(claimed);
        _names[new TsMethodOverloadKey(method.Name, method.DeclarationOrdinal)] = claimed;

        if (claimed != candidate)
            ReportRename(interfaceName, method, candidate, claimed);
    }

    private void ReportRename(
        string interfaceName,
        IrMethodDeclaration method,
        string candidate,
        string claimed
    ) =>
        _findings.Add(
            new TsClaimFinding(
                MetanoDiagnosticSeverity.Warning,
                DiagnosticCodes.OverloadNameShadowed,
                $"Fast path '{interfaceName}.{candidate}' of "
                    + $"'{DispatchedOverload.SignatureOf(method)}' collides with a member of the "
                    + $"interface; it is emitted as '{claimed}' (spec 013 FR-004).",
                new TsClaimSite(method.Name, Signature: DispatchedOverload.SignatureOf(method))
            )
        );

    public static TsInterfaceFastPaths Of(IrInterfaceDeclaration ir)
    {
        var claims = new TsInterfaceFastPaths();
        var methods = (ir.Members ?? []).OfType<IrMethodDeclaration>().ToList();
        var taken = MemberNamesOf(ir);

        foreach (var method in methods.Where(method => method.OverloadName is not null))
            claims.Claim(ir.Name, method, taken);

        return claims;
    }

    /// <summary>The names the interface's own members declare: properties and dispatchers.</summary>
    private static HashSet<string> MemberNamesOf(IrInterfaceDeclaration ir)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (var member in ir.Members ?? [])
        {
            switch (member)
            {
                case IrPropertyDeclaration property:
                    names.Add(
                        IrToTsNamingPolicy.ToDeclaredDataMemberName(
                            property.Name,
                            property.Attributes
                        )
                    );
                    break;

                case IrMethodDeclaration method:
                    names.Add(IrToTsNamingPolicy.ToMethodName(method.Name, method.Attributes));
                    break;
            }
        }

        return names;
    }
}
