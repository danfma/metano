using System;
using System.Linq;
using Metano.Annotations;

namespace SampleInt64Conformance;

/// <summary>
/// The LINQ and ticks rows of the reference matrix (spec 012 FR-036,
/// FR-041, FR-042): the checked 64-bit <c>Sum</c> and <c>Average</c>,
/// <c>Min</c> / <c>Max</c> with their empty-sequence exceptions, the
/// nullable terminals, <c>LongCount</c>, and the <c>TimeSpan</c> /
/// <c>DateTime</c> tick round trips with their range checks.
/// </summary>
[NoContainer]
public static class Aggregates
{
    public static long SumOfThree(long a, long b, long c) => new[] { a, b, c }.Sum();

    public static double AverageOfTwo(long a, long b) => new[] { a, b }.Average();

    public static long? NullableSum(long? a, long? b) => new[] { a, b }.Sum();

    public static double? NullableAverage(long? a, long? b) => new[] { a, b }.Average();

    public static long MinOfTwo(long a, long b) => new[] { a, b }.Min();

    public static ulong MaxOfUnsigned(ulong a, ulong b) => new[] { a, b }.Max();

    public static long? NullableMax(long? a, long? b) => new[] { a, b }.Max();

    public static long CountPositive(long a, long b) => new[] { a, b }.LongCount(x => x > 0);

    public static double EmptyAverage() => new long[0].Average();

    public static long EmptyMin() => new long[0].Min();

    public static long? EmptyNullableMin() => new long?[0].Min();

    public static long SpanRoundTrip(long ticks) => TimeSpan.FromTicks(ticks).Ticks;

    public static long ConstructedSpanRoundTrip(long ticks) => new TimeSpan(ticks).Ticks;

    public static long MomentRoundTrip(long ticks) => new DateTime(ticks).Ticks;
}
