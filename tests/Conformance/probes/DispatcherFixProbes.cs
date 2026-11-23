using System.Runtime.InteropServices;

namespace Conformance;

public class OptionalFirst
{
    public string Q([Optional] int a, int b) => "q" + a + b;

    public string R([Optional] string? a, int b) => "r" + (a ?? "null") + b;
}

public static class DispatcherFixProbes
{
    public static string OptionalBeforeRequired()
    {
        var o = new OptionalFirst();

        return o.Q(b: 2) + "," + o.R(b: 3);
    }
}
