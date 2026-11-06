using Metano.Annotations;

namespace SampleInt64Conformance;

/// <summary>
/// The comparison and hashing rows of the reference matrix (spec 012 FR-040,
/// contracts/runtime-int64.md § Comparison and hashing): <c>CompareTo</c>
/// over the boundary operands, <c>Equals(long)</c>, <c>Equals(object)</c>
/// over boxed operands (a boxed <c>long</c> is not equal to a boxed
/// <c>int</c>, on either side), and <c>GetHashCode()</c>, whose values pin
/// the <c>hashInt64</c> formula (DECIDED-13).
/// </summary>
[NoContainer]
public static class Comparisons
{
    public static int CompareTo(long value, long other) => value.CompareTo(other);

    public static int CompareToUnsigned(ulong value, ulong other) => value.CompareTo(other);

    public static bool EqualsLong(long value, long other) => value.Equals(other);

    public static bool EqualsBoxed(long value, object boxed) => value.Equals(boxed);

    public static int GetHashCodeOf(long value) => value.GetHashCode();
}
