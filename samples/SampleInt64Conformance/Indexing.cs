using Metano.Annotations;

namespace SampleInt64Conformance;

/// <summary>
/// The indexing rows of the reference matrix (spec 012 FR-036,
/// contracts/ts-emission.md § BCL): an element read through a 64-bit index,
/// <c>Array.LongLength</c>, and array creation with a 64-bit length. The
/// indices stay in range on purpose: an out-of-range index reads
/// <c>undefined</c> in the generated arrays (011 OD-4, a documented
/// divergence) where .NET throws.
/// </summary>
[NoContainer]
public static class Indexing
{
    public static long ElementAt(long[] values, long index) => values[index];

    public static int IntElementAt(int[] values, long index) => values[index];

    public static long LongLengthOf(long[] values) => values.LongLength;

    public static long[] SizedLongs(long length) => new long[length];

    public static int[] SizedInts(int length) => new int[length];
}
