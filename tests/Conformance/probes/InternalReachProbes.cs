using System.Collections.Generic;

namespace Conformance;

// Wave 9 group S: internal types reached from emitted code are transpiled (#770).

internal enum IrPhase
{
    Idle,
    Busy,
}

internal static class IrLedger
{
    private static readonly List<string> Entries = [];

    internal static void Write(string entry)
    {
        Entries.Add(entry);
        IrCounter.Count++;
    }

    internal static string Text() => string.Join(",", Entries) + "#" + IrCounter.Count;
}

internal static class IrCounter
{
    internal static int Count;
}

internal static class IrExtensions
{
    public static string Tagged(this IrPhase phase) => "<" + phase + ">";
}

public static class InternalReachProbes
{
    public static string InternalHelpersRun()
    {
        IrLedger.Write("a");
        IrLedger.Write(IrPhase.Busy.Tagged());

        return IrLedger.Text();
    }
}
