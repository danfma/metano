using System.Collections.Generic;

namespace Conformance;

// Instance initializers and dispatcher-constructor bodies whose stores bind
// temporaries: they lower over statement-list temporaries (W10-12), not IIFEs.

public class ItBox
{
    public int B = 5;
}

public static class ItBoxExts
{
    extension(ItBox c)
    {
        public int Ext
        {
            get => c.B;
            set => c.B = value;
        }
    }
}

public static class ItShared
{
    public static ItBox Box = new ItBox();

    public static int Calls;

    public static int NextIndex;

    public static readonly List<int> Items = [1, 2, 3, 4];

    public static ItBox Get()
    {
        Calls++;

        return Box;
    }

    public static int Next() => NextIndex++;
}

public class ItTwoCtors
{
    public int Inc { get; } = ItShared.Get().Ext += 1;

    public int Y;

    public ItTwoCtors(int v)
    {
        Y = (ItShared.Get().Ext += v);
    }

    public ItTwoCtors(string s)
    {
        Y = (ItShared.Get().Ext += s.Length);
    }
}

public class ItOneCtor
{
    public int Inc { get; } = ItShared.Get().Ext += 1;

    public int T = ItShared.Box.Ext++;

    public int Bumped = ItShared.Items[ItShared.Next()] += 10;

    public int Y;

    public ItOneCtor(int v)
    {
        Y = (ItShared.Get().Ext += v);
    }
}

public static class InitializerTemporaryProbes
{
    public static string StoresBindTheirReceiverOnce()
    {
        var a = new ItTwoCtors(3);
        var b = new ItTwoCtors("four");
        var c = new ItOneCtor(7);

        return string.Join(
            "/",
            a.Inc,
            a.Y,
            b.Inc,
            b.Y,
            c.Inc,
            c.T,
            c.Bumped,
            c.Y,
            ItShared.Box.B,
            ItShared.Calls,
            string.Join(",", ItShared.Items)
        );
    }
}
