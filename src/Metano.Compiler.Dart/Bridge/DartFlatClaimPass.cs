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
        var members = ir.Members ?? [];

        var userMembers = members
            .Where(member => member is not IrMethodDeclaration { OverloadName: not null })
            .Select(member => IrToDartNamingPolicy.ToMemberName(member.Name, member.Attributes));

        var candidates = members
            .OfType<IrMethodDeclaration>()
            .Where(method => method.OverloadName is not null)
            .Select(method => new DartFlatCandidate(
                DartOverloadKey.Of(method),
                IrToDartNamingPolicy.ToFastPathName(method.Name, method.OverloadName!.Suffix),
                method.SignatureText
            ));

        return Run(ir.Name, userMembers, candidates);
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

        var candidates = functions
            .Where(function => function is { OverloadName: not null, OverloadRef: not null })
            .Select(function => new DartFlatCandidate(
                DartOverloadKey.Of(function.OverloadRef!),
                IrToDartNamingPolicy.ToFastPathName(function.Name, function.OverloadName!.Suffix),
                function.SignatureText
            ));

        return Run(moduleName, userMembers, candidates);
    }

    private static DartOverloadClaims Run(
        string ownerName,
        IEnumerable<string> userMembers,
        IEnumerable<DartFlatCandidate> candidates
    )
    {
        var pass = new DartFlatClaimPass(ownerName, userMembers);

        foreach (var candidate in candidates)
            pass.Claim(candidate.Key, candidate.Name, candidate.Signature);

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

/// <summary>One overload's candidate fast path in a flat table, with its C# signature.</summary>
internal sealed record DartFlatCandidate(DartOverloadKey Key, string Name, string? Signature);
