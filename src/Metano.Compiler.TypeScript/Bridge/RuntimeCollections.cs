using Metano.Compiler.IR;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// The <c>metano-runtime/system/collections</c> surface the TypeScript target
/// emits against (spec 011 contracts/runtime-collections.md, stage 011c): the
/// runtime class or interface of each <see cref="IrCollectionKind"/>, which
/// runtime classes implement each interface (W4-18: the one table the type
/// tests and the overload dispatchers share; stage 011h adds its classes), and
/// the module every collection name is imported from.
/// </summary>
public static class RuntimeCollections
{
    /// <summary>The barrel generated code imports every collection name from.</summary>
    public const string Module = "metano-runtime/system/collections";

    private static readonly IReadOnlyDictionary<IrCollectionKind, string> NamesByKind =
        new Dictionary<IrCollectionKind, string>
        {
            [IrCollectionKind.List] = "List",
            [IrCollectionKind.IList] = "IList",
            [IrCollectionKind.ICollection] = "ICollection",
            [IrCollectionKind.IReadOnlyList] = "IReadOnlyList",
            [IrCollectionKind.IReadOnlyCollection] = "IReadOnlyCollection",
            [IrCollectionKind.Dictionary] = "Dictionary",
            [IrCollectionKind.IDictionary] = "IDictionary",
            [IrCollectionKind.IReadOnlyDictionary] = "IReadOnlyDictionary",
            [IrCollectionKind.HashSet] = "HashSet",
            [IrCollectionKind.ISet] = "ISet",
            [IrCollectionKind.IReadOnlySet] = "IReadOnlySet",
            [IrCollectionKind.Queue] = "Queue",
            [IrCollectionKind.Stack] = "Stack",
            [IrCollectionKind.KeyCollection] = "KeyCollection",
            [IrCollectionKind.ValueCollection] = "ValueCollection",
        };

    private static readonly IReadOnlyDictionary<
        IrCollectionKind,
        IReadOnlyList<IrCollectionKind>
    > ImplementersByInterface = new Dictionary<IrCollectionKind, IReadOnlyList<IrCollectionKind>>
    {
        [IrCollectionKind.IList] = [IrCollectionKind.List],
        [IrCollectionKind.IReadOnlyList] = [IrCollectionKind.List],
        [IrCollectionKind.ICollection] = [IrCollectionKind.List, IrCollectionKind.HashSet],
        [IrCollectionKind.IReadOnlyCollection] =
        [
            IrCollectionKind.List,
            IrCollectionKind.HashSet,
            IrCollectionKind.Queue,
            IrCollectionKind.Stack,
            IrCollectionKind.Dictionary,
            IrCollectionKind.KeyCollection,
            IrCollectionKind.ValueCollection,
        ],
        [IrCollectionKind.IDictionary] = [IrCollectionKind.Dictionary],
        [IrCollectionKind.IReadOnlyDictionary] = [IrCollectionKind.Dictionary],
        [IrCollectionKind.ISet] = [IrCollectionKind.HashSet],
        [IrCollectionKind.IReadOnlySet] = [IrCollectionKind.HashSet],
    };

    /// <summary>
    /// The runtime helpers of the collections barrel the target emits besides
    /// the classes and interfaces (contracts/runtime-collections.md § Helpers,
    /// § Cold throw helpers, § Equality and comparison).
    /// </summary>
    private static readonly IReadOnlySet<string> HelperNames = new HashSet<string>(
        StringComparer.Ordinal
    )
    {
        "throwModified",
        "fromEnd",
        "isIterable",
        "asArray",
        "asJsArray",
        "asSpan",
        "asReadOnlySpan",
        "elementEquals",
        "defaultCompare",
        "createComparer",
        "defaultComparer",
        "defaultEqualityComparer",
        "Span",
    };

    private static readonly IReadOnlySet<string> CollectionTypeNames = new HashSet<string>(
        StringComparer.Ordinal
    )
    {
        "List",
        "IList",
        "ICollection",
        "IReadOnlyList",
        "IReadOnlyCollection",
        "Dictionary",
        "IDictionary",
        "IReadOnlyDictionary",
        "HashSet",
        "ISet",
        "IReadOnlySet",
        "Queue",
        "Stack",
    };

    private static readonly IReadOnlySet<string> AllNames = new HashSet<string>(
        NamesByKind.Values.Concat(HelperNames),
        StringComparer.Ordinal
    );

    /// <summary>The runtime class or interface name of <paramref name="kind"/>.</summary>
    public static string NameOf(IrCollectionKind kind) => NamesByKind[kind];

    /// <summary>Whether <paramref name="kind"/> is a collection interface (no runtime value).</summary>
    public static bool IsInterface(IrCollectionKind kind) =>
        ImplementersByInterface.ContainsKey(kind);

    /// <summary>Whether elements of <paramref name="kind"/> are addressed by key (the dictionaries).</summary>
    public static bool IsKeyed(IrCollectionKind kind) =>
        kind
            is IrCollectionKind.Dictionary
                or IrCollectionKind.IDictionary
                or IrCollectionKind.IReadOnlyDictionary;

    /// <summary>
    /// The runtime classes whose instances a value of <paramref name="kind"/>
    /// can be: the class itself, or every 011c class implementing the interface.
    /// </summary>
    public static IReadOnlyList<IrCollectionKind> ClassesOf(IrCollectionKind kind) =>
        ImplementersByInterface.TryGetValue(kind, out var implementers) ? implementers : [kind];

    /// <summary>
    /// Whether <paramref name="fullName"/> (a member origin's declaring type)
    /// names a stage 011c collection or one of its dictionary views.
    /// </summary>
    public static bool IsCollectionTypeName(string fullName)
    {
        const string prefix = "System.Collections.Generic.";

        if (!fullName.StartsWith(prefix, StringComparison.Ordinal))
            return false;

        var simple = fullName[prefix.Length..];
        var end = simple.IndexOfAny(['<', '`', '+', '.']);

        return CollectionTypeNames.Contains(end < 0 ? simple : simple[..end]);
    }

    /// <summary>Whether <paramref name="name"/> is imported from <see cref="Module"/>.</summary>
    public static bool IsCollectionsName(string name) => AllNames.Contains(name);
}
