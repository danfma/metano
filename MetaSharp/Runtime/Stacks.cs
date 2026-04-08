// Declarative BCL → JavaScript mappings for System.Collections.Generic.Stack<T>.
//
// Stack<T> lowers to a plain JS array used LIFO. JS arrays already have push/pop with
// LIFO semantics; peek and clear use templates because JS has no equivalent member.

using System.Collections.Generic;
using MetaSharp.Annotations;

[assembly: MapProperty(typeof(Stack<>), nameof(Stack<int>.Count), JsProperty = "length")]

[assembly: MapMethod(typeof(Stack<>), nameof(Stack<int>.Push), JsMethod = "push")]
[assembly: MapMethod(typeof(Stack<>), nameof(Stack<int>.Pop), JsMethod = "pop")]
[assembly: MapMethod(typeof(Stack<>), nameof(Stack<int>.Peek), JsTemplate = "$this[$this.length - 1]")]
[assembly: MapMethod(typeof(Stack<>), nameof(Stack<int>.Contains), JsMethod = "includes")]
[assembly: MapMethod(typeof(Stack<>), nameof(Stack<int>.Clear), JsTemplate = "$this.length = 0")]
