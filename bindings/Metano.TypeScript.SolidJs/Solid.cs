using Metano.Annotations;

namespace Metano.TypeScript.SolidJs;

[NoContainer]
public static partial class Solid
{
    /// <summary>
    /// Creates a SolidJS signal. The <c>[Emit]</c> template fully replaces the
    /// body at the call site: <c>Solid.CreateSignal(v)</c> →
    /// <c>createSignal(v)</c> (imported from <c>solid-js</c>), returning the
    /// signal tuple directly. No <c>SignalWrapper</c> is allocated — the body
    /// below exists only so the C# facade compiles and is never transpiled.
    /// </summary>
    [Import("createSignal", from: "solid-js"), Emit("createSignal($0)")]
    public static ISignal<T> CreateSignal<T>(T value)
    {
        throw new NotSupportedException("Only for TypeScript");
    }

    [Import("createEffect", from: "solid-js")]
    public static void CreateEffect(Action action)
    {
        throw new NotSupportedException("Only for TypeScript");
    }
}
