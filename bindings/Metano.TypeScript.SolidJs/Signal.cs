using Metano.Annotations;
using Metano.Annotations.TypeScript;

namespace Metano.TypeScript.SolidJs;

/// <summary>
/// SolidJS signal as a JS array-tuple <c>[Accessor&lt;T&gt;, Setter&lt;T&gt;]</c>.
/// Modeled as a <c>[JsTuple]</c> positional record so it lowers to the imported
/// Solid <c>Signal&lt;T&gt;</c> array type with no wrapper object, no class, and
/// no synthesized record members. Destructuring a value of this type
/// (<c>var (get, set) = Solid.CreateSignal(v)</c>) lowers to JS array
/// destructuring (<c>const [get, set] = createSignal(v)</c>); positional access
/// falls back to index form (<c>sig.Getter</c> → <c>sig[0]</c>).
/// <para>
/// The getter is a pure single-signature <see cref="System.Func{T}"/> invoked as
/// <c>get()</c>; the setter is a <see cref="ISignalSetter{T}"/> callable whose
/// overloaded <c>Invoke</c> covers both the value and updater write forms.
/// </para>
/// </summary>
[JsTuple, Import("Signal", from: "solid-js")]
public record Signal<T>(System.Func<T> Getter, ISignalSetter<T> Setter);
