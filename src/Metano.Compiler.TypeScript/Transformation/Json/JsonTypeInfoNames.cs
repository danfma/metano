using Metano.Compiler.Diagnostics;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.TypeScript.Transformation.Json;

/// <summary>
/// The single naming function of the generated context (FR-031): every
/// member name — root getters, <c>ref</c> descriptors and <c>base</c> links —
/// comes from <see cref="GetterOf"/>, so a link always resolves to the getter
/// of the type it names. Names follow the System.Text.Json <c>JsonTypeInfo</c>
/// property names: an explicit <c>TypeInfoPropertyName</c> wins for that type
/// everywhere in the context; a non-generic type uses its resolved TS name
/// (which honors <c>[Name]</c>); a closed generic type is its name without
/// arity followed by the names of its type arguments (<c>ListListInt32</c>);
/// an array is its element name + <c>Array</c>. Type arguments use this same
/// function recursively, so <c>List&lt;UserDto&gt;</c> with
/// <c>[Name("User")]</c> on <c>UserDto</c> is <c>ListUser</c> (System.Text.Json
/// says <c>ListUserDto</c>).
/// </summary>
internal sealed class JsonTypeInfoNames
{
    /// <summary>
    /// Instance members of the runtime <c>SerializerContext</c> (FR-036). The
    /// generated static <c>default</c> / <c>_default</c> are not reserved: an
    /// instance getter or field of the same name does not clash with them.
    /// </summary>
    private static readonly HashSet<string> ReservedMembers = new(StringComparer.Ordinal)
    {
        "options",
        "converters",
        "createSpec",
        "createValueSpec",
        "readOptions",
        "resolve",
        "_registry",
        "constructor",
    };

    private readonly TypeScriptTransformContext _context;
    private readonly IReadOnlyDictionary<ITypeSymbol, string> _overrides;
    private readonly Action<MetanoDiagnostic> _report;

    private JsonTypeInfoNames(
        TypeScriptTransformContext context,
        IReadOnlyDictionary<ITypeSymbol, string> overrides,
        Action<MetanoDiagnostic> report
    )
    {
        _context = context;
        _overrides = overrides;
        _report = report;
    }

    /// <summary>
    /// Builds the naming function from the roots' <c>TypeInfoPropertyName</c>
    /// overrides. When one type is listed more than once, the first override
    /// written for it wins and a later, different one is reported as an
    /// <c>MS0036</c> warning.
    /// </summary>
    public static JsonTypeInfoNames Create(
        TypeScriptTransformContext context,
        IReadOnlyList<JsonContractRoot> roots,
        Action<MetanoDiagnostic> report
    )
    {
        var overrides = new Dictionary<ITypeSymbol, string>(SymbolEqualityComparer.Default);

        foreach (var root in roots)
        {
            if (root.TypeInfoPropertyName is not { } name)
                continue;

            if (overrides.TryAdd(root.Type, name) || overrides[root.Type] == name)
                continue;

            report(
                new MetanoDiagnostic(
                    MetanoDiagnosticSeverity.Warning,
                    DiagnosticCodes.JsonTypeInfoNameCollision,
                    $"'{DisplayOf(root.Type)}' is listed again with TypeInfoPropertyName "
                        + $"'{name}'; the first TypeInfoPropertyName '{overrides[root.Type]}' "
                        + "was kept.",
                    root.Location
                )
            );
        }

        return new JsonTypeInfoNames(context, overrides, report);
    }

    /// <summary>The FR-031 name of <paramref name="type"/> (PascalCase).</summary>
    public string NameOf(ITypeSymbol type)
    {
        if (_overrides.TryGetValue(type, out var overridden))
            return overridden;

        return type switch
        {
            IArrayTypeSymbol array => NameOf(array.ElementType) + "Array",
            INamedTypeSymbol { TypeArguments.Length: > 0 } generic => generic.Name
                + string.Concat(generic.TypeArguments.Select(NameOf)),
            INamedTypeSymbol named => _context.ResolveTsName(named),
            _ => type.Name,
        };
    }

    /// <summary>The context getter that holds the type's spec
    /// (<c>get iReadOnlyListEpisodeSummary()</c>).</summary>
    public string GetterOf(ITypeSymbol type) => TypeScriptNaming.ToCamelCaseMember(NameOf(type));

    /// <summary>The getter's lazy backing field.</summary>
    public static string FieldOf(string getter) => "_" + getter;

    /// <summary>
    /// Assigns the context members of a final closure in discovery order and
    /// returns the entries to emit. The first type keeps a contested name
    /// (DECIDED-14). Reports <c>MS0036</c> (FR-036): an Error when the losing
    /// type is the target of a <c>ref</c> or <c>base</c> link, or when a name
    /// equals a reserved <c>SerializerContext</c> member; a Warning when the
    /// loser is an unreferenced root, which is then not emitted (as
    /// System.Text.Json's SYSLIB1031). Losers are never emitted.
    /// </summary>
    public IReadOnlyList<JsonContractEntry> Claim(JsonContract contract)
    {
        var owners = new Dictionary<string, ITypeSymbol>(StringComparer.Ordinal);
        var claimed = new List<JsonContractEntry>();

        foreach (var entry in contract.Entries)
        {
            var getter = GetterOf(entry.Type);

            if (ReservedMemberOf(getter) is { } reserved)
            {
                ReportReservedMember(entry, getter, reserved, contract);
                continue;
            }

            if (owners.TryAdd(getter, entry.Type))
            {
                claimed.Add(entry);
                continue;
            }

            ReportCollision(entry, owners[getter], getter, contract);
        }

        return claimed;
    }

    /// <summary>The <c>SerializerContext</c> member a getter or its field
    /// would shadow, or <c>null</c>.</summary>
    private static string? ReservedMemberOf(string getter) =>
        new[] { getter, FieldOf(getter) }.FirstOrDefault(ReservedMembers.Contains);

    private void ReportReservedMember(
        JsonContractEntry entry,
        string getter,
        string reserved,
        JsonContract contract
    )
    {
        var kind = reserved == getter ? "member" : "field";

        Report(
            MetanoDiagnosticSeverity.Error,
            $"'{DisplayOf(entry.Type)}' maps to context {kind} '{reserved}', which "
                + "SerializerContext already declares. Use TypeInfoPropertyName.",
            entry,
            contract
        );
    }

    private void ReportCollision(
        JsonContractEntry loser,
        ITypeSymbol winner,
        string getter,
        JsonContract contract
    )
    {
        var collision =
            $"'{DisplayOf(loser.Type)}' and '{DisplayOf(winner)}' both map to context member "
            + $"'{getter}'";

        if (contract.Referrers.TryGetValue(loser.Type, out var referrer))
        {
            Report(
                MetanoDiagnosticSeverity.Error,
                $"{collision}; '{DisplayOf(loser.Type)}' is referenced by "
                    + $"'{DisplayOf(referrer)}', so it cannot be dropped. Use "
                    + "TypeInfoPropertyName to resolve the collision.",
                loser,
                contract
            );

            return;
        }

        Report(
            MetanoDiagnosticSeverity.Warning,
            $"{collision}; '{DisplayOf(winner)}' was kept. Use TypeInfoPropertyName to "
                + "resolve the collision.",
            loser,
            contract
        );
    }

    /// <summary>
    /// Reports at the entry's own <c>[JsonSerializable]</c> attribute, or, for
    /// a discovered type, at the root it was reached from.
    /// </summary>
    private void Report(
        MetanoDiagnosticSeverity severity,
        string message,
        JsonContractEntry entry,
        JsonContract contract
    ) =>
        _report(
            new MetanoDiagnostic(
                severity,
                DiagnosticCodes.JsonTypeInfoNameCollision,
                message,
                LocationOf(entry, contract)
            )
        );

    private static Location? LocationOf(JsonContractEntry entry, JsonContract contract)
    {
        var current = entry.Type;
        var visited = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);

        while (visited.Add(current))
        {
            var root = contract
                .Entries.FirstOrDefault(candidate =>
                    SymbolEqualityComparer.Default.Equals(candidate.Type, current)
                )
                ?.Root;

            if (root is not null)
                return root.Location;

            if (!contract.Referrers.TryGetValue(current, out var referrer))
                break;

            current = referrer;
        }

        return entry.Type.Locations.FirstOrDefault();
    }

    private static string DisplayOf(ITypeSymbol type) =>
        type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
}
