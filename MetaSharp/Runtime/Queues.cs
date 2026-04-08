// Declarative BCL → JavaScript mappings for System.Collections.Generic.Queue<T>.
//
// Queue<T> lowers to a plain JS array used FIFO. JS arrays don't have a dequeue/enqueue
// API, so we use templates to express the equivalent operations: push for enqueue,
// shift for dequeue, [0] for peek, length = 0 for clear.

using System.Collections.Generic;
using MetaSharp.Annotations;

[assembly: MapProperty(typeof(Queue<>), nameof(Queue<int>.Count), JsProperty = "length")]

[assembly: MapMethod(typeof(Queue<>), nameof(Queue<int>.Enqueue), JsMethod = "push")]
[assembly: MapMethod(typeof(Queue<>), nameof(Queue<int>.Dequeue), JsMethod = "shift")]
[assembly: MapMethod(typeof(Queue<>), nameof(Queue<int>.Peek), JsTemplate = "$this[0]")]
[assembly: MapMethod(typeof(Queue<>), nameof(Queue<int>.Contains), JsMethod = "includes")]
[assembly: MapMethod(typeof(Queue<>), nameof(Queue<int>.Clear), JsTemplate = "$this.length = 0")]
