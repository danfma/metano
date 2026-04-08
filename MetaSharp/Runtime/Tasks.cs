// Declarative BCL → JavaScript mappings for System.Threading.Tasks.Task.
//
// Task<T> and Task lower to JS Promise<T> and Promise<void> respectively. The static
// factory methods on Task become equivalent calls on Promise.

using System.Threading.Tasks;
using MetaSharp.Annotations;

// Task.CompletedTask → Promise.resolve()
[assembly: MapProperty(typeof(Task), "CompletedTask", JsTemplate = "Promise.resolve()")]

// Task.FromResult(x) → Promise.resolve(x)
[assembly: MapMethod(typeof(Task), "FromResult", JsTemplate = "Promise.resolve($0)")]
