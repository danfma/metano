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
    /// <c>(value as any)</c>, and .NET returns the argument. Member names after it are
    /// verbatim: a Metano member <c>Total</c> is emitted as <c>total</c>, so
    /// <c>x.AsDynamic().Total</c> reads a member the emitted object does not have.
    /// Until dynamic member access is supported, a member access on the result
    /// reports <c>MS0001</c>.
    /// </summary>
    public static dynamic AsDynamic(this object? value) => value!;
}
