using Metano.Compiler.Analysis;
using Metano.Compiler.Diagnostics;
using Metano.Compiler.Extraction;
using Metano.Compiler.IR;

namespace Metano.Compiler.Dart.Bridge;

/// <summary>
/// The Dart claim pass of spec 013 (FR-004, FR-022, DECIDED-18) over one
/// class. Dart keeps instance members, static members and named constructors
/// in one namespace per class, where a named constructor conflicts with
/// another named constructor or a static member but not with an instance
/// member (<c>Money.fromCents</c> may sit beside an instance
/// <c>fromCents()</c>). Every candidate claims against that table, in one
/// order: the user members, the named constructors (<c>from&lt;Suffix&gt;</c>,
/// declaration order), then the method fast paths
/// (<c>&lt;group&gt;&lt;Suffix&gt;</c>) — anchored overloads first, which take
/// their root's name (FR-025) and claim nothing, then the type's own, which
/// also claim against the inherited members of every visibility (step 7). A
/// taken name takes <c>$</c> until free and reports <c>MS0067</c>; a user
/// member that meets an inherited fast path, or holds an anchored overload's
/// name, reports <c>MS0068</c> (FR-034). Overloaded operator groups name
/// nothing: Dart declares one <c>operator +</c> per class.
/// </summary>
internal sealed class DartClassClaimPass
{
    private readonly DartClassClaimInput _input;
    private readonly IReadOnlyList<DartUserMember> _userMembers;
    private readonly Dictionary<string, DartClaimedMember> _table;
    private readonly Dictionary<int, string> _constructors = [];
    private readonly Dictionary<DartOverloadKey, string> _fastPaths = [];
    private readonly List<DartClaimFinding> _findings = [];

    private DartClassClaimPass(DartClassClaimInput input)
    {
        _input = input;
        _userMembers = DartClassMemberNames.Of(input.Ir, input.Shape);
        _table = new Dictionary<string, DartClaimedMember>(StringComparer.Ordinal);

        foreach (var member in _userMembers)
        {
            _table.TryAdd(
                member.DartName,
                new DartClaimedMember(
                    DartClaimedMemberKind.User,
                    $"the user member '{member.DartName}'",
                    input.TypeName,
                    member.IsStatic
                )
            );
        }
    }

    public static DartOverloadClaims Run(DartClassClaimInput input) =>
        new DartClassClaimPass(input).Claim();

    private string TypeName => _input.TypeName;

    private IReadOnlyDictionary<string, DartClaimedMember> Inherited => _input.InheritedMembers;

    private DartOverloadClaims Claim()
    {
        ReportHijackedFastPaths();

        if (_input.Shape.NamesMembers)
        {
            ClaimConstructors();
            ClaimMethodFastPaths();
        }

        return new DartOverloadClaims(
            _input.Shape,
            _constructors,
            _fastPaths,
            InheritableMembers(),
            _findings
        );
    }

    /// <summary>
    /// <c>MS0068</c> reason 1: a user member whose name is an inherited fast
    /// path would override it.
    /// </summary>
    private void ReportHijackedFastPaths()
    {
        foreach (var member in _userMembers)
        {
            if (
                Inherited.GetValueOrDefault(member.DartName) is
                { Kind: DartClaimedMemberKind.FastPath } fastPath
            )
                ReportHijack(member, fastPath);
        }
    }

    private void ReportHijack(DartUserMember member, DartClaimedMember fastPath) =>
        _findings.Add(
            new DartClaimFinding(
                MetanoDiagnosticSeverity.Error,
                DiagnosticCodes.OverloadFastPathHijacked,
                $"Dart target: '{TypeName}.{member.DartName}' would override "
                    + $"{DescriptionOf(fastPath)}; rename the member, or rename the root "
                    + "overload's parameter or pin it with [Name] (spec 013 FR-034).",
                new OverloadDeclarationSite(member.CSharpName)
            )
        );

    /// <summary>One named constructor per overload of a dispatched set, in declaration order.</summary>
    private void ClaimConstructors()
    {
        if (_input.Ir.Constructor is not { Overloads.Count: > 0 } constructor)
            return;

        foreach (var overload in constructor.DeclarationsInOrder)
        {
            if (overload.OverloadName is { } overloadName)
                ClaimConstructor(overload, overloadName);
        }
    }

    private void ClaimConstructor(IrConstructorDeclaration overload, IrOverloadName overloadName)
    {
        var candidate = IrToDartNamingPolicy.ToNamedConstructorName(overloadName.Suffix);
        var claimed = DartFreeName.FirstFree(candidate, IsTakenForConstructor);
        var signature = overload.SignatureText ?? candidate;

        _constructors[overload.DeclarationOrdinal] = claimed;

        _table[claimed] = new DartClaimedMember(
            DartClaimedMemberKind.NamedConstructor,
            $"the named constructor of '{signature}'",
            TypeName,
            IsStatic: false
        );

        if (claimed == candidate)
            return;

        ReportRename(
            $"Named constructor '{TypeName}.{candidate}' of '{signature}'",
            candidate,
            claimed,
            new OverloadDeclarationSite(IrOverloadRef.ConstructorName, overload.DeclarationOrdinal)
        );
    }

    private bool IsTakenForConstructor(string name) =>
        _table.GetValueOrDefault(name) is { ConflictsWithConstructor: true };

    /// <summary>The fast paths of each dispatched method group.</summary>
    private void ClaimMethodFastPaths()
    {
        var overloads = (_input.Ir.Members ?? [])
            .OfType<IrMethodDeclaration>()
            .Where(DartClassMemberNames.IsFastPathGroup)
            .SelectMany(method => IrMethodGroup.Of(method).Members)
            .ToList();

        // The root names first: an own candidate never takes a name an
        // anchored overload must keep.
        foreach (var overload in overloads)
        {
            if (overload.Anchor is { } anchor)
                Record(overload, AnchoredNameOf(overload, anchor));
        }

        foreach (var overload in overloads)
        {
            if (overload.Anchor is null)
                Record(overload, ClaimOwnFastPath(overload));
        }
    }

    /// <summary>
    /// The root's emitted name (FR-025): it overrides or implements, so it
    /// claims nothing. A user member that holds it reports <c>MS0068</c>.
    /// </summary>
    private string AnchoredNameOf(IrMethodDeclaration overload, IrOverloadAnchor anchor)
    {
        var name = _input.RootFastPathOf(anchor.Root) ?? CandidateOf(overload);

        foreach (var member in _userMembers.Where(member => member.DartName == name))
        {
            ReportHijack(
                member,
                new DartClaimedMember(
                    DartClaimedMemberKind.FastPath,
                    $"the fast path of '{overload.SignatureText}'",
                    TypeName,
                    overload.IsStatic
                )
            );
        }

        return name;
    }

    private string ClaimOwnFastPath(IrMethodDeclaration overload)
    {
        var candidate = CandidateOf(overload);

        var claimed = DartFreeName.FirstFree(
            candidate,
            name => IsTakenForFastPath(name, overload.IsStatic)
        );

        if (claimed != candidate)
        {
            ReportRename(
                $"Fast path '{TypeName}.{candidate}' of '{overload.SignatureText}'",
                candidate,
                claimed,
                new OverloadDeclarationSite(overload.Name, Signature: overload.SignatureText)
            );
        }

        return claimed;
    }

    private bool IsTakenForFastPath(string name, bool isStatic) =>
        _table.GetValueOrDefault(name) is { } holder && holder.ConflictsWithMember(isStatic)
        || Inherited.ContainsKey(name);

    private void Record(IrMethodDeclaration overload, string fastPath)
    {
        _fastPaths[DartOverloadKey.Of(overload)] = fastPath;

        _table.TryAdd(
            fastPath,
            new DartClaimedMember(
                DartClaimedMemberKind.FastPath,
                $"the fast path of '{overload.SignatureText}'",
                TypeName,
                overload.IsStatic
            )
        );
    }

    /// <summary>
    /// <c>MS0067</c>. Its reason — a user member, an earlier generated name or
    /// an inherited member — is the holder the message names.
    /// </summary>
    private void ReportRename(
        string subject,
        string candidate,
        string claimed,
        OverloadDeclarationSite site
    )
    {
        var holder = _table.GetValueOrDefault(candidate) ?? Inherited[candidate];

        _findings.Add(
            new DartClaimFinding(
                MetanoDiagnosticSeverity.Warning,
                DiagnosticCodes.OverloadNameShadowed,
                $"Dart target: {subject} collides with {DescriptionOf(holder)}; it is emitted as "
                    + $"'{claimed}' (spec 013 FR-004).",
                site
            )
        );
    }

    private static string CandidateOf(IrMethodDeclaration overload) =>
        IrToDartNamingPolicy.ToFastPathName(overload.Name, overload.OverloadName!.Suffix);

    private string DescriptionOf(DartClaimedMember holder) =>
        holder.DeclaredBy == TypeName
            ? holder.Description
            : $"{holder.Description} inherited from '{holder.DeclaredBy}'";

    /// <summary>The inherited members under the class's own, named constructors excluded.</summary>
    private Dictionary<string, DartClaimedMember> InheritableMembers()
    {
        var members = new Dictionary<string, DartClaimedMember>(Inherited, StringComparer.Ordinal);

        foreach (var (name, member) in _table)
        {
            if (member.Kind != DartClaimedMemberKind.NamedConstructor)
                members[name] = member;
        }

        return members;
    }
}

/// <summary>
/// What the claim pass of one class reads: its IR (shape only), its Dart
/// name, how Dart emits its overload groups
/// (<see cref="DartTypeShapes.ShapeOf"/>), the members it
/// inherits from its emitted base, and the emitted name of a root overload
/// (<c>null</c> when the transform cannot see its declaring type).
/// </summary>
internal sealed record DartClassClaimInput(
    IrClassDeclaration Ir,
    string TypeName,
    DartOverloadShape Shape,
    IReadOnlyDictionary<string, DartClaimedMember> InheritedMembers,
    Func<IrOverloadRef, string?> RootFastPathOf
);
