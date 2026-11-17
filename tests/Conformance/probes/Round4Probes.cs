namespace Conformance;

public class Slot
{
    public string? Text;
    public string Solid = "s";
    public string? Auto { get; set; }
    public Slot? Next;
}

public static class Round4Probes
{
    static int _calls;
    static Slot _slot = new Slot();

    static Slot Get()
    {
        _calls++;
        return _slot;
    }

    public static string CompoundReceiverOnce()
    {
        _calls = 0;
        _slot = new Slot();
        Get().Text += "x";
        Get().Solid += "y";
        Get().Auto += "z";
        Get().Text += null;
        return _slot.Text + "|" + _slot.Solid + "|" + _slot.Auto + "|" + _calls;
    }

    public static string CompoundIndexOnce()
    {
        var a = new string?[] { null, "b", null };
        var i = 0;
        a[i++] += "x";
        a[i++] += "y";
        a[i++] += null;
        var l = new List<string> { "p" };
        var j = 0;
        l[j++] += "q";
        return string.Join(",", a.Select(x => x ?? "null")) + "|" + i + "|" + l[0] + "|" + j;
    }

    public static string CompoundChain()
    {
        var s = new Slot { Next = new Slot() };
        s.Next!.Text += "a";
        s.Next.Text += "b";
        s.Next!.Next ??= new Slot();
        s.Next.Next.Solid += "!";
        return s.Next.Text + "|" + s.Next.Next.Solid;
    }

    public static string CompoundNonString()
    {
        var n = 1;
        n += 2;
        double d = 1;
        d += 0.5;
        long l = 1;
        l += 2;
        int? q = null;
        q += 1;
        var s = "a";
        s += 1;
        s += 'c';
        s += 1.5;
        s += true;
        s += null;
        return n + "|" + d.ToString() + "|" + l + "|" + (q == null ? "null" : "x") + "|" + s;
    }

    public static string CompoundInLambda()
    {
        var acc = "";
        string? part = null;
        new List<string?> { "a", null, "c" }.ForEach(x => acc += x);
        Action f = () => acc += part;
        f();
        return acc;
    }

    public static string CompoundStaticField()
    {
        _field = null;
        _field += "a";
        _field += null;
        _field += "b";
        return _field!;
    }

    static string? _field;

    public static string CompoundExprValue()
    {
        string? s = null;
        var r = (s += "a") + (s += null) + (s += "b");
        return r + "|" + s;
    }
}
