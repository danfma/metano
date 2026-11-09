using Metano.Compiler.Diagnostics;
using Metano.Compiler.Extraction;
using Metano.Compiler.IR;

namespace Metano.Compiler.Dart.Bridge;

/// <summary>
/// The Dart claim pass over a table that holds only user members and fast
/// paths (spec 013 FR-004, FR-022, FR-031, FR-032): an interface's members,
/// or the top-level functions of a containerless module. Each dispatched
/// overload's candidate claims against the table in declaration order; a
/// taken name takes <c>$</c> and reports <c>MS0067</c>.
/// </summary>
internal sealed class DartFlatClaimPass
{
    private readonly string _ownerName;
    private readonly HashSet<string> _taken;
    private readonly Dictionary<DartOverloadKey, string> _fastPaths = [];
    private readonly List<DartClaimFinding> _findings = [];

    private DartFlatClaimPass(string ownerName, IEnumerable<string> userMembers)
    {
        _ownerName = ownerName;
        _taken = new HashSet<string>(userMembers, StringComparer.Ordinal);
    }

    /// <summary>The fast paths an interface declares beside its other members.</summary>
    public static DartOverloadClaims Run(IrInterfaceDeclaration ir)
    {
        var methods = (ir.Members ?? []).OfType<IrMethodDeclaration>().ToList();

        var userMembers = (ir.Members ?? [])
            .Where(member => member is not IrMethodDeclaration { OverloadName: not null })
            .Select(member => IrToDartNamingPolicy.ToMemberName(member.Name, member.Attributes));

        var pass = new DartFlatClaimPass(ir.Name, userMembers);

        foreach (var method in methods)
        {
            if (method.OverloadName is { } overloadName)
            {
                pass.Claim(
                    DartOverloadKey.Of(method),
                    IrToDartNamingPolicy.ToFastPathName(method.Name, overloadName.Suffix),
                    method.SignatureText
                );
            }
        }

        return pass.Result();
    }

    /// <summary>The top-level fast paths of a containerless module's dispatched groups.</summary>
    public static DartOverloadClaims Run(
        string moduleName,
        IReadOnlyList<IrModuleFunction> functions
    )
    {
        var userMembers = functions
            .Where(function => function.OverloadName is null)
            .Select(function =>
                IrToDartNamingPolicy.ToMemberName(function.Name, function.Attributes)
            );

        var pass = new DartFlatClaimPass(moduleName, userMembers);

        foreach (var function in functions)
        {
            if (function is { OverloadName: { } overloadName, OverloadRef: { } overloadRef })
            {
                pass.Claim(
                    DartOverloadKey.Of(overloadRef),
                    IrToDartNamingPolicy.ToFastPathName(function.Name, overloadName.Suffix),
                    function.SignatureText
                );
            }
        }

        return pass.Result();
    }

    private void Claim(DartOverloadKey key, string candidate, string? signature)
    {
        var claimed = DartFreeName.FirstFree(candidate, _taken.Contains);

        _taken.Add(claimed);
        _fastPaths[key] = claimed;

        if (claimed == candidate)
            return;

        _findings.Add(
            new DartClaimFinding(
                MetanoDiagnosticSeverity.Warning,
                DiagnosticCodes.OverloadNameShadowed,
                $"Dart target: fast path '{_ownerName}.{candidate}' of '{signature}' collides "
                    + $"with another member; it is emitted as '{claimed}' (spec 013 FR-004).",
                new OverloadDeclarationSite(key.MemberName, Signature: signature)
            )
        );
    }

    private DartOverloadClaims Result() =>
        new(
            new Dictionary<int, string>(),
            _fastPaths,
            new Dictionary<string, DartClaimedMember>(StringComparer.Ordinal),
            _findings
        );
}
