using Lib;

namespace Conformance;

/// <summary>A class named like a strict-mode reserved word with a static dispatcher.</summary>
public class @private
{
    public static string Make(int n) => "n" + n;

    public static string Make(string s) => "s" + s;
}

/// <summary>A derived class in another package than its escaped base.</summary>
public class AppLet : let
{
    public int Z = 1;

    public AppLet(int n)
        : base(() => "a" + n)
    {
        Z += n;
    }
}

public static class StrictWordClassProbes
{
    public static string StaticDispatcherOfAnEscapedClass() =>
        @private.Make(1) + "," + @private.Make("z");

    public static string StaticDispatcherAcrossPackages() =>
        let.Pick(1)
        + ","
        + let.Pick("z")
        + ","
        + LSignal.Describe(1)
        + ","
        + LSignal.Describe("a")
        + ","
        + new LSignal<int>(3).Value;

    public static string ConstructorSelectorOfAnEscapedBase()
    {
        var app = new AppLet(1);

        return app.K + "," + app.Z;
    }
}
