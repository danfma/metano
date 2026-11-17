namespace Conformance;

/// <summary>Spec 001 FR-071 to FR-073 (#501): a char read as its UTF-16 code unit, stores back into a char, lone surrogates.</summary>
public static class CharCodeProbes
{
    static string Pick(int i) => "int" + i;

    static string Pick(string s) => "str" + s;

    static string Try(Func<string> run)
    {
        try
        {
            return run();
        }
        catch (OverflowException)
        {
            return "OverflowException";
        }
    }

    public static string ToInt()
    {
        var s = "Az";
        int a = s[0];
        int b = (int)s[1];
        ushort u = s[0];
        long l = s[0];
        double d = s[1];
        decimal m = s[0];

        return a + "|" + b + "|" + u + "|" + l + "|" + d + "|" + m;
    }

    public static string Arithmetic()
    {
        char c = 'a';
        char z = 'z';

        return (c + 1)
            + "|"
            + (c - 'a')
            + "|"
            + (z - c)
            + "|"
            + (c * 2)
            + "|"
            + (-c)
            + "|"
            + (+c)
            + "|"
            + (~c)
            + "|"
            + (c / 2)
            + "|"
            + (c % 10)
            + "|"
            + (c & 0xF)
            + "|"
            + (c << 1)
            + "|"
            + (c + z)
            + "|"
            + (c * 1.5);
    }

    public static string Compare()
    {
        char c = 'm';
        int n = 100;
        string r = "";
        r += c < n ? "1" : "0";
        r += c == 109 ? "1" : "0";
        r += c >= 'a' && c <= 'z' ? "1" : "0";
        r += 'm' == c ? "1" : "0";
        r += c != 'x' ? "1" : "0";
        r += c is >= 'a' and <= 'z' ? "1" : "0";
        char hi = '￿';
        r += hi > c ? "1" : "0";

        return r;
    }

    public static string NarrowCompare()
    {
        char l = '\u0141';
        char a = 'A';
        char hi = '\uFFFF';
        char lo = 'a';
        string r = "";
        r += unchecked((byte)l) == unchecked((byte)a) ? "1" : "0";
        r += unchecked((short)hi) < unchecked((short)lo) ? "1" : "0";
        r += unchecked((sbyte)l) == unchecked((sbyte)a) ? "1" : "0";
        r += (int)l > (int)a ? "1" : "0";
        r += (long)l > (long)a ? "1" : "0";
        r += (double)l > (double)a ? "1" : "0";
        r += (decimal)l > (decimal)a ? "1" : "0";
        r += (ushort)hi > (ushort)lo ? "1" : "0";

        return r;
    }

    public static string IntPatterns()
    {
        string r = "";

        foreach (int i in new[] { 97, 98, 120, 5 })
        {
            r += i switch
            {
                'a' => "A",
                >= 'b' and <= 'w' => "mid",
                >= 'x' => "hi",
                _ => "?",
            };

            r += (i is 'a' or 'b') ? "T" : "F";
            r += i == 'x' ? "X" : "-";

            switch (i)
            {
                case 'b':
                    r += "B";
                    break;
                default:
                    r += ".";
                    break;
            }
        }

        return r;
    }

    public static string Narrow()
    {
        char c = 'a';
        char next = (char)(c + 1);
        char fromInt = (char)98;
        int n = 65601;
        char wrapped = unchecked((char)n);
        short neg = -1;
        char fromShort = unchecked((char)neg);
        uint big = 0x1_0041;
        char fromUint = unchecked((char)big);
        byte b = unchecked((byte)'Ł');
        sbyte sb = unchecked((sbyte)'ÿ');
        short sh = unchecked((short)'￿');

        return next
            + "|"
            + fromInt
            + "|"
            + (int)wrapped
            + "|"
            + (int)fromShort
            + "|"
            + (int)fromUint
            + "|"
            + b
            + "|"
            + sb
            + "|"
            + sh;
    }

    public static string Stores()
    {
        char c = 'a';
        c++;
        ++c;
        c--;
        c += (char)2;
        c -= (char)1;
        c |= (char)0x20;
        c <<= 1;
        char top = '￿';
        top++;
        char bottom = '\0';
        bottom--;
        char hi = '￾';
        hi += (char)3;

        return (int)c + "|" + (int)top + "|" + (int)bottom + "|" + (int)hi;
    }

    public static string PostfixValue()
    {
        char c = 'a';
        char old = c++;
        char pre = ++c;
        var a = new char[] { 'x', 'y' };
        int i = 0;
        char fromArray = a[i++]++;

        return old + "" + pre + c + fromArray + a[0] + a[1] + i;
    }

    public static string Checked()
    {
        return Try(() =>
            {
                char c = '￿';
                checked
                {
                    c++;
                }

                return "no throw " + (int)c;
            })
            + "|"
            + Try(() =>
            {
                char c = '￿';
                checked
                {
                    c += (char)1;
                }

                return "no throw " + (int)c;
            })
            + "|"
            + Try(() =>
            {
                int n = 70000;

                return "no throw " + (int)checked((char)n);
            })
            + "|"
            + Try(() =>
            {
                int n = -1;

                return "no throw " + (int)checked((char)n);
            })
            + "|"
            + Try(() =>
            {
                char big = 'Ł';

                return "no throw " + checked((byte)big);
            })
            + "|"
            + Try(() =>
            {
                char ok = 'a';

                return "ok " + (int)checked((char)(ok + 1));
            });
    }

    public static string Text()
    {
        char c = 'a';
        string s = "s";

        return ("x" + c)
            + "|"
            + (c + "x")
            + "|"
            + $"[{c}]"
            + "|"
            + c.ToString()
            + "|"
            + (s + c + c)
            + "|"
            + ("" + c + 'b')
            + "|"
            + (c + 'b');
    }

    public static string Positions()
    {
        char c = 'a';
        Func<int> lambda = () => c;
        var ints = new List<int>();
        ints.Add(c);
        int[] spread = [.. "Hi"];
        double[] spreadD = [.. "Hi"];
        int t = 0;

        foreach (int x in "Hi")
            t += x;

        return lambda()
            + "|"
            + ints[0]
            + "|"
            + string.Join(",", spread)
            + "|"
            + string.Join(",", spreadD)
            + "|"
            + t
            + "|"
            + (true ? c : 0)
            + "|"
            + (false ? c : 'b')
            + "|"
            + Math.Max(c, 'z');
    }

    public static string Dispatch()
    {
        char c = 'a';
        string s = "a";

        return Pick(c) + "|" + Pick(s) + "|" + Pick((int)c);
    }

    public static string Foreach()
    {
        int t = 0;

        foreach (var c in "Hi")
            t += c;

        int u = 0;

        foreach (char c in new double[] { 65.9, 66 })
            u += c;

        string text = "";

        foreach (char c in new[] { 72, 105 })
            text += c;

        return t + "|" + u + "|" + text;
    }

    public static string Wide()
    {
        char c = 'a';
        long l = 1;
        int i = 5;
        long r = c + i;
        long shifted = l << c;
        ulong ul = c;
        long sum = c + 10L;

        return r + "|" + shifted + "|" + ul + "|" + sum;
    }

    public static string Lifted()
    {
        char? some = 'a';
        char? none = null;
        int? plus = some + 1;
        int? plusNone = none + 1;
        int? cast = (int?)some;

        return plus
            + "|"
            + plusNone
            + "|"
            + cast
            + "|"
            + (some < 5)
            + "|"
            + (none < 5)
            + "|"
            + (none <= none)
            + "|"
            + (some <= some)
            + "|"
            + (some == none)
            + "|"
            + (none == none)
            + "|"
            + (some > none);
    }

    public static string LoneSurrogates()
    {
        string lone = "a\uD83Db";
        string pairAfterLone = "\uDE00😀";
        char c = '\uD83D';
        string interpolated = $"x{'\uDE00'}y\uD83D";

        return lone.Length
            + "|"
            + (int)lone[1]
            + "|"
            + pairAfterLone.Length
            + "|"
            + (int)pairAfterLone[0]
            + "|"
            + (int)pairAfterLone[1]
            + "|"
            + (int)c
            + "|"
            + interpolated.Length
            + "|"
            + (int)interpolated[1]
            + "|"
            + (int)interpolated[^1]
            + "|"
            + "😀".Length;
    }
}
