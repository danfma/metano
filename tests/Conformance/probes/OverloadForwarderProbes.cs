using System;
using Metano.Annotations;

namespace Conformance;

/// <summary>
/// A dispatcher left with one reachable overload is a typed forwarder (#799):
/// a call the compiler cannot bind (a <c>dynamic</c> receiver) goes through it
/// and reaches the overload with its defaults. A <c>dynamic</c> member call
/// keeps the C# spelling, so the group is spelled as TypeScript names it.
/// </summary>
public class LonePicker
{
    public virtual string pick(Func<int, int> f, int x = 3) => "fx:" + f(x);

    [Name("text")]
    public virtual string pick(Func<string, int> f, int x = 4) => "text:" + f("ab") + x;
}

public class LonePickerOverride : LonePicker
{
    public override string pick(Func<int, int> f, int x = 3) => "over:" + f(x);
}

public static class OverloadForwarderProbes
{
    public static string DynamicCallUsesTheDefault()
    {
        Func<int, int> twice = v => v * 2;
        dynamic picker = new LonePicker();

        return (string)picker.pick(twice);
    }

    public static string DynamicCallPassesTheArgument()
    {
        Func<int, int> twice = v => v * 2;
        dynamic picker = new LonePicker();

        return (string)picker.pick(twice, 5);
    }

    public static string DynamicCallReachesTheOverride()
    {
        Func<int, int> twice = v => v * 2;
        dynamic picker = new LonePickerOverride();

        return (string)picker.pick(twice);
    }

    public static string BoundCallReachesThePinnedOverload()
    {
        var picker = new LonePicker();

        return picker.pick((string s) => s.Length) + "," + picker.pick(v => v + 1);
    }
}
