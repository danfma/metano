// Declarative BCL → JavaScript mappings for System.Collections.Generic.Stack<T> (spec 011
// FR-007, stage 011c): the `metano-runtime` `Stack` class, which enumerates, copies and
// serializes top first as C# does (contracts/runtime-collections.md). Every member is a
// plain rename; `TryPop` / `TryPeek` take the value C# stores on a miss, which the
// out-argument lowering appends.

using Metano.Annotations;

[assembly: MapProperty(typeof(Stack<>), nameof(Stack<int>.Count), JsProperty = "count")]
[assembly: MapMethod(typeof(Stack<>), nameof(Stack<int>.Push), JsMethod = "push")]
[assembly: MapMethod(typeof(Stack<>), nameof(Stack<int>.Pop), JsMethod = "pop")]
[assembly: MapMethod(typeof(Stack<>), nameof(Stack<int>.Peek), JsMethod = "peek")]
[assembly: MapMethod(typeof(Stack<>), nameof(Stack<int>.TryPop), JsMethod = "tryPop")]
[assembly: MapMethod(typeof(Stack<>), nameof(Stack<int>.TryPeek), JsMethod = "tryPeek")]
[assembly: MapMethod(typeof(Stack<>), nameof(Stack<int>.Contains), JsMethod = "contains")]
[assembly: MapMethod(typeof(Stack<>), nameof(Stack<int>.Clear), JsMethod = "clear")]
[assembly: MapMethod(typeof(Stack<>), nameof(Stack<int>.ToArray), JsMethod = "toArray")]
[assembly: MapMethod(typeof(Stack<>), nameof(Stack<int>.CopyTo), JsMethod = "copyTo")]

[assembly: MapMethod(
    typeof(Stack<>),
    MapMethodAttribute.ConstructorName,
    JsTemplate = "new Stack<$T0>()",
    RuntimeImports = "Stack",
    WhenParameterTypes = new Type[0]
)]
[assembly: MapMethod(
    typeof(Stack<>),
    MapMethodAttribute.ConstructorName,
    JsTemplate = "Stack.withCapacity<$T0>($0)",
    RuntimeImports = "Stack",
    WhenParameterTypes = new[] { typeof(int) }
)]
[assembly: MapMethod(
    typeof(Stack<>),
    MapMethodAttribute.ConstructorName,
    JsTemplate = "Stack.from($0)",
    RuntimeImports = "Stack",
    WhenParameterTypes = new[] { typeof(IEnumerable<>) }
)]
