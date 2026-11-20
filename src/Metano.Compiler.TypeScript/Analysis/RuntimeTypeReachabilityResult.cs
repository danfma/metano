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

    /// <summary>
    /// The runtime collection classes whose constructions pass their type
    /// arguments (spec 011 FR-008, D31): the classes a test, <c>as</c>,
    /// <c>GetType()</c> or <c>typeof</c> names, and those implementing a
    /// questioned interface.
    /// </summary>
    public IReadOnlySet<IrCollectionKind> QuestionedCollectionClasses { get; init; } =
        new HashSet<IrCollectionKind>();

    /// <summary>
    /// The collection-interface tests that report <c>MS0001</c> because a type that
    /// is not a runtime collection (or a runtime class with another shape) can be
    /// the operand (W4-17, D31).
    /// </summary>
    public IReadOnlySet<CollectionTestKey> UnanswerableCollectionTests { get; init; } =
        new HashSet<CollectionTestKey>();

    /// <summary>
    /// The types whose synthesized structural guard the compilation emits: the
    /// <c>[PlainObject]</c> types a runtime test recognizes by it (FR-020) and,
    /// once the transform merges them, the structural JSON variants (spec 009
    /// FR-019). The bridge renders no other synthesized guard.
    /// </summary>
    public required IReadOnlySet<SupertypeKey> GuardedPlainObjects { get; init; }

    /// <summary>
    /// The <c>[PlainObject]</c> types whose <c>GetType()</c> folds to their
    /// descriptor: no other plain object derives from them (FR-020).
    /// </summary>
    public required IReadOnlySet<SupertypeKey> FoldedPlainObjects { get; init; }

    /// <summary>
    /// The <c>[PlainObject]</c> implementers this compilation declares for a
    /// tested interface of a referenced library, which the test site augments
    /// the library descriptor with (<c>withImplementers</c>, FR-019).
    /// </summary>
    public required IReadOnlyDictionary<
        SupertypeKey,
        IReadOnlyList<SupertypeKey>
    > LibraryPlainImplementers { get; init; }

    /// <summary>The reached classes whose getter overrides one their base declares.</summary>
    public required IReadOnlySet<SupertypeKey> OverridingGetters { get; init; }

    /// <summary>
    /// The named types a <c>typeof</c> names that have no descriptor in
    /// TypeScript (reported as <c>MS0001</c> by the analysis).
    /// </summary>
    public required IReadOnlySet<SupertypeKey> Undescribable { get; init; }

    /// <summary>
    /// Whether enum boxing conversions box (spec 011 FR-050): the assembly does
    /// not carry <c>[assembly: DisableBoxing]</c>. Type questions on a value that
    /// can hold a box then test for one.
    /// </summary>
    public bool BoxingEnabled { get; init; }

    /// <summary>
    /// Whether the compilation boxes at least one value (a boxing conversion of
    /// an enum with a descriptor, boxing on): the gate of the interop
    /// <c>unbox</c> (FR-051).
    /// </summary>
    public bool HasBoxingConversion { get; init; }

    /// <summary>Whether interop boundaries unbox (FR-051).</summary>
    public bool UnboxesAtInterop => BoxingEnabled && HasBoxingConversion;

    /// <summary>A result that reaches nothing.</summary>
    public static RuntimeTypeReachabilityResult Empty { get; } =
        new()
        {
            Reached = new HashSet<SupertypeKey>(),
            GuardedPlainObjects = new HashSet<SupertypeKey>(),
            FoldedPlainObjects = new HashSet<SupertypeKey>(),
            LibraryPlainImplementers = new Dictionary<SupertypeKey, IReadOnlyList<SupertypeKey>>(),
            PlainImplementers = new Dictionary<SupertypeKey, IReadOnlyList<SupertypeKey>>(),
            QuestionedCollectionTypes = new HashSet<string>(StringComparer.Ordinal),
            OverridingGetters = new HashSet<SupertypeKey>(),
            Undescribable = new HashSet<SupertypeKey>(),
        };

    /// <summary>
    /// A result for code that renders no output (an ambiguity analysis asking
    /// whether a type has a check at all): it reaches every type, so it never
    /// fails the drift assertion. Never a group's result.
    /// </summary>
    public static RuntimeTypeReachabilityResult Unchecked { get; } =
        Empty with
        {
            ReachesEverything = true,
        };

    /// <summary>Whether every type counts as reached (<see cref="Unchecked"/>).</summary>
    private bool ReachesEverything { get; init; }

    /// <summary>Whether this is <see cref="Unchecked"/>.</summary>
    public bool IsUnchecked => ReachesEverything;

    /// <summary>Whether a construction of <paramref name="kind"/> carries its type arguments.</summary>
    public bool AsksAbout(IrCollectionKind kind) =>
        ReachesEverything || QuestionedCollectionClasses.Contains(kind);

    /// <summary>Whether the test of <paramref name="tested"/> reports MS0001.</summary>
    public bool IsUnanswerable(IrCollectionKind tested, IrTypeRef? operand) =>
        UnanswerableCollectionTests.Contains(CollectionTestKey.From(tested, OperandKey(operand)));

    private static string OperandKey(IrTypeRef? operand) =>
        operand switch
        {
            IrNullableTypeRef nullable => OperandKey(nullable.Inner),
            IrArrayTypeRef => CollectionTestKey.ArrayOperand,
            IrCollectionTypeRef collection => CollectionTestKey.OperandOfCollection(
                collection.Kind
            ),
            _ => CollectionTestKey.OpenOperand,
        };

    /// <summary>Whether the descriptor of <paramref name="key"/> is emitted.</summary>
    public bool Reaches(SupertypeKey key) => ReachesEverything || Reached.Contains(key);

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
                    .. GuardedPlainObjects
                        .Select(key => $"guarded:{Display(key)}")
                        .Order(StringComparer.Ordinal),
                    .. FoldedPlainObjects
                        .Select(key => $"folded:{Display(key)}")
                        .Order(StringComparer.Ordinal),
                    .. LibraryPlainImplementers
                        .Select(entry =>
                            $"library:{Display(entry.Key)}={string.Join(",", entry.Value.Select(Display))}"
                        )
                        .Order(StringComparer.Ordinal),
                    .. QuestionedCollectionTypes
                        .Select(name => $"collection:{name}")
                        .Order(StringComparer.Ordinal),
                    .. QuestionedCollectionClasses
                        .Select(kind => $"questioned:{kind}")
                        .Order(StringComparer.Ordinal),
                    .. UnanswerableCollectionTests
                        .Select(key => $"unanswerable:{key}")
                        .Order(StringComparer.Ordinal),
                    .. OverridingGetters
                        .Select(key => $"override:{Display(key)}")
                        .Order(StringComparer.Ordinal),
                    .. Undescribable
                        .Select(key => $"undescribable:{Display(key)}")
                        .Order(StringComparer.Ordinal),
                    $"boxing:{BoxingEnabled}",
                    $"boxes:{HasBoxingConversion}",
                ]
            )
        );

    private static string Display(SupertypeKey key) => $"{key.Namespace}.{key.Name}";
}
