using Metano.Interop;

namespace Metano.TypeScript.SolidJs;

public static partial class Solid
{
    /// <summary>
    /// SolidJS <c>&lt;For&gt;</c> helper. The <c>items</c> parameter is the native
    /// array (<see cref="JsArray{T}"/>, spec 011 FR-004) because Solid's
    /// <c>&lt;For each&gt;</c> requires one, not a lazy iterable or a runtime
    /// collection; a <c>List&lt;T&gt;</c> crosses explicitly through
    /// <c>AsJsArray()</c> (a zero-copy view) or <c>ToJsArray()</c> (a copy). The
    /// render-prop <c>index</c> is contextually typed by Solid as
    /// <c>Accessor&lt;number&gt;</c>; the transpiler strips the lowered lambda's
    /// parameter type annotations so Solid's contextual typing applies.
    /// </summary>
    public static JsxElement For<T>(JsArray<T> items, Func<T, int, JsxElement> renderItem)
    {
        throw new NotImplementedException();
    }
}
