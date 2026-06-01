using Metano.Annotations;
using Metano.Annotations.TypeScript;

namespace Metano.TypeScript.SolidJs;

/// <summary>
/// Compile-time facade over the SolidJS signal tuple
/// <c>[Accessor&lt;T&gt;, Setter&lt;T&gt;]</c> (the <c>Signal&lt;T&gt;</c> type
/// from <c>solid-js</c>). No wrapper object is allocated: every member lowers
/// to a direct tuple operation via <c>[Emit]</c>, and the type itself maps to
/// the imported <c>Signal&lt;T&gt;</c> so a field/variable typed as
/// <c>ISignal&lt;T&gt;</c> annotates as <c>Signal&lt;T&gt;</c>.
/// </summary>
[External, Import("Signal", from: "solid-js")]
public interface ISignal<T>
{
    [Emit("$0[0]()")]
    public T Value { get; }

    // Direct setter form per FR-016 / clarification D-Set: the value is NOT
    // wrapped in `() => v`.
    //
    // FR-016's defensive updater-wrap for *function-typed* signals (a
    // `ISignal<Func<…>>` whose value-set would be misread by Solid's setter as
    // an updater) is deliberately DEFERRED here: the [Emit] template is static
    // and cannot branch on whether the closed generic argument `T` is a delegate
    // type, so the disambiguation cannot be expressed declaratively. A correct
    // fix needs call-site logic that inspects the receiver's closed `T` and
    // selects a thunk-wrapping template (`$0[1](() => $1)`) only for the
    // delegate case — new machinery used by nothing else today. Per spec this
    // case is explicitly rare; wrapping unconditionally would violate FR-016's
    // "value MUST NOT be wrapped" rule for the common (non-function) case, so
    // the unconditional direct form is the safe default until a binding needs
    // function-valued signals.
    [Emit("$0[1]($1)")]
    public void Set(T value);

    [Emit("$0[1]($1)")]
    public void Set(Func<T, T> updater);
}
