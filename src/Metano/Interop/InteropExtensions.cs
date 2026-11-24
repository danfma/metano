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
    /// Contract (mirroring <c>CollectionsMarshal.AsSpan</c>): do not add or remove
    /// elements through the writable view while the <see cref="List{T}"/> is being
    /// enumerated (it bypasses <c>_version</c>). TypeScript emits
    /// <c>asJsArray(list)</c>, which throws <c>ArgumentNullException</c> for
    /// <c>null</c>. On .NET it is a copy, so a write through it is not seen by
    /// the list, a documented divergence; TypeScript only (<c>MS0051</c> elsewhere).
    /// </summary>
    [TargetSpecific(Metano.Annotations.TargetLanguage.TypeScript)]
    public static JsArray<T> AsJsArray<T>(this List<T> list) =>
        new(list ?? throw new ArgumentNullException(nameof(list)));

    /// <summary>
    /// A read-only view over the list's backing array (spec 011 FR-056):
    /// zero-copy and live for a <see cref="List{T}"/> (reflects later mutations;
    /// read-only for the consumer). TypeScript emits
    /// <c>asReadOnlyJsArray(list)</c> typed <c>readonly T[]</c>, which throws
    /// <c>ArgumentNullException</c> for <c>null</c>. On .NET it wraps
    /// <paramref name="list"/> without copying; TypeScript only (<c>MS0051</c>
    /// elsewhere).
    /// </summary>
    [TargetSpecific(Metano.Annotations.TargetLanguage.TypeScript)]
    public static ReadOnlyJsArray<T> AsReadOnlyJsArray<T>(this List<T> list) =>
        ReadOnlyJsArray<T>.Wrap(list ?? throw new ArgumentNullException(nameof(list)));

    /// <summary>
    /// A read-only view over the list's backing array (spec 011 FR-056a):
    /// zero-copy and live for runtime array-backed list kinds (reflects later
    /// mutations of a mutable source; array identity is not stable for older
    /// <c>ImmutableList</c>/<c>ImmutableArray</c> versions sharing a grown store),
    /// and a shallow snapshot copy (<c>Array.from(list)</c>) when wrapping a
    /// <c>ReadOnlyCollection</c> over a custom <c>IList</c> or a receiver that
    /// lacks <c>asReadOnlyJsArray()</c> (such as a user-defined
    /// <see cref="IReadOnlyList{T}"/> class). TypeScript emits
    /// <c>asReadOnlyJsArray(list)</c> typed <c>readonly T[]</c>, which throws
    /// <c>ArgumentNullException</c> for <c>null</c>. On .NET it wraps
    /// <paramref name="list"/> without copying; TypeScript only (<c>MS0051</c>
    /// elsewhere).
    /// </summary>
    [TargetSpecific(Metano.Annotations.TargetLanguage.TypeScript)]
    public static ReadOnlyJsArray<T> AsReadOnlyJsArray<T>(this IReadOnlyList<T> list) =>
        ReadOnlyJsArray<T>.Wrap(list ?? throw new ArgumentNullException(nameof(list)));

    /// <summary>
    /// A new native array copied from the list (spec 011 FR-055): TypeScript
    /// emits <c>list.toArray()</c>, as for <c>ToArray()</c>.
    /// </summary>
    [TargetSpecific(Metano.Annotations.TargetLanguage.TypeScript)]
    public static JsArray<T> ToJsArray<T>(this List<T> list) =>
        new(list ?? throw new ArgumentNullException(nameof(list)));

    /// <summary>
    /// A new mutable native array copied from the read-only list (spec 011
    /// FR-055): TypeScript emits <c>list.toArray()</c>.
    /// </summary>
    [TargetSpecific(Metano.Annotations.TargetLanguage.TypeScript)]
    public static JsArray<T> ToJsArray<T>(this IReadOnlyList<T> list) =>
        new(list ?? throw new ArgumentNullException(nameof(list)));
}
