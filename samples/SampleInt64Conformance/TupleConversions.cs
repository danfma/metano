using System;
using System.Collections.Generic;
using Metano.Annotations;

namespace SampleInt64Conformance;

/// <summary>
/// The tuple and spread rows of the reference matrix (spec 012 FR-010): C#
/// converts a tuple element by element and a spread element by element, so
/// an <c>(int, int)</c> read as a <c>(long, long)</c>, compared with one or
/// spread into a 64-bit collection becomes <c>bigint</c> values at each
/// position Roslyn reports the conversion.
/// </summary>
[NoContainer]
public static class TupleConversions
{
    public static bool EqualsOperator(int a, int b)
    {
        (long, long) wide = (a, b);
        (int, int) narrow = (a, b);

        return wide == narrow && narrow == wide;
    }

    public static bool NotEqualsOperator(int a, int b)
    {
        (long, long) wide = (a, b);
        (int, int) narrow = (a, b);

        return wide != narrow || narrow != wide;
    }

    public static bool EqualsLiteral(int a, int b)
    {
        (long, long) wide = (a, b);

        return wide == (a, b);
    }

    public static bool EqualsMethod(int a, int b)
    {
        (long, long) wide = (a, b);
        (int, int) narrow = (a, b);

        return wide.Equals(narrow);
    }

    public static long AssignedSum(int a, int b)
    {
        (int, int) narrow = (a, b);
        (long, long) wide = narrow;

        return wide.Item1 + wide.Item2;
    }

    public static long ReturnedSum(int a, int b)
    {
        Func<(int, int), (long, long)> widen = pair => pair;
        (int, int) narrow = (a, b);
        var wide = widen(narrow);

        return wide.Item1 + wide.Item2;
    }

    public static long ArgumentSum(int a, int b)
    {
        Func<(long, long), long> sum = pair => pair.Item1 + pair.Item2;
        (int, int) narrow = (a, b);

        return sum(narrow);
    }

    public static long NestedSum(int a, int b)
    {
        ((int, int), int) narrow = ((a, b), a);
        ((long, long), long) wide = narrow;

        return wide.Item1.Item1 + wide.Item1.Item2 + wide.Item2;
    }

    public static long NamedSum(int a, int b)
    {
        (int x, int y) narrow = (a, b);
        (long x, long y) wide = narrow;

        return wide.x + wide.y;
    }

    public static string MixedText(int a, int b)
    {
        (int, string) narrow = (a, b.ToString());
        (long, string) wide = narrow;

        return (wide.Item1 + 1) + wide.Item2;
    }

    public static long NullableElementSum(int a, int b)
    {
        (int?, int) narrow = (a, b);
        (long?, long) wide = narrow;

        return (wide.Item1 ?? 0) + wide.Item2;
    }

    public static bool NullableElementStaysNull(int b)
    {
        (int?, int) narrow = (null, b);
        (long?, long) wide = narrow;

        return wide.Item1 is null && wide.Item2 == b;
    }

    public static long NullableTupleSum(int a, int b)
    {
        (int, int)? narrow = (a, b);
        (long, long)? wide = narrow;

        return wide is { } pair ? pair.Item1 + pair.Item2 : -1;
    }

    public static bool NullableTupleStaysNull()
    {
        (int, int)? narrow = null;
        (long, long)? wide = narrow;

        return wide is null;
    }

    public static int ExplicitNarrowed(long a)
    {
        (long, long) wide = (a, a);
        var narrow = ((int, int))wide;

        return narrow.Item1;
    }

    public static long IteratedSum(int a, int b)
    {
        var pairs = new List<(int, int)> { (a, b), (b, a) };
        long total = 0;

        foreach ((long, long) pair in pairs)
            total += pair.Item1 - pair.Item2 + pair.Item1;

        return total;
    }

    public static long NullableLiteralSum(int a, int b)
    {
        (long, long)? wide = (a, b);

        return wide is { } pair ? pair.Item1 + pair.Item2 : -1;
    }

    public static long SpreadListSum(int a, int b)
    {
        var ints = new List<int> { a, b };
        List<long> longs = [.. ints];

        return longs[0] + longs[1];
    }

    public static bool SpreadListContains(int a, int b)
    {
        var ints = new List<int> { a, b };
        List<long> longs = [.. ints];

        return longs.Contains(a);
    }

    public static bool SpreadSetContains(int a, int b)
    {
        var ints = new[] { a, b };
        HashSet<long> longs = [.. ints];

        return longs.Contains(b);
    }

    public static long SpreadArraySum(int a, int b)
    {
        var ints = new[] { a, b };
        long[] longs = [.. ints, 3];

        return longs[0] + longs[1] + longs[2];
    }

    public static long SpreadTupleSum(int a, int b)
    {
        var pairs = new List<(int, int)> { (a, b) };
        List<(long, long)> wide = [.. pairs];

        return wide[0].Item1 + wide[0].Item2;
    }
}
