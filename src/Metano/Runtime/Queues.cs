// Declarative BCL → JavaScript mappings for System.Collections.Generic.Queue<T> (spec 011
// FR-007, stage 011c): the `metano-runtime` `Queue` class, a ring buffer with O(1) dequeue
// (contracts/runtime-collections.md). Every member is a plain rename; `TryDequeue` /
// `TryPeek` take the value C# stores on a miss, which the out-argument lowering appends.

using Metano.Annotations;

[assembly: MapProperty(typeof(Queue<>), nameof(Queue<int>.Count), JsProperty = "count")]
[assembly: MapMethod(typeof(Queue<>), nameof(Queue<int>.Enqueue), JsMethod = "enqueue")]
[assembly: MapMethod(typeof(Queue<>), nameof(Queue<int>.Dequeue), JsMethod = "dequeue")]
[assembly: MapMethod(typeof(Queue<>), nameof(Queue<int>.Peek), JsMethod = "peek")]
[assembly: MapMethod(typeof(Queue<>), nameof(Queue<int>.TryDequeue), JsMethod = "tryDequeue")]
[assembly: MapMethod(typeof(Queue<>), nameof(Queue<int>.TryPeek), JsMethod = "tryPeek")]
[assembly: MapMethod(typeof(Queue<>), nameof(Queue<int>.Contains), JsMethod = "contains")]
[assembly: MapMethod(typeof(Queue<>), nameof(Queue<int>.Clear), JsMethod = "clear")]
[assembly: MapMethod(typeof(Queue<>), nameof(Queue<int>.ToArray), JsMethod = "toArray")]
[assembly: MapMethod(typeof(Queue<>), nameof(Queue<int>.CopyTo), JsMethod = "copyTo")]

[assembly: MapMethod(
    typeof(Queue<>),
    MapMethodAttribute.ConstructorName,
    JsTemplate = "new Queue<$T0>()",
    RuntimeImports = "Queue",
    WhenParameterTypes = new Type[0]
)]
[assembly: MapMethod(
    typeof(Queue<>),
    MapMethodAttribute.ConstructorName,
    JsTemplate = "Queue.withCapacity<$T0>($0)",
    RuntimeImports = "Queue",
    WhenParameterTypes = new[] { typeof(int) }
)]
[assembly: MapMethod(
    typeof(Queue<>),
    MapMethodAttribute.ConstructorName,
    JsTemplate = "Queue.from($0)",
    RuntimeImports = "Queue",
    WhenParameterTypes = new[] { typeof(IEnumerable<>) }
)]
