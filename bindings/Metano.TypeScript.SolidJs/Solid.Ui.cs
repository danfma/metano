namespace Metano.TypeScript.SolidJs;

public static partial class Solid
{
    /// <summary>
    /// SolidJS <c>&lt;For&gt;</c> helper. The <c>items</c> parameter is typed as
    /// <see cref="IReadOnlyList{T}"/> (not <see cref="IEnumerable{T}"/>) so the
    /// transpiled <c>each={…}</c> binding receives a materialized array — Solid's
    /// <c>&lt;For each&gt;</c> requires an array, not a lazy iterable (an
    /// <c>Iterable&lt;T&gt;</c> would be a TS2322). The render-prop <c>index</c>
    /// is contextually typed by Solid as <c>Accessor&lt;number&gt;</c>; the
    /// transpiler strips the lowered lambda's parameter type annotations so
    /// Solid's contextual typing applies.
    /// </summary>
    public static JsxElement For<T>(IReadOnlyList<T> items, Func<T, int, JsxElement> renderItem)
    {
        throw new NotImplementedException();
    }
}
