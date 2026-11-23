using System.Runtime.InteropServices;
using Metano.Annotations;

namespace Conformance;

public class OptionalFirst
{
    public string Q([Optional] int a, int b) => "q" + a + b;

    public string R([Optional] string? a, int b) => "r" + (a ?? "null") + b;
}

[Branded]
public readonly record struct FixUserId(int Value)
{
    public static FixUserId Make(int v = 3) => new FixUserId(v);
}

public static class DispatcherFixProbes
{
    public static string BrandedStaticDefault() =>
        FixUserId.Make().Value + "," + FixUserId.Make(4).Value;

    public static string OptionalBeforeRequired()
    {
        var o = new OptionalFirst();

        return o.Q(b: 2) + "," + o.R(b: 3);
    }
}
