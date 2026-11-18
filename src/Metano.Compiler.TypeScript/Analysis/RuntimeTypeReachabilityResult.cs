using Metano.Compiler.IR;
using Metano.Compiler.TypeScript.Caching;

namespace Metano.Compiler.TypeScript.Analysis;

/// <summary>
/// Which runtime type descriptors the TypeScript output declares (spec 011
/// FR-027, D29), computed once per transform by
/// <see cref="RuntimeTypeReachability"/> before any file group is lowered.
/// </summary>
internal sealed record RuntimeTypeReachabilityResult
{
    /// <summary>The types of this compilation whose descriptor is emitted.</summary>
    public required IReadOnlySet<SupertypeKey> Reached { get; init; }

    /// <summary>
    /// The <c>[PlainObject]</c> implementers of each reached interface, in
    /// declaration order (OD-8).
    /// </summary>
    public required IReadOnlyDictionary<
        SupertypeKey,
        IReadOnlyList<SupertypeKey>
    > PlainImplementers { get; init; }

    /// <summary>The collection definitions a runtime question names (FR-008, stage 011c).</summary>
    public required IReadOnlySet<string> QuestionedCollectionTypes { get; init; }

    /// <summary>The reached classes whose getter overrides one their base declares.</summary>
    public required IReadOnlySet<SupertypeKey> OverridingGetters { get; init; }

    /// <summary>
    /// The named types a <c>typeof</c> names that have no descriptor in
    /// TypeScript (reported as <c>MS0001</c> by the analysis).
    /// </summary>
    public required IReadOnlySet<SupertypeKey> Undescribable { get; init; }

    /// <summary>A result that reaches nothing.</summary>
    public static RuntimeTypeReachabilityResult Empty { get; } =
        new()
        {
            Reached = new HashSet<SupertypeKey>(),
            PlainImplementers = new Dictionary<SupertypeKey, IReadOnlyList<SupertypeKey>>(),
            QuestionedCollectionTypes = new HashSet<string>(StringComparer.Ordinal),
            OverridingGetters = new HashSet<SupertypeKey>(),
            Undescribable = new HashSet<SupertypeKey>(),
        };

    /// <summary>Whether the descriptor of <paramref name="key"/> is emitted.</summary>
    public bool Reaches(SupertypeKey key) => Reached.Contains(key);

    /// <summary>
    /// The hash of the whole result, which every group-cache key folds in: a
    /// change anywhere (a type newly reached, an implementer order, a getter
    /// override) re-transforms every group.
    /// </summary>
    public string Fingerprint() =>
        FileMetadataExtractor.Sha256(
            string.Join(
                "\n",
                [
                    .. Reached
                        .Select(key => $"reached:{Display(key)}")
                        .Order(StringComparer.Ordinal),
                    .. PlainImplementers
                        .Select(entry =>
                            $"plain:{Display(entry.Key)}={string.Join(",", entry.Value.Select(Display))}"
                        )
                        .Order(StringComparer.Ordinal),
                    .. QuestionedCollectionTypes
                        .Select(name => $"collection:{name}")
                        .Order(StringComparer.Ordinal),
                    .. OverridingGetters
                        .Select(key => $"override:{Display(key)}")
                        .Order(StringComparer.Ordinal),
                    .. Undescribable
                        .Select(key => $"undescribable:{Display(key)}")
                        .Order(StringComparer.Ordinal),
                ]
            )
        );

    private static string Display(SupertypeKey key) => $"{key.Namespace}.{key.Name}";
}
