using Metano.Annotations;

namespace Conformance;

// Wave 10 final-review fix round (W10-10): an instance initializer stored inside the
// constructor reads an imported binding that a constructor-scope name shadows.

[ExportedAsModule]
public static class CsCalc
{
    public static int Reckon(int x) => x * 7;

    public static int Third() => 11;

    public static bool TryGet(string s, out int v)
    {
        v = s.Length;
        return true;
    }
}

public class CsOutShadow
{
    public int C = CsCalc.Reckon(2);
    public int T = CsCalc.Third();
    public int Sum;

    public CsOutShadow(string s)
    {
        int.TryParse(s, out var third);

        if (CsCalc.TryGet(s, out var reckon))
            Sum = reckon + third;
    }
}

[Name("csWidget")]
public class CsWidget
{
    public int V = 3;

    public static int Make() => 9;

    public static int Count = 2;
}

[Name("csShade")]
public enum CsShade
{
    Light,
    Dark,
}

public static class CsHolder
{
    public static object O = new CsWidget();
}

public class CsEnumShadow
{
    public CsShade S = CsShade.Dark;
    public CsShade Zero;
    public int Sum;

    public CsEnumShadow(int csShade)
    {
        Sum = csShade;
    }
}

public class CsTypeShadow
{
    public CsWidget W = new CsWidget { V = 5 };
    public int M = CsWidget.Make();
    public int N = CsWidget.Count;
    public bool B = CsHolder.O is CsWidget;
    public Func<CsWidget> Factory = () => new CsWidget();
    public int Sum;

    public CsTypeShadow(int csWidget)
    {
        Sum = csWidget;
    }
}

public class CsPackageTypeShadow
{
    public Lib.LGadget G = new Lib.LGadget();
    public int M = Lib.LGadget.Make();
    public int Sum;

    public CsPackageTypeShadow(int gadget)
    {
        Sum = gadget;
    }
}

public static class ConstructorShadowProbes
{
    public static string EnumNamedLikeAParameter()
    {
        var e = new CsEnumShadow(8);

        return $"{(int)e.S},{(int)e.Zero},{e.S == CsShade.Dark},{e.Sum}";
    }

    public static string TypeNamedLikeAParameter()
    {
        var t = new CsTypeShadow(8);

        return $"{t.W.V},{t.M},{t.N},{t.B},{t.Factory().V},{t.Sum}";
    }

    public static string PackageTypeNamedLikeAParameter()
    {
        var p = new CsPackageTypeShadow(8);

        return $"{p.G.V},{p.M},{p.Sum}";
    }

    public static string OutVariableNamedLikeACalledFunction()
    {
        var o = new CsOutShadow("12");

        return $"{o.C},{o.T},{o.Sum}";
    }
}
