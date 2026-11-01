// Declarative BCL → JavaScript mappings for GetEnumerator() on the sequence types.
//
// `GetEnumerator()` is C#'s entry into the enumeration contract; JavaScript's is
// `[Symbol.iterator]()`. Every sequence type below lowers to a JS iterable (an array, a
// Map, or the runtime HashSet), so the call lowers to that iterable's iterator, typed
// `Iterator<T>` (IEnumerator<T>). This is what lets a class implementing IEnumerable<T>
// delegate to a wrapped collection: `GetEnumerator() => _items.GetEnumerator()`.
//
// - IEnumerable<T> covers every interface receiver (IList<T>, ICollection<T>,
//   IReadOnlyList<T>, … inherit it) and transpiled IEnumerable<T> implementations, which
//   declare `[Symbol.iterator]()`.
// - Dictionary<K,V> iterates `[key, value]` pairs, the KeyValuePair<K,V> shape.
// - Queue<T> is an array iterated front to back, as C# does. Stack<T> stays unmapped:
//   C# enumerates it top first, the reverse of its backing array.
//
// Only iterating the result is supported: IEnumerator<T>.MoveNext / Current / Reset are
// not mapped.

using Metano.Annotations;

[assembly: MapMethod(
    typeof(IEnumerable<>),
    nameof(IEnumerable<int>.GetEnumerator),
    JsTemplate = "$this[Symbol.iterator]()"
)]
[assembly: MapMethod(
    typeof(List<>),
    nameof(List<int>.GetEnumerator),
    JsTemplate = "$this[Symbol.iterator]()"
)]
[assembly: MapMethod(
    typeof(HashSet<>),
    nameof(HashSet<int>.GetEnumerator),
    JsTemplate = "$this[Symbol.iterator]()"
)]
[assembly: MapMethod(
    typeof(Dictionary<,>),
    nameof(Dictionary<int, int>.GetEnumerator),
    JsTemplate = "$this[Symbol.iterator]()"
)]
[assembly: MapMethod(
    typeof(Queue<>),
    nameof(Queue<int>.GetEnumerator),
    JsTemplate = "$this[Symbol.iterator]()"
)]
