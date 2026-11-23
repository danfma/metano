// Declarative BCL → JavaScript mappings for System.Threading.Tasks.TaskCompletionSource<T>
// (#686): the `metano-runtime` `TaskCompletionSource` class. `Task` lowers to a `Promise`, which
// the class exposes as `task`. The non-generic `TaskCompletionSource` and the `TaskCreationOptions`
// constructors stay unmapped.

using Metano.Annotations;

[assembly: ExportFromBcl(
    typeof(TaskCompletionSource<>),
    ExportedName = "TaskCompletionSource",
    FromPackage = "metano-runtime"
)]
[assembly: MapProperty(
    typeof(TaskCompletionSource<>),
    nameof(TaskCompletionSource<int>.Task),
    JsProperty = "task"
)]
[assembly: MapMethod(
    typeof(TaskCompletionSource<>),
    MapMethodAttribute.ConstructorName,
    JsTemplate = "new TaskCompletionSource<$T0>()",
    RuntimeImports = "TaskCompletionSource",
    WhenParameterTypes = new Type[0]
)]
[assembly: MapMethod(
    typeof(TaskCompletionSource<>),
    nameof(TaskCompletionSource<int>.SetResult),
    JsMethod = "setResult"
)]
[assembly: MapMethod(
    typeof(TaskCompletionSource<>),
    nameof(TaskCompletionSource<int>.TrySetResult),
    JsMethod = "trySetResult"
)]
[assembly: MapMethod(
    typeof(TaskCompletionSource<>),
    nameof(TaskCompletionSource<int>.SetCanceled),
    JsMethod = "setCanceled"
)]
[assembly: MapMethod(
    typeof(TaskCompletionSource<>),
    nameof(TaskCompletionSource<int>.TrySetCanceled),
    JsMethod = "trySetCanceled"
)]
[assembly: MapMethod(
    typeof(TaskCompletionSource<>),
    nameof(TaskCompletionSource<int>.SetException),
    JsMethod = "setException",
    WhenParameterTypes = [typeof(Exception)]
)]
[assembly: MapMethod(
    typeof(TaskCompletionSource<>),
    nameof(TaskCompletionSource<int>.TrySetException),
    JsMethod = "trySetException",
    WhenParameterTypes = [typeof(Exception)]
)]
