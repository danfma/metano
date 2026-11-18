using Metano.Annotations;

namespace Conformance;

public class UMoney
{
    public int C;

    public UMoney(int c)
    {
        C = c;
    }

    public static UMoney operator +(UMoney a, UMoney b) => new(a.C + b.C);

    public static UMoney operator -(UMoney a, UMoney b) => new(a.C - b.C);

    public static UMoney operator +(UMoney a) => new(a.C + 1000);

    public static UMoney operator ++(UMoney a) => new(a.C + 1);

    public static UMoney operator checked +(UMoney a, UMoney b) => new(checked(a.C + b.C) + 5);

    public static bool operator ==(UMoney? a, UMoney? b) =>
        a is null ? b is null : b is not null && a.C % 10 == b.C % 10;

    public static bool operator !=(UMoney? a, UMoney? b) => !(a == b);

    public static bool operator <(UMoney a, UMoney b) => a.C < b.C;

    public static bool operator >(UMoney a, UMoney b) => a.C > b.C;

    public static implicit operator UMoney(int c) => new(c * 10);

    public static implicit operator decimal(UMoney m) => m.C / 4m + 0.01m;

    public static implicit operator bool(UMoney m) => m.C > 0;

    public override bool Equals(object? o) => o is UMoney m && m.C == C;

    public override int GetHashCode() => C;
}

public struct UPt
{
    public int X;

    public UPt(int x)
    {
        X = x;
    }

    public static UPt operator +(UPt a, UPt b) => new(a.X + b.X);

    public static UPt operator -(UPt a) => new(-a.X);

    public static UPt operator ++(UPt a) => new(a.X + 1);

    public static bool operator ==(UPt a, UPt b) => a.X % 10 == b.X % 10;

    public static bool operator !=(UPt a, UPt b) => !(a == b);

    public static bool operator <(UPt a, UPt b) => a.X < b.X;

    public static bool operator >(UPt a, UPt b) => a.X > b.X;

    public static implicit operator UPt(int x) => new(x);

    public static explicit operator int(UPt p) => p.X * 2;

    public override bool Equals(object? o) => o is UPt p && p.X == X;

    public override int GetHashCode() => X;
}

public readonly struct UTick
{
    public readonly int T;

    public UTick(int t)
    {
        T = t;
    }

    public static implicit operator int(UTick t) => t.T;
}

public class UFeet
{
    public int F;

    public UFeet(int f)
    {
        F = f;
    }

    public static UFeet operator +(USpan s, UFeet f) => new(s.S + f.F);
}

public class USpan
{
    public int S;

    public USpan(int s)
    {
        S = s;
    }

    public static UFeet operator +(USpan s, int k) => new(s.S + k * 100);

    public static implicit operator USpan(UFeet f) => new(f.F + 1);
}

public class UNamed
{
    public int V;

    public UNamed(int v)
    {
        V = v;
    }

    [Name("combine")]
    public static UNamed operator +(UNamed a, UNamed b) => new(a.V * 100 + b.V);
}

public readonly struct UZPt
{
    public readonly int V;

    public UZPt(int v)
    {
        V = v;
    }

    public static explicit operator int(UZPt p) => p.V;

    public static explicit operator UZPt(int v) => new(v);
}

public readonly struct UZWide
{
    public readonly long V;

    public UZWide(long v)
    {
        V = v;
    }

    public static implicit operator long(UZWide w) => w.V;
}

/// <summary>
/// The use sites of user-defined operators and conversions (baseline FR-077,
/// FR-079): every application runs the operator C# bound.
/// </summary>
public static class OperatorUseProbes
{
    static string Of(UMoney? m) => m is null ? "null" : m.C.ToString();

    static string Of(UPt? p) => p is null ? "null" : p.Value.X.ToString();

    static int _calls;

    static UMoney Next()
    {
        _calls++;
        return new UMoney(_calls);
    }

    public static string BinaryAndUnary()
    {
        var a = new UMoney(5);
        var b = new UMoney(7);

        return Of(a + b)
            + "|"
            + Of(a - b)
            + "|"
            + Of(+a)
            + "|"
            + Of(a + 3)
            + "|"
            + (a < b)
            + "|"
            + (a > b)
            + "|"
            + Of(checked(a + b));
    }

    public static string Equality()
    {
        var a = new UMoney(5);
        var b = new UMoney(15);
        UMoney? none = null;

        return (a == b)
            + "|"
            + (a != b)
            + "|"
            + (none == null)
            + "|"
            + (null != a)
            + "|"
            + (a == none)
            + "|"
            + (none == none);
    }

    public static string CompoundAndIncrement()
    {
        var m = new UMoney(1);
        m += new UMoney(2);
        m -= 1;
        m++;
        var post = m++;
        var pre = ++m;
        var arr = new[] { new UMoney(1), new UMoney(2) };
        var i = 0;
        arr[i++] += new UMoney(40);
        arr[i++]++;

        return Of(m)
            + "|"
            + Of(post)
            + "|"
            + Of(pre)
            + "|"
            + Of(arr[0])
            + "|"
            + Of(arr[1])
            + "|"
            + i;
    }

    public static string CompoundWithConversions()
    {
        var sv = new USpan(1);
        sv += 2;

        return sv.S.ToString();
    }

    public static string LiftedStruct()
    {
        UPt? a = new UPt(3);
        UPt? none = null;
        UPt b = new UPt(13);

        return Of(a + b)
            + "|"
            + Of(none + b)
            + "|"
            + Of(-a)
            + "|"
            + Of(-none)
            + "|"
            + (a == b)
            + "|"
            + (a != b)
            + "|"
            + (none == null)
            + "|"
            + (none == none)
            + "|"
            + (a == none)
            + "|"
            + (a < b)
            + "|"
            + (none < b);
    }

    public static string LiftedIncrement()
    {
        UPt? a = new UPt(3);
        UPt? none = null;
        a++;
        none++;

        return Of(a) + "|" + Of(none);
    }

    public static string Conversions()
    {
        UMoney m = 7;
        UMoney fromChar = 'a';
        decimal d = m;
        d += m;
        long l = new UTick(9);
        int x = (int)new UPt(21);
        UPt p = 4;

        return Of(m) + "|" + Of(fromChar) + "|" + d + "|" + l + "|" + x + "|" + p.X;
    }

    public static string NullableConversions()
    {
        int? n = 3;
        int? none = null;
        UPt? lifted = n;
        UPt? liftedNone = none;
        UMoney? classTarget = (UMoney?)none;
        string thrown;

        try
        {
            var s = (UPt)none;
            thrown = "no throw " + s.X;
        }
        catch (InvalidOperationException)
        {
            thrown = "IOE";
        }

        return Of(lifted) + "|" + Of(liftedNone) + "|" + Of(classTarget) + "|" + thrown;
    }

    public static string Conditions()
    {
        var yes = new UMoney(1);
        var no = new UMoney(-1);
        var text = "";

        if (yes)
            text += "a";

        if (!no)
            text += "b";

        while (no)
            text += "never";

        text += yes ? "c" : "d";

        return text;
    }

    public static string Elements()
    {
        int[] raw = [1, 2];
        var total = new UMoney(0);

        foreach (UMoney m in raw)
            total += m;

        UMoney[] list = [3, new UMoney(4)];
        UMoney? maybe = null;
        UMoney chosen = maybe ?? 5;
        int? raw1 = 6;
        int? rawNone = null;
        UMoney left = raw1 ?? new UMoney(1);
        UMoney fallback = rawNone ?? new UMoney(2);

        return Of(total)
            + "|"
            + Of(list[0])
            + "|"
            + Of(list[1])
            + "|"
            + Of(chosen)
            + "|"
            + Of(left)
            + "|"
            + Of(fallback);
    }

    public static string EvaluationOrder()
    {
        _calls = 0;
        var sum = Next() - Next();

        return Of(sum) + "|" + _calls;
    }

    public static string PinnedAndCrossPackage()
    {
        var n = new UNamed(1) + new UNamed(2);
        var a = new Lib.LAmount(3) * 2;
        var b = a * new Lib.LAmount(2);
        var c = -b;

        return n.V + "|" + a.V + "|" + b.V + "|" + c.V;
    }

    public static string CastsAroundAConversion()
    {
        var narrow = (byte)new UZPt(300);
        var shortened = (short)new UZPt(70000);
        var widened = (long)(UZPt)8;
        var truncated = (int)new UZWide(5000000000L);
        var fromLong = (UZPt)(long)42;

        return narrow + "|" + shortened + "|" + widened + "|" + truncated + "|" + fromLong.V;
    }

    public static string CheckedCastAroundAConversion()
    {
        try
        {
            return checked((byte)new UZPt(300)).ToString();
        }
        catch (OverflowException)
        {
            return "OverflowException";
        }
    }
}
