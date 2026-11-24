using System.Numerics;
using System.Text;

namespace Conformance;

public record BmPoint(int X, int Y);

public class BmModuloComparer : IEqualityComparer<int>
{
    public bool Equals(int x, int y) => x % 3 == y % 3;

    public int GetHashCode(int value) => value % 3;
}

/// <summary>
/// BCL members that had no mapping (#686, #584, #617, #403, #687): the same text on .NET, Bun
/// and Node.
/// </summary>
public static class BclMemberProbes
{
    static string Safe<T>(Func<T> body)
    {
        try
        {
            return "" + body();
        }
        catch (Exception exception)
        {
            return exception.GetType().Name + ":" + exception.Message.Replace("\n", "|");
        }
    }

    public static string KeyValuePairCreate()
    {
        var pair = KeyValuePair.Create("z", 26);

        return pair.Key + pair.Value;
    }

    public static string BigIntegerConstants()
    {
        var sum = BigInteger.Zero + BigInteger.One + BigInteger.MinusOne + BigInteger.One;

        return sum.ToString()
            + BigInteger.Zero.IsZero
            + BigInteger.One.IsOne
            + new BigInteger(4).IsEven
            + new BigInteger(3).IsEven;
    }

    public static string CharCategories()
    {
        var chars = new[]
        {
            'a',
            'Z',
            '5',
            ' ',
            '_',
            '.',
            '+',
            '\n',
            '\t',
            'é',
            'ж',
            '٣',
            '½',
            ' ',
            '\u0085',
            ' ',
            '$',
        };
        var text = new StringBuilder();

        foreach (var c in chars)
        {
            text.Append(char.IsLetter(c) ? 'L' : '-')
                .Append(char.IsDigit(c) ? 'D' : '-')
                .Append(char.IsLetterOrDigit(c) ? 'A' : '-')
                .Append(char.IsNumber(c) ? 'N' : '-')
                .Append(char.IsUpper(c) ? 'U' : '-')
                .Append(char.IsLower(c) ? 'l' : '-')
                .Append(char.IsPunctuation(c) ? 'P' : '-')
                .Append(char.IsSymbol(c) ? 'S' : '-')
                .Append(char.IsSeparator(c) ? 'Z' : '-')
                .Append(char.IsControl(c) ? 'C' : '-')
                .Append(char.IsWhiteSpace(c) ? 'W' : '-')
                .Append(' ');
        }

        return text.ToString();
    }

    public static string CharAsciiTests()
    {
        var chars = new[]
        {
            'a',
            'c',
            'F',
            'g',
            'Z',
            '0',
            '9',
            '_',
            'é',
            '\u007F',
            '\u0080',
            '٣',
            ' ',
        };
        var text = new StringBuilder();

        foreach (var c in chars)
        {
            text.Append(char.IsAscii(c) ? 'a' : '-')
                .Append(char.IsAsciiDigit(c) ? 'd' : '-')
                .Append(char.IsAsciiLetter(c) ? 'l' : '-')
                .Append(char.IsAsciiLetterLower(c) ? 'w' : '-')
                .Append(char.IsAsciiLetterUpper(c) ? 'u' : '-')
                .Append(char.IsAsciiLetterOrDigit(c) ? 'o' : '-')
                .Append(char.IsAsciiHexDigit(c) ? 'h' : '-')
                .Append(char.IsAsciiHexDigitLower(c) ? 'x' : '-')
                .Append(char.IsAsciiHexDigitUpper(c) ? 'X' : '-')
                .Append(char.IsBetween(c, 'a', 'f') ? 'b' : '-')
                .Append(char.IsBetween(c, 'f', 'a') ? 'r' : '-')
                .Append(' ');
        }

        return text.ToString();
    }

    public static string ConvertToDouble()
    {
        int i = 7;
        decimal d = 2.5m;
        string text = "3.25";
        string? none = null;

        return Convert.ToDouble(i)
            + ","
            + Convert.ToDouble(d)
            + ","
            + Convert.ToDouble(true)
            + ","
            + Convert.ToDouble(text)
            + ","
            + Convert.ToDouble(none)
            + ","
            + decimal.ToDouble(d)
            + ","
            + Safe(() => Convert.ToDouble("abc"));
    }

    public static string NumericCompareTo()
    {
        short s1 = 5,
            s2 = -3;
        byte b1 = 200,
            b2 = 7;
        char c1 = 'a',
            c2 = 'z';
        uint u1 = 3,
            u2 = 4000000000;

        return 5.CompareTo(9)
            + ","
            + 9.CompareTo(5)
            + ","
            + 5.CompareTo(5)
            + ","
            + s1.CompareTo(s2)
            + ","
            + b1.CompareTo(b2)
            + ","
            + c1.CompareTo(c2)
            + ","
            + u1.CompareTo(u2)
            + ","
            + 1.5.CompareTo(2.5)
            + ","
            + double.NaN.CompareTo(1.0)
            + ","
            + 1.0.CompareTo(double.NaN)
            + ","
            + double.NaN.CompareTo(double.NaN)
            + ","
            + (-0.0).CompareTo(0.0);
    }

    public static string EqualityComparerDefault()
    {
        var comparer = EqualityComparer<BmPoint>.Default;
        var ints = EqualityComparer<int>.Default;

        return comparer.Equals(new BmPoint(1, 2), new BmPoint(1, 2))
            + ","
            + comparer.Equals(new BmPoint(1, 2), new BmPoint(1, 3))
            + ","
            + ints.Equals(4, 4)
            + ","
            + (comparer.GetHashCode(new BmPoint(1, 2)) == comparer.GetHashCode(new BmPoint(1, 2)));
    }

    public static string StringBuilderOperations()
    {
        var sb = new StringBuilder("x");

        sb.Append(1.5)
            .Append(true)
            .Append(7L)
            .Append('c')
            .Append("s")
            .Append('z', 3)
            .AppendLine("end");
        sb.Append(2).Append(-0.5).Append(12345678901L);
        sb.Insert(0, ">>").Remove(0, 1).Replace("z", "Z");

        var length = sb.Length;

        sb.Length = 4;

        var head = sb.ToString();

        sb.Clear();

        return length
            + "|"
            + head
            + "|"
            + sb.Length
            + "|"
            + new StringBuilder("hello").ToString(1, 3);
    }

    public static string StringBuilderFailures()
    {
        return Safe(() => new StringBuilder("ab").Insert(5, "x"))
            + ";"
            + Safe(() => new StringBuilder("ab").Remove(1, 5))
            + ";"
            + Safe(() => new StringBuilder("ab").Append('c', -1));
    }

    public static string LazyValues()
    {
        var runs = 0;
        var lazy = new Lazy<int>(() => ++runs);
        var before = lazy.IsValueCreated;
        var first = lazy.Value;
        var second = lazy.Value;
        var failing = new Lazy<int>(() => throw new InvalidOperationException("boom"));

        return before
            + ","
            + first
            + ","
            + second
            + ","
            + lazy.IsValueCreated
            + ","
            + runs
            + ","
            + Safe(() => failing.Value)
            + ","
            + Safe(() => failing.Value);
    }

    public static string ElementAtValues()
    {
        var xs = new List<int> { 10, 20, 30 };
        var ss = new List<string> { "a", "b" };
        var ns = new List<int?> { 1 };

        return xs.ElementAt(1)
            + ","
            + xs.Where(x => x > 10).ElementAt(1)
            + ","
            + xs.ElementAtOrDefault(2)
            + ","
            + xs.ElementAtOrDefault(3)
            + ","
            + xs.ElementAtOrDefault(-1)
            + ","
            + (ss.ElementAtOrDefault(5) ?? "null")
            + ","
            + (ns.ElementAtOrDefault(5)?.ToString() ?? "null")
            + ","
            + Enumerable.ElementAt(xs, 0);
    }

    public static string ElementAtFailures()
    {
        var xs = new List<int> { 10, 20, 30 };

        return Safe(() => xs.ElementAt(3))
            + ";"
            + Safe(() => xs.ElementAt(-1))
            + ";"
            + Safe(() => xs.Where(x => x > 10).ElementAt(2));
    }

    public static string DefaultIfEmptyValues()
    {
        var none = new List<int>();
        var some = new List<int> { 4, 5 };
        var words = new List<string>();

        return string.Join(" ", none.DefaultIfEmpty())
            + ","
            + string.Join(" ", none.DefaultIfEmpty(9))
            + ","
            + string.Join(" ", some.DefaultIfEmpty(9))
            + ","
            + words.DefaultIfEmpty().Count()
            + ","
            + (words.DefaultIfEmpty().First() ?? "null")
            + ","
            + words.DefaultIfEmpty("d").First()
            + ","
            + none.Where(x => x > 0).DefaultIfEmpty().Count();
    }

    public static string DistinctWithAComparer()
    {
        var xs = new List<int> { 1, 4, 2, 7, 5, 3 };

        return string.Join(" ", xs.Distinct(new BmModuloComparer()))
            + ","
            + string.Join(" ", xs.Distinct(EqualityComparer<int>.Default))
            + ","
            + string.Join(
                " ",
                new List<BmPoint> { new(1, 2), new(1, 2), new(2, 2) }
                    .Distinct()
                    .Select(p => p.X + ":" + p.Y)
            );
    }

    public static string StringBuilderInterpolatedHoles()
    {
        var sb = new StringBuilder();
        var flag = true;
        var single = 0.1f;
        var big = 1e21;
        var half = 2.5;

        sb.Append($"x{half}y{flag}z{single}w{big}").AppendLine($"{false}");

        return sb.ToString().Replace("\n", "|");
    }
}
