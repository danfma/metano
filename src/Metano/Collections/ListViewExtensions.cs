using System.Runtime.InteropServices;

namespace Metano.Collections;

/// <summary>
/// Zero-copy views of a <see cref="List{T}"/>'s elements (spec 011 FR-056):
/// each is the list's backing array itself, so a write through the view is a
/// write to the list and the reverse. TypeScript lowers each call to the free
/// helper of the same name in <c>metano-runtime/system/collections</c>
/// (<c>asSpan(list)</c>, <c>asReadOnlySpan(list)</c>), which also handles
/// <c>null</c>. A view taken before the list grows keeps its length in .NET
/// and sees the growth in TypeScript (one backing array for life), a
/// documented divergence.
/// </summary>
public static class ListViewExtensions
{
    /// <summary>The list's elements as a writable span (<c>CollectionsMarshal.AsSpan</c>).</summary>
    public static Span<T> AsSpan<T>(this List<T>? list) => CollectionsMarshal.AsSpan(list);

    /// <summary>The list's elements as a read-only span.</summary>
    public static ReadOnlySpan<T> AsReadOnlySpan<T>(this List<T>? list) =>
        CollectionsMarshal.AsSpan(list);
}
