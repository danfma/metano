namespace Conformance;

/// <summary>Spec 001 FR-074 to FR-076 (#639 layer 3): int / uint arithmetic wraps in the default (unchecked) context.</summary>
public static class IntWrapProbes
{
    public enum Level
    {
        Low = 1,
        High = 40,
    }

    sealed class Box
    {
        public int Field;
        public uint Unsigned;

        public int Prop { get; set; }
    }

    static int max = 2147483647;
    static int min = -2147483648;

    static string Try(Func<string> run)
    {
        try
        {
            return run();
        }
        catch (Exception e)
        {
            return ProbeUtil.Kind(e);
        }
    }

    public static string AddSub()
    {
        var a = max;
        var b = min;
        var one = 1;

        return (a + one)
            + "|"
            + (b - one)
            + "|"
            + (a + a)
            + "|"
            + (b + b)
            + "|"
            + (-b)
            + "|"
            + (a - b);
    }

    public static string Multiply()
    {
        var a = 0x7FFFFFFF;
        var b = 0x7FFFFFF3;
        var c = 5;
        var big = 123456789;

        return (a * b)
            + "|"
            + (a * b + c)
            + "|"
            + (big * big)
            + "|"
            + (big * -big)
            + "|"
            + (a * 2)
            + "|"
            + (min * -1);
    }

    public static string UnsignedWrap()
    {
        uint zero = 0;
        uint one = 1;
        uint top = 4294967295;
        uint big = 3000000000;

        return (zero - one)
            + "|"
            + (top + one)
            + "|"
            + (big + big)
            + "|"
            + (big * 3u)
            + "|"
            + (top * top)
            + "|"
            + (big * big + one);
    }

    public static string Compound()
    {
        var i = max;
        i += 1;
        var j = min;
        j -= 1;
        var k = 65536;
        k *= 65536;
        uint u = 3000000000;
        u *= 3;
        uint v = 1;
        v -= 2;

        var box = new Box
        {
            Field = max,
            Unsigned = 0,
            Prop = min,
        };

        box.Field += 10;
        box.Unsigned -= 1;
        box.Prop -= 1;
        var arr = new[] { max, 7 };
        arr[0] += arr[1];
        arr[1] *= max;

        return i
            + "|"
            + j
            + "|"
            + k
            + "|"
            + u
            + "|"
            + v
            + "|"
            + box.Field
            + "|"
            + box.Unsigned
            + "|"
            + box.Prop
            + "|"
            + arr[0]
            + "|"
            + arr[1];
    }

    public static string IncDec()
    {
        var i = max;
        i++;
        var j = min;
        --j;
        var p = max;
        var before = p++;
        var q = max;
        var after = ++q;
        uint u = 0;
        u--;
        uint w = 0;
        var old = w--;
        var box = new Box { Field = max };
        box.Field++;
        var arr = new[] { min };
        arr[0]--;

        return i
            + "|"
            + j
            + "|"
            + before
            + "|"
            + p
            + "|"
            + after
            + "|"
            + u
            + "|"
            + old
            + "|"
            + w
            + "|"
            + box.Field
            + "|"
            + arr[0];
    }

    public static string Hash()
    {
        var text = "the quick brown fox jumps over the lazy dog";
        var h = 17;

        foreach (var ch in text)
            h = h * 31 + ch;

        uint fnv = 2166136261;

        foreach (var ch in text)
        {
            fnv ^= ch;
            fnv *= 16777619;
        }

        return h + "|" + fnv;
    }

    public static string Consumers()
    {
        var a = max;
        var b = 7;
        var c = 3;

        return ((a + b) & 0xFF)
            + "|"
            + (byte)(a + b)
            + "|"
            + (short)(a * b)
            + "|"
            + ((a + b) | 0)
            + "|"
            + ((a + b) >> 1)
            + "|"
            + ((a + b) >>> 1)
            + "|"
            + ((a + b) << 1)
            + "|"
            + ~(a + b)
            + "|"
            + (a - -b)
            + "|"
            + (a - (b + c))
            + "|"
            + (a - (b - c))
            + "|"
            + (-(a + 1))
            + "|"
            + (a + b + c - a * b)
            + "|"
            + (uint)(a + b);
    }

    public static string KeptWrap()
    {
        var a = max;
        var b = 1;

        long widened = a + b;
        double asDouble = a * 2;
        var halves = (a + b) / 2;
        var rest = (a + b) % 10;
        var negative = a + b < 0;
        var text = "v" + (a + b);
        var ok = Try(() => checked(b + unchecked(a + b)).ToString());

        return widened
            + "|"
            + asDouble
            + "|"
            + halves
            + "|"
            + rest
            + "|"
            + negative
            + "|"
            + text
            + "|"
            + ok;
    }

    public static string SmallOperands()
    {
        byte b1 = 200;
        byte b2 = 250;
        short s = 32767;
        ushort us = 65535;
        char c = 'z';

        return (b1 + b2)
            + "|"
            + (b1 * b2 * b1 * b2)
            + "|"
            + (s * s * 4)
            + "|"
            + (us * us)
            + "|"
            + (c * 70000 * 1000)
            + "|"
            + (c + 1)
            + "|"
            + (c - 'a');
    }

    public static string Lifted()
    {
        int? a = max;
        int? none = null;
        uint? u = 0;
        int? i = max;
        i++;
        int? j = min;
        j -= 1;

        return (a + 1)
            + "|"
            + (none + 1 == null)
            + "|"
            + (u - 1u)
            + "|"
            + i
            + "|"
            + j
            + "|"
            + (a * a);
    }

    public static string Enums()
    {
        var high = Level.High;
        var low = Level.Low;

        return (high - low) + "|" + (int)(high + 1) + "|" + ((int)high * max);
    }

    public static string Elisions()
    {
        var arr = new[] { 1, 2, 3 };
        var list = new List<int> { 4, 5 };
        var dict = new Dictionary<string, int> { ["a"] = 1 };
        var text = "abcd";
        var sum = 0;

        for (var i = 0; i < arr.Length; i++)
            sum += arr[i];

        for (var i = list.Count - 1; i >= 0; i--)
            sum += list[i];

        for (uint i = 10; i > 7; i--)
            sum += (int)i;

        for (var i = 0; i <= 2 && sum > 0; ++i)
            sum += i;

        return sum
            + "|"
            + (arr.Length - 1)
            + "|"
            + (list.Count - 1)
            + "|"
            + (dict.Count + 1)
            + "|"
            + (text.Length * 2)
            + "|"
            + (arr[arr.Length - 1]);
    }

    public static string CounterWritten()
    {
        var steps = 0;

        for (var i = max - 2; i > 0; i++)
        {
            steps++;

            if (steps > 5)
                break;
        }

        var n = 0;

        for (var i = 0; i < 10; i++)
        {
            i += 2;
            n++;
        }

        var last = 0;

        for (var i = max - 1; i != min + 1; i++)
            last = i;

        return steps + "|" + n + "|" + last;
    }
}
