using Metano.Annotations.TypeScript;

namespace Metano.TypeScript.SolidJs;

/// <summary>
/// The SolidJS signal setter as a JS callable. Marked <c>[JsCallable]</c> so the
/// interface is erased and every <c>Invoke(...)</c> call lowers to a direct
/// invocation of the receiver — <c>setCount.Invoke(v)</c> → <c>setCount(v)</c> —
/// with no <c>[Emit]</c> template. Two overloads model Solid's setter call
/// signatures: a direct value write and an updater write. The value form is NOT
/// wrapped in a thunk; both lower identically to a positional call.
/// </summary>
[JsCallable]
public interface ISignalSetter<T>
{
    void Invoke(T value);

    void Invoke(System.Func<T, T> updater);
}
