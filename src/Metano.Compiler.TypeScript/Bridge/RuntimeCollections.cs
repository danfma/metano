using Metano.Compiler.IR;
using Metano.Compiler.TypeScript.AST;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// The <c>metano-runtime/system/collections</c> surface the TypeScript target
/// emits against (spec 011 contracts/runtime-collections.md, stages 011c and
/// 011h): the runtime class or interface of each <see cref="IrCollectionKind"/>,
/// which runtime classes implement each interface (W4-18: the one table the
/// type tests and the overload dispatchers share, in agreement with the
/// <c>interfaces</c> of each class literal; the dispatchers leave out the
/// implementers whose shape does not implement a mutable interface, W4-66),
/// and the module every collection name is imported from.
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
            [IrCollectionKind.ReadOnlyCollection] = "ReadOnlyList",
            [IrCollectionKind.ImmutableList] = "ImmutableList",
            [IrCollectionKind.ImmutableArray] = "ImmutableArray",
            [IrCollectionKind.ImmutableHashSet] = "ImmutableHashSet",
            [IrCollectionKind.ImmutableDictionary] = "ImmutableDictionary",
            [IrCollectionKind.IImmutableList] = "IImmutableList",
            [IrCollectionKind.IImmutableSet] = "IImmutableSet",
            [IrCollectionKind.IImmutableDictionary] = "IImmutableDictionary",
            [IrCollectionKind.ImmutableListBuilder] = "ImmutableListBuilder",
            [IrCollectionKind.ImmutableArrayBuilder] = "ImmutableArrayBuilder",
            [IrCollectionKind.ImmutableHashSetBuilder] = "ImmutableHashSetBuilder",
            [IrCollectionKind.ImmutableDictionaryBuilder] = "ImmutableDictionaryBuilder",
            [IrCollectionKind.SortedSet] = "SortedSet",
            [IrCollectionKind.SortedDictionary] = "SortedDictionary",
            [IrCollectionKind.SortedList] = "SortedList",
            [IrCollectionKind.SortedListView] = "SortedListView",
            [IrCollectionKind.ConcurrentDictionary] = "ConcurrentDictionary",
        };

    /// <summary>
    /// The runtime classes implementing each collection interface, as .NET 10
    /// declares them (contracts/runtime-collections.md § Type tests and
    /// dispatchers): the rows agree with the <c>interfaces</c> of each class
    /// literal whose arguments are the class's own.
    /// </summary>
    private static readonly IReadOnlyDictionary<
        IrCollectionKind,
        IReadOnlyList<IrCollectionKind>
    > ImplementersByInterface = new Dictionary<IrCollectionKind, IReadOnlyList<IrCollectionKind>>
    {
        [IrCollectionKind.IList] =
        [
            IrCollectionKind.List,
            IrCollectionKind.ReadOnlyCollection,
            IrCollectionKind.SortedListView,
            IrCollectionKind.ImmutableListBuilder,
            IrCollectionKind.ImmutableArrayBuilder,
            IrCollectionKind.ImmutableList,
            IrCollectionKind.ImmutableArray,
        ],
        [IrCollectionKind.IReadOnlyList] =
        [
            IrCollectionKind.List,
            IrCollectionKind.ReadOnlyCollection,
            IrCollectionKind.ImmutableList,
            IrCollectionKind.ImmutableArray,
            IrCollectionKind.ImmutableListBuilder,
            IrCollectionKind.ImmutableArrayBuilder,
        ],
        [IrCollectionKind.ICollection] =
        [
            IrCollectionKind.List,
            IrCollectionKind.HashSet,
            IrCollectionKind.Dictionary,
            IrCollectionKind.ReadOnlyCollection,
            IrCollectionKind.SortedSet,
            IrCollectionKind.SortedListView,
            IrCollectionKind.ImmutableListBuilder,
            IrCollectionKind.ImmutableArrayBuilder,
            IrCollectionKind.ImmutableHashSetBuilder,
            IrCollectionKind.ImmutableList,
            IrCollectionKind.ImmutableArray,
            IrCollectionKind.ImmutableHashSet,
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
            IrCollectionKind.ReadOnlyCollection,
            IrCollectionKind.ImmutableList,
            IrCollectionKind.ImmutableArray,
            IrCollectionKind.ImmutableHashSet,
            IrCollectionKind.ImmutableDictionary,
            IrCollectionKind.ImmutableListBuilder,
            IrCollectionKind.ImmutableArrayBuilder,
            IrCollectionKind.ImmutableHashSetBuilder,
            IrCollectionKind.ImmutableDictionaryBuilder,
            IrCollectionKind.SortedSet,
            IrCollectionKind.SortedDictionary,
            IrCollectionKind.SortedList,
            IrCollectionKind.ConcurrentDictionary,
        ],
        [IrCollectionKind.IDictionary] =
        [
            IrCollectionKind.Dictionary,
            IrCollectionKind.SortedDictionary,
            IrCollectionKind.SortedList,
            IrCollectionKind.ConcurrentDictionary,
            IrCollectionKind.ImmutableDictionaryBuilder,
            IrCollectionKind.ImmutableDictionary,
        ],
        [IrCollectionKind.IReadOnlyDictionary] =
        [
            IrCollectionKind.Dictionary,
            IrCollectionKind.SortedDictionary,
            IrCollectionKind.SortedList,
            IrCollectionKind.ConcurrentDictionary,
            IrCollectionKind.ImmutableDictionaryBuilder,
            IrCollectionKind.ImmutableDictionary,
        ],
        [IrCollectionKind.ISet] =
        [
            IrCollectionKind.HashSet,
            IrCollectionKind.SortedSet,
            IrCollectionKind.ImmutableHashSetBuilder,
            IrCollectionKind.ImmutableHashSet,
        ],
        [IrCollectionKind.IReadOnlySet] =
        [
            IrCollectionKind.HashSet,
            IrCollectionKind.SortedSet,
            IrCollectionKind.ImmutableHashSet,
        ],
        [IrCollectionKind.IImmutableList] =
        [
            IrCollectionKind.ImmutableList,
            IrCollectionKind.ImmutableArray,
        ],
        [IrCollectionKind.IImmutableSet] = [IrCollectionKind.ImmutableHashSet],
        [IrCollectionKind.IImmutableDictionary] = [IrCollectionKind.ImmutableDictionary],
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
            [IrCollectionKind.ReadOnlyCollection] = "ReadOnlyCollection`1",
            [IrCollectionKind.ImmutableList] = "ImmutableList`1",
            [IrCollectionKind.ImmutableArray] = "ImmutableArray`1",
            [IrCollectionKind.ImmutableHashSet] = "ImmutableHashSet`1",
            [IrCollectionKind.ImmutableDictionary] = "ImmutableDictionary`2",
            [IrCollectionKind.IImmutableList] = "IImmutableList`1",
            [IrCollectionKind.IImmutableSet] = "IImmutableSet`1",
            [IrCollectionKind.IImmutableDictionary] = "IImmutableDictionary`2",
            [IrCollectionKind.ImmutableListBuilder] = "ImmutableList`1+Builder",
            [IrCollectionKind.ImmutableArrayBuilder] = "ImmutableArray`1+Builder",
            [IrCollectionKind.ImmutableHashSetBuilder] = "ImmutableHashSet`1+Builder",
            [IrCollectionKind.ImmutableDictionaryBuilder] = "ImmutableDictionary`2+Builder",
            [IrCollectionKind.SortedSet] = "SortedSet`1",
            [IrCollectionKind.SortedDictionary] = "SortedDictionary`2",
            [IrCollectionKind.SortedList] = "SortedList`2",
            [IrCollectionKind.ConcurrentDictionary] = "ConcurrentDictionary`2",
        };

    /// <summary>
    /// The descriptor factory for every supported collection class and interface,
    /// including nested builders whose arguments belong to their declaring type.
    /// </summary>
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
            [IrCollectionKind.ReadOnlyCollection] = "readOnlyListType",
            [IrCollectionKind.ImmutableList] = "immutableListType",
            [IrCollectionKind.ImmutableArray] = "immutableArrayType",
            [IrCollectionKind.ImmutableHashSet] = "immutableHashSetType",
            [IrCollectionKind.ImmutableDictionary] = "immutableDictionaryType",
            [IrCollectionKind.IImmutableList] = "iImmutableListType",
            [IrCollectionKind.IImmutableSet] = "iImmutableSetType",
            [IrCollectionKind.IImmutableDictionary] = "iImmutableDictionaryType",
            [IrCollectionKind.ImmutableListBuilder] = "immutableListBuilderType",
            [IrCollectionKind.ImmutableArrayBuilder] = "immutableArrayBuilderType",
            [IrCollectionKind.ImmutableHashSetBuilder] = "immutableHashSetBuilderType",
            [IrCollectionKind.ImmutableDictionaryBuilder] = "immutableDictionaryBuilderType",
            [IrCollectionKind.SortedSet] = "sortedSetType",
            [IrCollectionKind.SortedDictionary] = "sortedDictionaryType",
            [IrCollectionKind.SortedList] = "sortedListType",
            [IrCollectionKind.ConcurrentDictionary] = "concurrentDictionaryType",
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
        "asReadOnlyJsArray",
        "asSpan",
        "asReadOnlySpan",
        "asList",
        "elementEquals",
        "defaultCompare",
        "createComparer",
        "defaultComparer",
        "defaultEqualityComparer",
        "IComparer",
        "IEqualityComparer",
        "Span",
        "asReadOnly",
        "toImmutableList",
        "toImmutableArray",
        "toImmutableHashSet",
        "toImmutableDictionary",
    };

    /// <summary>
    /// The simple names <see cref="IsCollectionTypeName"/> matches. The
    /// dictionary views are nested types, so their full names reach the check
    /// through their declaring <c>Dictionary</c>.
    /// </summary>
    private static readonly IReadOnlySet<string> CollectionTypeNames = new HashSet<string>(
        Enum.GetValues<IrCollectionKind>().Select(OriginSimpleNameOf),
        StringComparer.Ordinal
    );

    /// <summary>The namespaces the collections of <see cref="CollectionTypeNames"/> live in.</summary>
    private static readonly IReadOnlyList<string> CollectionNamespaces =
    [
        "System.Collections.Generic.",
        "System.Collections.Immutable.",
        "System.Collections.ObjectModel.",
        "System.Collections.Concurrent.",
    ];

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
        $"{ClrNamespaceOf(kind)}.{ClrNamesByKind[kind]}";

    private static string ClrNamespaceOf(IrCollectionKind kind) =>
        kind switch
        {
            IrCollectionKind.ReadOnlyCollection => "System.Collections.ObjectModel",
            IrCollectionKind.ConcurrentDictionary => "System.Collections.Concurrent",
            IrCollectionKind.IImmutableList
            or IrCollectionKind.IImmutableSet
            or IrCollectionKind.IImmutableDictionary => "System.Collections.Immutable",
            _ when IrCollectionKinds.IsImmutable(kind)
                    || kind
                        is IrCollectionKind.ImmutableListBuilder
                            or IrCollectionKind.ImmutableArrayBuilder
                            or IrCollectionKind.ImmutableHashSetBuilder
                            or IrCollectionKind.ImmutableDictionaryBuilder =>
                "System.Collections.Immutable",
            _ => "System.Collections.Generic",
        };

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

    /// <summary>
    /// The .NET simple name of the type of <paramref name="kind"/> (a builder
    /// is named through its immutable container, whose members it shares the
    /// origin prefix of).
    /// </summary>
    private static string OriginSimpleNameOf(IrCollectionKind kind) =>
        kind switch
        {
            IrCollectionKind.ReadOnlyCollection => "ReadOnlyCollection",
            IrCollectionKind.ImmutableListBuilder => "ImmutableList",
            IrCollectionKind.ImmutableArrayBuilder => "ImmutableArray",
            IrCollectionKind.ImmutableHashSetBuilder => "ImmutableHashSet",
            IrCollectionKind.ImmutableDictionaryBuilder => "ImmutableDictionary",
            _ => kind.ToString(),
        };

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
    public static bool IsKeyed(IrCollectionKind kind) => IrCollectionKinds.IsKeyed(kind);

    /// <summary>
    /// The runtime classes whose instances a value of <paramref name="kind"/>
    /// can be: the class itself, or every runtime class implementing the
    /// interface (a type test's answer, as .NET's).
    /// </summary>
    public static IReadOnlyList<IrCollectionKind> ClassesOf(IrCollectionKind kind) =>
        ImplementersByInterface.TryGetValue(kind, out var implementers) ? implementers : [kind];

    /// <summary>
    /// The runtime classes an overload dispatcher routes to a parameter of
    /// <paramref name="kind"/>: <see cref="ClassesOf"/> without the classes
    /// whose shape does not implement the interface (W4-66: a dispatcher never
    /// routes an immutable value to an <c>IList&lt;T&gt;</c> overload).
    /// </summary>
    public static IReadOnlyList<IrCollectionKind> DispatchClassesOf(IrCollectionKind kind) =>
        [
            .. ClassesOf(kind)
                .Where(implementer =>
                    !IrCollectionKinds.IsMutableInterface(kind)
                    || ShapeImplementsMutableInterface(implementer)
                ),
        ];

    /// <summary>
    /// Whether the TypeScript shape of the runtime class of
    /// <paramref name="kind"/> implements the mutable collection interfaces
    /// its .NET type declares (<c>IList&lt;T&gt;</c>, <c>ICollection&lt;T&gt;</c>,
    /// <c>ISet&lt;T&gt;</c>, <c>IDictionary&lt;K, V&gt;</c>): <c>false</c> for
    /// the immutable classes, whose mutators return a new value where the
    /// interface's mutate in place (W4-66), so a static conversion to those
    /// interfaces is reported and the dispatchers leave them out.
    /// </summary>
    public static bool ShapeImplementsMutableInterface(IrCollectionKind kind) =>
        !IrCollectionKinds.IsImmutable(kind);

    /// <summary>
    /// Whether <paramref name="fullName"/> (a member origin's declaring type)
    /// names a collection of spec 011 FR-001 / FR-054 or one of its nested
    /// views and builders.
    /// </summary>
    public static bool IsCollectionTypeName(string fullName) =>
        CollectionNamespaces.Any(prefix =>
            fullName.StartsWith(prefix, StringComparison.Ordinal)
            && CollectionTypeNames.Contains(SimpleNameOf(fullName[prefix.Length..]))
        );

    private static string SimpleNameOf(string name)
    {
        var end = name.IndexOfAny(['<', '`', '+', '.']);

        return end < 0 ? name : name[..end];
    }

    /// <summary>Whether <paramref name="name"/> is imported from <see cref="Module"/>.</summary>
    public static bool IsCollectionsName(string name) => AllNames.Contains(name);
}
