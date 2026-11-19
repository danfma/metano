namespace Metano.Interop;

/// <summary>
/// Compile-time escape hatches between typed and untyped values (spec 011 FR-035,
/// FR-036). Neither emits a runtime check, a descriptor or a helper call.
/// </summary>
public static class InteropExtensions
{
    /// <summary>
    /// Reinterprets <paramref name="value"/> as <typeparamref name="T"/> without a
    /// check: TypeScript emits <c>value as unknown as T</c>. On .NET it is
    /// <c>(T)value</c>, which throws <see cref="InvalidCastException"/> for a value of
    /// another type, where TypeScript succeeds silently.
    /// </summary>
    public static T UnsafeCast<T>(this object? value) => (T)value!;

    /// <summary>
    /// Turns <paramref name="value"/> into <c>dynamic</c>: TypeScript emits
    /// <c>(value as any)</c> (<c>(unbox(value) as any)</c> when the value may hold a
    /// box), and .NET returns the argument. A member access on the result is emitted
    /// verbatim in TypeScript, <c>(x as any).Total</c>: a Metano member <c>Total</c>
    /// is emitted as <c>total</c>, so that access reads a member the emitted object
    /// does not have, and <c>MS0057</c> warns. Dart emits <c>(value as dynamic)</c>
    /// and reports <c>MS0001</c> for a member access on the result until after 3.0.
    /// </summary>
    public static dynamic AsDynamic(this object? value) => value!;

    /// <summary>
    /// The list's backing array itself, as the native JavaScript array (spec 011
    /// FR-056): no copy, so a write through either side is seen by the other.
    /// TypeScript emits <c>asJsArray(list)</c>, which throws
    /// <c>ArgumentNullException</c> for <c>null</c>. On .NET it is a copy, so a
    /// write through it is not seen by the list, a documented divergence;
    /// TypeScript only (<c>MS0051</c> elsewhere).
    /// </summary>
    [TargetSpecific(Metano.Annotations.TargetLanguage.TypeScript)]
    public static JsArray<T> AsJsArray<T>(this List<T> list) =>
        new(list ?? throw new ArgumentNullException(nameof(list)));

    /// <summary>
    /// A new native array copied from the list (spec 011 FR-055): TypeScript
    /// emits <c>list.toArray()</c>, as for <c>ToArray()</c>.
    /// </summary>
    [TargetSpecific(Metano.Annotations.TargetLanguage.TypeScript)]
    public static JsArray<T> ToJsArray<T>(this List<T> list) => new(list);
}
