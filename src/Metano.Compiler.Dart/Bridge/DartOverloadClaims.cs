using Metano.Compiler.Diagnostics;
using Metano.Compiler.Extraction;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Dart.Bridge;

/// <summary>
/// The Dart names one type's claim pass gave its dispatched overloads (spec
/// 013 FR-022, DECIDED-14, DECIDED-18): a named constructor per constructor
/// overload (<c>Dimension.fromSize</c>) and a fast path per method overload
/// (<c>addXY</c>), plus the member table a derived class claims against and
/// the findings to report at the type's declarations.
/// </summary>
public sealed class DartOverloadClaims
{
    private readonly IReadOnlyDictionary<int, string> _constructors;
    private readonly IReadOnlyDictionary<DartOverloadKey, string> _fastPaths;

    internal DartOverloadClaims(
        DartOverloadShape shape,
        IReadOnlyDictionary<int, string> constructors,
        IReadOnlyDictionary<DartOverloadKey, string> fastPaths,
        IReadOnlyDictionary<string, DartClaimedMember> members,
        IReadOnlyList<DartClaimFinding> findings
    )
    {
        Shape = shape;
        _constructors = constructors;
        _fastPaths = fastPaths;
        Members = members;
        Findings = findings;
    }

    /// <summary>A type with no dispatched overload.</summary>
    public static DartOverloadClaims None { get; } =
        new(
            DartOverloadShape.None,
            new Dictionary<int, string>(),
            new Dictionary<DartOverloadKey, string>(),
            new Dictionary<string, DartClaimedMember>(StringComparer.Ordinal),
            []
        );

    /// <summary>
    /// The members a subclass inherits, by Dart name — inherited ones
    /// included, named constructors excluded (Dart does not inherit them).
    /// </summary>
    internal IReadOnlyDictionary<string, DartClaimedMember> Members { get; }

    internal IReadOnlyList<DartClaimFinding> Findings { get; }

    /// <summary>How Dart emits the type's overload groups (<see cref="DartTypeShapes.ShapeOf"/>).</summary>
    internal DartOverloadShape Shape { get; }

    /// <summary>
    /// The named constructor of the constructor overload at
    /// <paramref name="ordinal"/>; <c>null</c> outside a dispatched set.
    /// </summary>
    public string? ConstructorNameAt(int ordinal) => _constructors.GetValueOrDefault(ordinal);

    /// <summary>
    /// The fast path of the overload declared under
    /// <paramref name="memberName"/> at <paramref name="ordinal"/>;
    /// <c>null</c> when the type names no dispatched overload there.
    /// </summary>
    public string? FastPathAt(string memberName, int ordinal) =>
        _fastPaths.GetValueOrDefault(new DartOverloadKey(memberName, ordinal));

    /// <summary>
    /// The fast path of an overload the type declares. The claim pass runs
    /// over the same shape the declaration is emitted from, so a missing name
    /// is a broken invariant: a fallback would declare a name no call site
    /// resolves.
    /// </summary>
    internal string RequiredFastPathAt(string memberName, int ordinal) =>
        FastPathAt(memberName, ordinal)
        ?? throw new InvalidOperationException(
            $"The Dart claim pass named no fast path for '{memberName}' at ordinal {ordinal}."
        );

    /// <summary>The named constructor of a constructor overload of a dispatched set (see <see cref="RequiredFastPathAt"/>).</summary>
    internal string RequiredConstructorNameAt(int ordinal) =>
        ConstructorNameAt(ordinal)
        ?? throw new InvalidOperationException(
            $"The Dart claim pass named no constructor at ordinal {ordinal}."
        );

    /// <summary>Reports the findings, located at the declarations of <paramref name="type"/>.</summary>
    public void Report(INamedTypeSymbol type, Action<MetanoDiagnostic> report)
    {
        foreach (var finding in Findings)
        {
            report(
                new MetanoDiagnostic(
                    finding.Severity,
                    finding.Code,
                    finding.Message,
                    finding.Site.LocationIn(type)
                )
            );
        }
    }
}

/// <summary>
/// One overload of a type: its C# group name and its declaration ordinal there
/// (<see cref="IrOverloadRef"/>).
/// </summary>
internal sealed record DartOverloadKey(string MemberName, int Ordinal)
{
    public static DartOverloadKey Of(IrMethodDeclaration overload) =>
        new(overload.Name, overload.DeclarationOrdinal);

    public static DartOverloadKey Of(IrOverloadRef overload) =>
        new(overload.MemberName, overload.Ordinal);
}

/// <summary>What holds a name in a Dart member table.</summary>
internal enum DartClaimedMemberKind
{
    User,
    NamedConstructor,
    FastPath,
}

/// <summary>
/// A name held in a Dart member table: what holds it, described for a
/// message, the type that declares it, and whether it is a static member —
/// a named constructor conflicts with a static member of its class, never
/// with an instance one.
/// </summary>
internal sealed record DartClaimedMember(
    DartClaimedMemberKind Kind,
    string Description,
    string DeclaredBy,
    bool IsStatic
)
{
    /// <summary>
    /// Whether a named constructor of the same class cannot take this name:
    /// another named constructor or a static member holds it.
    /// </summary>
    public bool ConflictsWithConstructor =>
        IsStatic || Kind == DartClaimedMemberKind.NamedConstructor;

    /// <summary>
    /// Whether a member of the given staticness cannot take this name: any
    /// member holds it, except a named constructor next to an instance member.
    /// </summary>
    public bool ConflictsWithMember(bool isStatic) =>
        isStatic || Kind != DartClaimedMemberKind.NamedConstructor;
}

/// <summary>A diagnostic a claim pass found, and the declaration it is about.</summary>
internal sealed record DartClaimFinding(
    MetanoDiagnosticSeverity Severity,
    string Code,
    string Message,
    OverloadDeclarationSite Site
);

/// <summary>
/// How Dart moves a generated name out of the way of one already taken: it
/// appends <c>$</c>, a legal Dart identifier character, until the name is free
/// (spec 013 FR-004).
/// </summary>
internal static class DartFreeName
{
    private const string Mark = "$";

    public static string FirstFree(string candidate, Func<string, bool> isTaken)
    {
        var name = candidate;

        while (isTaken(name))
            name += Mark;

        return name;
    }
}
