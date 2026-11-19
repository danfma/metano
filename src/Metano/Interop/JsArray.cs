using System.Collections;
using System.Runtime.CompilerServices;
using Metano.Annotations;

namespace Metano.Interop;

/// <summary>
/// The native JavaScript array, for interop signatures (<c>[Import]</c>
/// facades, <c>[JsCallable]</c> callbacks, JSX props) that must name it
/// without <c>T[]</c>'s C# semantics (spec 011 FR-004, D2). TypeScript emits
/// <c>T[]</c>, and every member is the <c>Array</c> member of the same name;
/// a C# <c>T[]</c> converts to it implicitly and nothing is emitted (both are
/// the native array). On .NET it is backed by a <see cref="List{T}"/>.
/// TypeScript only: any other target reports <c>MS0051</c>.
/// </summary>
/// <typeparam name="T">The element type.</typeparam>
[TargetSpecific(TargetLanguage.TypeScript)]
[CollectionBuilder(typeof(JsArray), nameof(JsArray.Create))]
public sealed class JsArray<T> : IList<T>, IReadOnlyList<T>
{
    private readonly List<T> _items;

    /// <summary>An empty array (<c>[]</c>).</summary>
    public JsArray() => _items = [];

    /// <summary>An array holding <paramref name="items"/>, copied.</summary>
    public JsArray(IEnumerable<T> items) => _items = [.. items];

    /// <summary><c>length</c>.</summary>
    public int Length => _items.Count;

    /// <summary><c>arr[index]</c>: no bounds check in JavaScript.</summary>
    public T this[int index]
    {
        get => _items[index];
        set => _items[index] = value;
    }

    int ICollection<T>.Count => _items.Count;

    int IReadOnlyCollection<T>.Count => _items.Count;

    bool ICollection<T>.IsReadOnly => false;

    /// <summary>The array itself: <c>T[]</c> → <c>JsArray&lt;T&gt;</c> emits nothing.</summary>
    public static implicit operator JsArray<T>(T[] array) => new(array);

    /// <summary><c>push(...items)</c>: the new length.</summary>
    public int Push(params T[] items)
    {
        _items.AddRange(items);

        return _items.Count;
    }

    /// <summary><c>pop()</c>: the last element, removed (<c>undefined</c> when empty).</summary>
    public T? Pop()
    {
        if (_items.Count == 0)
            return default;

        var last = _items[^1];

        _items.RemoveAt(_items.Count - 1);

        return last;
    }

    /// <summary><c>shift()</c>: the first element, removed (<c>undefined</c> when empty).</summary>
    public T? Shift()
    {
        if (_items.Count == 0)
            return default;

        var first = _items[0];

        _items.RemoveAt(0);

        return first;
    }

    /// <summary><c>unshift(...items)</c>: the new length.</summary>
    public int Unshift(params T[] items)
    {
        _items.InsertRange(0, items);

        return _items.Count;
    }

    /// <summary><c>slice(start, end)</c>: a copy of <c>[start, end)</c>.</summary>
    public JsArray<T> Slice(int start = 0, int? end = null)
    {
        var from = Clamp(start);
        var to = end is { } last ? Clamp(last) : _items.Count;

        return new JsArray<T>(_items.Skip(from).Take(Math.Max(0, to - from)));
    }

    /// <summary><c>splice(start, deleteCount, ...items)</c>: the removed elements.</summary>
    public JsArray<T> Splice(int start, int deleteCount, params T[] items)
    {
        var from = Clamp(start);
        var count = Math.Clamp(deleteCount, 0, _items.Count - from);
        var removed = new JsArray<T>(_items.GetRange(from, count));

        _items.RemoveRange(from, count);
        _items.InsertRange(from, items);

        return removed;
    }

    /// <summary><c>indexOf(item)</c>.</summary>
    public int IndexOf(T item) => _items.IndexOf(item);

    /// <summary><c>includes(item)</c>.</summary>
    public bool Includes(T item) => _items.Contains(item);

    /// <summary><c>map(f)</c>.</summary>
    public JsArray<TResult> Map<TResult>(Func<T, TResult> selector) => new(_items.Select(selector));

    /// <summary><c>filter(p)</c>.</summary>
    public JsArray<T> Filter(Func<T, bool> predicate) => new(_items.Where(predicate));

    /// <summary><c>forEach(f)</c>.</summary>
    public void ForEach(Action<T> action) => _items.ForEach(action);

    /// <summary><c>join(separator)</c>.</summary>
    public string Join(string separator = ",") => string.Join(separator, _items);

    /// <summary><c>reverse()</c>: in place, the array itself.</summary>
    public JsArray<T> Reverse()
    {
        _items.Reverse();

        return this;
    }

    /// <summary><c>sort(compareFn)</c>: in place, the array itself.</summary>
    public JsArray<T> Sort(Comparison<T> comparison)
    {
        _items.Sort(comparison);

        return this;
    }

    /// <summary><c>at(index)</c>: a negative index counts from the end.</summary>
    public T? At(int index)
    {
        var position = index < 0 ? _items.Count + index : index;

        return position >= 0 && position < _items.Count ? _items[position] : default;
    }

    /// <summary>
    /// The array viewed as a <see cref="List{T}"/> sharing its storage:
    /// <c>List.adopt(array)</c> (spec 011 FR-055).
    /// </summary>
    public List<T> AsList() => _items;

    /// <inheritdoc />
    public IEnumerator<T> GetEnumerator() => _items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    void ICollection<T>.Add(T item) => _items.Add(item);

    void ICollection<T>.Clear() => _items.Clear();

    bool ICollection<T>.Contains(T item) => _items.Contains(item);

    void ICollection<T>.CopyTo(T[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);

    bool ICollection<T>.Remove(T item) => _items.Remove(item);

    void IList<T>.Insert(int index, T item) => _items.Insert(index, item);

    void IList<T>.RemoveAt(int index) => _items.RemoveAt(index);

    private int Clamp(int index) =>
        index < 0 ? Math.Max(0, _items.Count + index) : Math.Min(index, _items.Count);
}

/// <summary>Factories of <see cref="JsArray{T}"/>.</summary>
[TargetSpecific(TargetLanguage.TypeScript)]
public static class JsArray
{
    /// <summary><c>Array.from(items)</c>: a new native array copied from any iterable.</summary>
    public static JsArray<T> From<T>(IEnumerable<T> items) => new(items);

    /// <summary>
    /// The builder of a collection expression (<c>JsArray&lt;int&gt; a = [1, 2]</c>),
    /// which TypeScript emits as the array literal itself.
    /// </summary>
    public static JsArray<T> Create<T>(ReadOnlySpan<T> items) => new(items.ToArray());
}
