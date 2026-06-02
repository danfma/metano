using Metano.Annotations;

namespace Metano.TypeScript.SolidJs;

[NoContainer]
public static partial class Solid
{
    /// <summary>
    /// Creates a SolidJS signal. Lowers to <c>createSignal(value)</c> (imported
    /// from <c>solid-js</c>) returning the <see cref="Signal{T}"/> array-tuple
    /// directly. No wrapper object is allocated — the <c>[JsTuple]</c> return
    /// type erases to Solid's <c>Signal&lt;T&gt;</c>, so consumers destructure
    /// it: <c>var (get, set) = Solid.CreateSignal(v)</c> →
    /// <c>const [get, set] = createSignal(v)</c>. The body below exists only so
    /// the C# facade compiles and is never transpiled.
    /// </summary>
    [Import("createSignal", from: "solid-js")]
    public static Signal<T> CreateSignal<T>(T value)
    {
        throw new NotSupportedException("Only for TypeScript");
    }

    [Import("createEffect", from: "solid-js")]
    public static void CreateEffect(Action action)
    {
        throw new NotSupportedException("Only for TypeScript");
    }
}
