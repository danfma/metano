using System.Collections;
using System.Runtime.CompilerServices;
using Metano.Annotations;

namespace Metano.Interop;

/// <summary>
/// A read-only view of a native JavaScript array, for interop signatures
/// (<c>[Import]</c> facades, <c>[JsCallable]</c> callbacks, JSX props) and
/// zero-copy <c>AsReadOnlyJsArray()</c> views (spec 011 FR-004, FR-056).
/// TypeScript emits <c>readonly T[]</c>, exposing only non-mutating
/// <c>ReadonlyArray</c> members; <see cref="JsArray{T}"/> and <c>T[]</c>
/// convert to it implicitly and nothing is emitted. On .NET it wraps an
/// <see cref="IReadOnlyList{T}"/> directly (or a <see cref="List{T}"/> copy when
/// constructed from an enumerable). TypeScript only: any other target reports
/// <c>MS0051</c>.
/// </summary>
/// <typeparam name="T">The element type.</typeparam>
[TargetSpecific(TargetLanguage.TypeScript)]
[CollectionBuilder(typeof(ReadOnlyJsArray), nameof(ReadOnlyJsArray.Create))]
public sealed class ReadOnlyJsArray<T> : IReadOnlyList<T>
{
    private readonly IReadOnlyList<T> _items;

    /// <summary>An empty read-only array (<c>[]</c>).</summary>
    public ReadOnlyJsArray() => _items = [];

    /// <summary>A read-only array holding <paramref name="items"/>, copied.</summary>
    public ReadOnlyJsArray(IEnumerable<T> items) => _items = [.. items];

    private ReadOnlyJsArray(IReadOnlyList<T> items, bool _) => _items = items;

    /// <summary><c>length</c>.</summary>
    public int Length => _items.Count;

    /// <summary><c>arr[index]</c>: read-only; no bounds check in JavaScript.</summary>
    public T this[int index] => _items[index];

    int IReadOnlyCollection<T>.Count => _items.Count;

    /// <summary>The array itself: <c>JsArray&lt;T&gt;</c> → <c>ReadOnlyJsArray&lt;T&gt;</c> emits nothing.</summary>
    public static implicit operator ReadOnlyJsArray<T>(JsArray<T> array) =>
        array is null ? null! : Wrap(array);

    /// <summary>The array itself: <c>T[]</c> → <c>ReadOnlyJsArray&lt;T&gt;</c> emits nothing.</summary>
    public static implicit operator ReadOnlyJsArray<T>(T[] array) =>
        array is null ? null! : Wrap(array);

    /// <summary><c>slice(start, end)</c>: a mutable copy of <c>[start, end)</c>.</summary>
    public JsArray<T> Slice(int start = 0, int? end = null)
    {
        var from = Clamp(start);
        var to = end is { } last ? Clamp(last) : _items.Count;

        return new JsArray<T>(_items.Skip(from).Take(Math.Max(0, to - from)));
    }

    /// <summary><c>indexOf(item)</c>.</summary>
    public int IndexOf(T item)
    {
        if (_items is List<T> list)
            return list.IndexOf(item);

        var comparer = EqualityComparer<T>.Default;

        for (var i = 0; i < _items.Count; i++)
        {
            if (comparer.Equals(_items[i], item))
                return i;
        }

        return -1;
    }

    /// <summary><c>includes(item)</c>.</summary>
    public bool Includes(T item) => IndexOf(item) >= 0;

    /// <summary><c>map(f)</c>.</summary>
    public JsArray<TResult> Map<TResult>(Func<T, TResult> selector) => new(_items.Select(selector));

    /// <summary><c>filter(p)</c>.</summary>
    public JsArray<T> Filter(Func<T, bool> predicate) => new(_items.Where(predicate));

    /// <summary><c>forEach(f)</c>.</summary>
    public void ForEach(Action<T> action)
    {
        foreach (var item in _items)
            action(item);
    }

    /// <summary><c>join(separator)</c>.</summary>
    public string Join(string separator = ",") => string.Join(separator, _items);

    /// <summary><c>at(index)</c>: a negative index counts from the end.</summary>
    public T? At(int index)
    {
        var position = index < 0 ? _items.Count + index : index;

        return position >= 0 && position < _items.Count ? _items[position] : default;
    }

    /// <inheritdoc />
    public IEnumerator<T> GetEnumerator() => _items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    internal static ReadOnlyJsArray<T> Wrap(IReadOnlyList<T> items) => new(items, true);

    private int Clamp(int index) =>
        index < 0 ? Math.Max(0, _items.Count + index) : Math.Min(index, _items.Count);
}

/// <summary>Factories of <see cref="ReadOnlyJsArray{T}"/>.</summary>
[TargetSpecific(TargetLanguage.TypeScript)]
public static class ReadOnlyJsArray
{
    /// <summary><c>Array.from(items)</c>: a new read-only array view copied from any iterable.</summary>
    public static ReadOnlyJsArray<T> From<T>(IEnumerable<T> items) => new(items);

    /// <summary>
    /// The builder of a collection expression (<c>ReadOnlyJsArray&lt;int&gt; a = [1, 2]</c>),
    /// which TypeScript emits as the array literal itself.
    /// </summary>
    public static ReadOnlyJsArray<T> Create<T>(ReadOnlySpan<T> items) => new(items.ToArray());
}
