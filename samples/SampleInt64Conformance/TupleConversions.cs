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

    /// <summary>
    /// A missing element read into a non-nullable tuple variable throws where
    /// it is read, before the body runs for it: the count says how many
    /// elements the body saw.
    /// </summary>
    public static long NullableIteratedStop(int a, int b)
    {
        var pairs = new List<(int, int)?> { (a, b), null, (b, a) };
        var count = 0;
        long total = 0;

        try
        {
            // The missing element is the row: C# warns about the read it tests.
#pragma warning disable CS8629
            foreach ((long, long) pair in pairs)
#pragma warning restore CS8629
            {
                count++;
                total += pair.Item1;
            }
        }
        catch (InvalidOperationException)
        {
            return total - count;
        }

        return total;
    }

    public static int IteratedNarrowedSum(long a)
    {
        var pairs = new List<(long, long)> { (a, a) };
        var total = 0;

        foreach ((int, int) pair in pairs)
            total ^= pair.Item1 ^ (pair.Item2 >> 1);

        return total;
    }

    public static int IteratedNarrowedCheckedSum(long a)
    {
        checked
        {
            var pairs = new List<(long, long)> { (a, a) };
            var total = 0;

            foreach ((int, int) pair in pairs)
                total ^= pair.Item1 ^ (pair.Item2 >> 1);

            return total;
        }
    }

    // The missing element is the row: C# warns about the cast it tests.
#pragma warning disable CS8629
    /// <summary>
    /// A cast tuple literal converts each element where it is written: the
    /// missing first element throws before the second one runs, so the calls
    /// counted stop at one.
    /// </summary>
    public static int LiteralCastStopsAtTheMissingElement(int a)
    {
        var calls = 0;

        Func<int?> missing = () =>
        {
            calls++;

            return null;
        };

        Func<int> next = () =>
        {
            calls += 100;

            return a;
        };

        try
        {
            var wide = ((long, long))(missing(), next());

            return (int)(wide.Item1 + wide.Item2);
        }
        catch (InvalidOperationException)
        {
            return -calls;
        }
    }
#pragma warning restore CS8629

    public static long LiteralCastSum(int a, int b)
    {
        int? first = a;
        var wide = (((long, long), long))((first, b), a);

        return wide.Item1.Item1 + wide.Item1.Item2 + wide.Item2;
    }

    public static int LiteralCastNarrowed(long a)
    {
        var narrow = ((int, int))(a, 5L);

        return narrow.Item1 ^ (narrow.Item2 << 4);
    }

    public static int LiteralCastNarrowedChecked(long a)
    {
        var narrow = checked(((int, int))(a, 5L));

        return narrow.Item1 ^ (narrow.Item2 << 4);
    }

    /// <summary>
    /// A missing nullable tuple element read into a nullable slot stays
    /// missing: the conversion lifts instead of unwrapping.
    /// </summary>
    public static long LiteralCastKeepsTheMissingTuple(int a)
    {
        Func<(int, int)?> missing = () => null;
        var wide = (((long, long)?, long))(missing(), a);

        return wide.Item1 is null ? wide.Item2 : -1;
    }

    // The missing element is the row: C# warns about the cast it tests.
#pragma warning disable CS8629
    public static int LiteralCastThrowsOnTheMissingLong(long a)
    {
        Func<long?> missing = () => null;

        try
        {
            var wide = ((double, double))(a, missing());

            return wide.Item2 > 0 ? 1 : 2;
        }
        catch (InvalidOperationException)
        {
            return -1;
        }
    }
#pragma warning restore CS8629

    public static long LiteralCastOfWrappedValues(int a)
    {
        var wide = ((long, long))((int?)2, (int?)a);

        return wide.Item1 + wide.Item2;
    }

    public static long LiteralCastWithDefault(int a)
    {
        int? first = a;
        var wide = ((long, long?))(first, default);

        return wide.Item1 + (wide.Item2 ?? 1);
    }

    /// <summary>
    /// A cast literal under an implicit conversion: the cast narrows each
    /// element and the position it is read from widens the result again.
    /// </summary>
    public static long CastLiteralInitializedSum(long a)
    {
        (long, long) wide = ((int, int))(a, a);

        return wide.Item1 + wide.Item2;
    }

    public static long CastLiteralArgumentSum(long a)
    {
        Func<(long, long), long> sum = pair => pair.Item1 + pair.Item2;

        return sum(((int, int))(a, a));
    }

    public static long CastLiteralReturnedSum(long a)
    {
        Func<(long, long)> narrowed = () => ((int, int))(a, a);
        var wide = narrowed();

        return wide.Item1 + wide.Item2;
    }

    public static long CastLiteralNullableSum(long a)
    {
        (long, long)? wide = ((int, int))(a, a);

        return wide is { } pair ? pair.Item1 + pair.Item2 : -1;
    }

    public static long CastLiteralElementSum(long a)
    {
        List<(long, long)> pairs = [((int, int))(a, a)];

        return pairs[0].Item1 + pairs[0].Item2;
    }

    public static long CastLiteralConditionalSum(long a)
    {
        (long, long) wide = a > 0 ? ((int, int))(a, a) : (5L, 6L);

        return wide.Item1 + wide.Item2;
    }

    public static long CastLocalTupleSum(long a)
    {
        (long, long) source = (a, a);
        (long, long) wide = ((int, int))source;

        return wide.Item1 + wide.Item2;
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
