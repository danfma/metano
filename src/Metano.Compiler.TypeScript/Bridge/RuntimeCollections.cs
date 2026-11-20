using Metano.Compiler.IR;
using Metano.Compiler.TypeScript.AST;

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
        [IrCollectionKind.ICollection] =
        [
            IrCollectionKind.List,
            IrCollectionKind.HashSet,
            IrCollectionKind.Dictionary,
        ],
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

    private static readonly IReadOnlyDictionary<IrCollectionKind, string> ClrNamesByKind =
        new Dictionary<IrCollectionKind, string>
        {
            [IrCollectionKind.List] = "List`1",
            [IrCollectionKind.IList] = "IList`1",
            [IrCollectionKind.ICollection] = "ICollection`1",
            [IrCollectionKind.IReadOnlyList] = "IReadOnlyList`1",
            [IrCollectionKind.IReadOnlyCollection] = "IReadOnlyCollection`1",
            [IrCollectionKind.Dictionary] = "Dictionary`2",
            [IrCollectionKind.IDictionary] = "IDictionary`2",
            [IrCollectionKind.IReadOnlyDictionary] = "IReadOnlyDictionary`2",
            [IrCollectionKind.HashSet] = "HashSet`1",
            [IrCollectionKind.ISet] = "ISet`1",
            [IrCollectionKind.IReadOnlySet] = "IReadOnlySet`1",
            [IrCollectionKind.Queue] = "Queue`1",
            [IrCollectionKind.Stack] = "Stack`1",
        };

    /// <summary>
    /// The kinds whose runtime class lists a mutable collection interface in its
    /// descriptor (as .NET does) without implementing its members: <c>add</c>
    /// returns a new collection (stage 011h's immutable classes). A test against
    /// such an interface is unanswerable like one against a user implementer.
    /// </summary>
    private static readonly IReadOnlySet<IrCollectionKind> KindsWithoutMutableShape =
        new HashSet<IrCollectionKind>();

    private static readonly IReadOnlyDictionary<IrCollectionKind, string> FactoriesByKind =
        new Dictionary<IrCollectionKind, string>
        {
            [IrCollectionKind.List] = "listType",
            [IrCollectionKind.IList] = "iListType",
            [IrCollectionKind.ICollection] = "iCollectionType",
            [IrCollectionKind.IReadOnlyList] = "iReadOnlyListType",
            [IrCollectionKind.IReadOnlyCollection] = "iReadOnlyCollectionType",
            [IrCollectionKind.Dictionary] = "dictionaryType",
            [IrCollectionKind.IDictionary] = "iDictionaryType",
            [IrCollectionKind.IReadOnlyDictionary] = "iReadOnlyDictionaryType",
            [IrCollectionKind.HashSet] = "hashSetType",
            [IrCollectionKind.ISet] = "iSetType",
            [IrCollectionKind.IReadOnlySet] = "iReadOnlySetType",
            [IrCollectionKind.Queue] = "queueType",
            [IrCollectionKind.Stack] = "stackType",
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
        "asList",
        "elementEquals",
        "defaultCompare",
        "createComparer",
        "defaultComparer",
        "defaultEqualityComparer",
        "Span",
    };

    /// <summary>
    /// The simple names <see cref="IsCollectionTypeName"/> matches. The
    /// dictionary views are nested types, so their full names reach the check
    /// through their declaring <c>Dictionary</c>.
    /// </summary>
    private static readonly IReadOnlySet<string> CollectionTypeNames = new HashSet<string>(
        NamesByKind.Values,
        StringComparer.Ordinal
    );

    private static readonly IReadOnlySet<string> AllNames = new HashSet<string>(
        NamesByKind.Values.Concat(HelperNames).Concat(FactoriesByKind.Values),
        StringComparer.Ordinal
    );

    /// <summary>The CLR definition name (<c>System.Collections.Generic.List`1</c>) of <paramref name="kind"/>.</summary>
    public static IrCollectionKind ArrayBackedKindOf(IrCollectionKind kind) =>
        kind
            is IrCollectionKind.IList
                or IrCollectionKind.ICollection
                or IrCollectionKind.IReadOnlyList
                or IrCollectionKind.IReadOnlyCollection
            ? IrCollectionKind.List
            : kind;

    public static string ClrNameOf(IrCollectionKind kind) =>
        $"System.Collections.Generic.{ClrNamesByKind[kind]}";

    /// <summary>The CLR definition name of <paramref name="kind"/>, <c>null</c> for a kind with none (a dictionary view).</summary>
    public static string? TryClrNameOf(IrCollectionKind kind) =>
        ClrNamesByKind.ContainsKey(kind) ? ClrNameOf(kind) : null;

    /// <summary>The kind a CLR definition name names, <c>null</c> for any other type.</summary>
    public static IrCollectionKind? KindOfClrName(string clrName) =>
        ClrNamesByKind
            .Where(entry => ClrNameOf(entry.Key) == clrName)
            .Select(entry => (IrCollectionKind?)entry.Key)
            .FirstOrDefault();

    /// <summary>The collection interfaces the runtime class <paramref name="kind"/> implements.</summary>
    public static IReadOnlyList<IrCollectionKind> InterfacesImplementedBy(IrCollectionKind kind) =>
        [
            .. ImplementersByInterface
                .Where(entry => entry.Value.Contains(kind))
                .Select(entry => entry.Key),
        ];

    /// <summary>
    /// Whether the TypeScript shape of the runtime class <paramref name="kind"/>
    /// implements the mutable collection interfaces its descriptor lists
    /// (<c>false</c> for stage 011h's immutable classes).
    /// </summary>
    public static bool ShapeImplementsMutableInterface(IrCollectionKind kind) =>
        !KindsWithoutMutableShape.Contains(kind);

    /// <summary>
    /// Whether a native array implements <paramref name="kind"/> in .NET
    /// (<c>IList</c>, <c>ICollection</c>, <c>IReadOnlyList</c>, <c>IReadOnlyCollection</c>),
    /// which no collection descriptor answers for.
    /// </summary>
    public static bool IsImplementedByArrays(IrCollectionKind kind) =>
        kind is not IrCollectionKind.List && ArrayBackedKindOf(kind) == IrCollectionKind.List;

    /// <summary>
    /// Whether the dictionary views (<c>Keys</c> / <c>Values</c>) implement
    /// <paramref name="kind"/> in .NET; they have no collection descriptor.
    /// </summary>
    public static bool IsImplementedByDictionaryViews(IrCollectionKind kind) =>
        kind is IrCollectionKind.ICollection or IrCollectionKind.IReadOnlyCollection;

    /// <summary>The runtime class kinds (every kind that is not an interface).</summary>
    public static IEnumerable<IrCollectionKind> ClassKinds() =>
        ClrNamesByKind.Keys.Where(kind => !IsInterface(kind));

    /// <summary>
    /// The descriptor factory (<c>listType</c>, <c>iListType</c>, …) of
    /// <paramref name="kind"/> in the collections barrel, <c>null</c> when it has none.
    /// </summary>
    public static TsRuntimeHelperReference? DescriptorFactoryOf(IrCollectionKind kind) =>
        FactoriesByKind.TryGetValue(kind, out var name) ? new(name, Module) : null;

    /// <summary>The runtime class or interface name of <paramref name="kind"/>.</summary>
    public static string NameOf(IrCollectionKind kind) => NamesByKind[kind];

    /// <summary>The value reference to the runtime class of <paramref name="kind"/>.</summary>
    public static TsRuntimeHelperReference ReferenceTo(IrCollectionKind kind) =>
        new(NameOf(kind), Module);

    /// <summary>
    /// The value reference to <paramref name="name"/>, one of the collections
    /// barrel's helpers (<c>fromEnd</c>, <c>throwModified</c>, <c>asList</c>, ...).
    /// </summary>
    public static TsRuntimeHelperReference HelperReference(string name) =>
        HelperNames.Contains(name)
            ? new TsRuntimeHelperReference(name, Module)
            : throw new ArgumentException($"'{name}' is not a collections helper.", nameof(name));

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
