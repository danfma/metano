// Declarative BCL → JavaScript mappings for the System.Collections.Generic List family
// (spec 011 FR-007, stage 011c). Read at compile time by the Metano transpiler via
// DeclarativeMappingRegistry.
//
// A `List<T>` is the `metano-runtime` `List` class (contracts/runtime-collections.md), so
// every instance member is a plain rename to the camelCase member of the same name: no
// template names `$this` twice (FR-009). Constructors map to the class's factories with
// full arity, so no argument is dropped. The interfaces map to the members of the runtime
// interfaces of the same name; `IReadOnlyList<T>` / `IReadOnlyCollection<T>` declare only
// `Count` (and the indexer), their `Contains` is the LINQ extension.
//
// Each interface variant is declared explicitly because Roslyn resolves a method call
// against the symbol's most-specific containing type, and the registry compares against
// that type's OriginalDefinition. The C# member name is captured via `nameof` so a typo
// surfaces at compile time.
//
// ImmutableList<T> / ImmutableArray<T> and ReadOnlyCollection<T> are stage 011h's
// (ImmutableCollections.cs, ReadOnlyCollections.cs).

using Metano.Annotations;

// ─── Count ───────────────────────────────────────────────────────
[assembly: MapProperty(
    typeof(List<>),
    nameof(List<int>.Count),
    JsProperty = "count",
    DartProperty = "length"
)]
[assembly: MapProperty(
    typeof(IList<>),
    nameof(IList<int>.Count),
    JsProperty = "count",
    DartProperty = "length"
)]
[assembly: MapProperty(
    typeof(ICollection<>),
    nameof(ICollection<int>.Count),
    JsProperty = "count",
    DartProperty = "length"
)]
[assembly: MapProperty(
    typeof(IReadOnlyList<>),
    nameof(IReadOnlyList<int>.Count),
    JsProperty = "count",
    DartProperty = "length"
)]
[assembly: MapProperty(
    typeof(IReadOnlyCollection<>),
    nameof(IReadOnlyCollection<int>.Count),
    JsProperty = "count",
    DartProperty = "length"
)]

// ─── ICollection<T>.IsReadOnly ───────────────────────────────────
[assembly: MapProperty(
    typeof(ICollection<>),
    nameof(ICollection<int>.IsReadOnly),
    JsProperty = "isReadOnly"
)]

// ─── Add / AddRange / Insert / InsertRange ───────────────────────
[assembly: MapMethod(typeof(List<>), nameof(List<int>.Add), JsMethod = "add")]
[assembly: MapMethod(typeof(IList<>), nameof(IList<int>.Add), JsMethod = "add")]
[assembly: MapMethod(typeof(ICollection<>), nameof(ICollection<int>.Add), JsMethod = "add")]
[assembly: MapMethod(typeof(List<>), nameof(List<int>.AddRange), JsMethod = "addRange")]
[assembly: MapMethod(typeof(List<>), nameof(List<int>.Insert), JsMethod = "insert")]
[assembly: MapMethod(typeof(IList<>), nameof(IList<int>.Insert), JsMethod = "insert")]
[assembly: MapMethod(typeof(List<>), nameof(List<int>.InsertRange), JsMethod = "insertRange")]

// ─── Remove / RemoveAt / RemoveRange / RemoveAll / Clear ─────────
[assembly: MapMethod(typeof(List<>), nameof(List<int>.Remove), JsMethod = "remove")]
[assembly: MapMethod(typeof(ICollection<>), nameof(ICollection<int>.Remove), JsMethod = "remove")]
[assembly: MapMethod(typeof(List<>), nameof(List<int>.RemoveAt), JsMethod = "removeAt")]
[assembly: MapMethod(typeof(IList<>), nameof(IList<int>.RemoveAt), JsMethod = "removeAt")]
[assembly: MapMethod(typeof(List<>), nameof(List<int>.RemoveRange), JsMethod = "removeRange")]
[assembly: MapMethod(typeof(List<>), nameof(List<int>.RemoveAll), JsMethod = "removeAll")]
[assembly: MapMethod(typeof(List<>), nameof(List<int>.Clear), JsMethod = "clear")]
[assembly: MapMethod(typeof(IList<>), nameof(IList<int>.Clear), JsMethod = "clear")]
[assembly: MapMethod(typeof(ICollection<>), nameof(ICollection<int>.Clear), JsMethod = "clear")]

// ─── Searches (elementEquals in the runtime) ─────────────────────
[assembly: MapMethod(typeof(List<>), nameof(List<int>.Contains), JsMethod = "contains")]
[assembly: MapMethod(typeof(IList<>), nameof(IList<int>.Contains), JsMethod = "contains")]
[assembly: MapMethod(
    typeof(ICollection<>),
    nameof(ICollection<int>.Contains),
    JsMethod = "contains"
)]
[assembly: MapMethod(typeof(List<>), nameof(List<int>.IndexOf), JsMethod = "indexOf")]
[assembly: MapMethod(typeof(IList<>), nameof(IList<int>.IndexOf), JsMethod = "indexOf")]
[assembly: MapMethod(typeof(List<>), nameof(List<int>.LastIndexOf), JsMethod = "lastIndexOf")]

// Find / FindLast take the value C# returns on a miss, default(T), which the
// extractor appends (IntrinsicBclLoweringTable.ListFind).
[assembly: MapMethod(typeof(List<>), nameof(List<int>.Find), JsMethod = "find")]
[assembly: MapMethod(typeof(List<>), nameof(List<int>.FindLast), JsMethod = "findLast")]
[assembly: MapMethod(typeof(List<>), nameof(List<int>.FindIndex), JsMethod = "findIndex")]
[assembly: MapMethod(typeof(List<>), nameof(List<int>.FindLastIndex), JsMethod = "findLastIndex")]
[assembly: MapMethod(typeof(List<>), nameof(List<int>.FindAll), JsMethod = "findAll")]
[assembly: MapMethod(typeof(List<>), nameof(List<int>.Exists), JsMethod = "exists")]
[assembly: MapMethod(typeof(List<>), nameof(List<int>.TrueForAll), JsMethod = "trueForAll")]
[assembly: MapMethod(typeof(List<>), nameof(List<int>.ForEach), JsMethod = "forEach")]
[assembly: MapMethod(typeof(List<>), nameof(List<int>.ConvertAll), JsMethod = "convertAll")]
[assembly: MapMethod(typeof(List<>), nameof(List<int>.BinarySearch), JsMethod = "binarySearch")]

// ─── In-place reordering, copies and ranges ──────────────────────
[assembly: MapMethod(typeof(List<>), nameof(List<int>.Sort), JsMethod = "sort")]
[assembly: MapMethod(typeof(List<>), nameof(List<int>.Reverse), JsMethod = "reverse")]
[assembly: MapMethod(typeof(List<>), nameof(List<int>.GetRange), JsMethod = "getRange")]
[assembly: MapMethod(typeof(List<>), nameof(List<int>.ToArray), JsMethod = "toArray")]
[assembly: MapMethod(typeof(List<>), nameof(List<int>.CopyTo), JsMethod = "copyTo")]
[assembly: MapMethod(typeof(ICollection<>), nameof(ICollection<int>.CopyTo), JsMethod = "copyTo")]

// ─── Constructors ────────────────────────────────────────────────
// new List<T>() → new List<T>(); new List<T>(capacity) → List.withCapacity<T>(capacity)
// (the capacity is evaluated once and validated, FR-001); new List<T>(source) →
// List.from(source), a copy that throws ArgumentNullException for null, as .NET.

[assembly: MapMethod(
    typeof(List<>),
    MapMethodAttribute.ConstructorName,
    JsTemplate = "new List<$T0>()",
    RuntimeImports = "List",
    WhenParameterTypes = new Type[0]
)]
[assembly: MapMethod(
    typeof(List<>),
    MapMethodAttribute.ConstructorName,
    JsTemplate = "List.withCapacity<$T0>($0)",
    RuntimeImports = "List",
    WhenParameterTypes = new[] { typeof(int) }
)]
[assembly: MapMethod(
    typeof(List<>),
    MapMethodAttribute.ConstructorName,
    JsTemplate = "List.from($0)",
    RuntimeImports = "List",
    WhenParameterTypes = new[] { typeof(IEnumerable<>) }
)]
