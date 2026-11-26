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

public class CsOuter
{
    public int C = CsCalc.Reckon(1);
    public int Sum;

    public CsOuter(int reckon)
    {
        Sum = reckon;
    }

    public class Inner
    {
        public int C = CsCalc.Reckon(2);
        public int Sum;

        public Inner(int reckon)
        {
            var Reckon = reckon + 1;
            Sum = Reckon;
        }
    }
}

[EmitInFile("cs-pair")]
public class CsPairA
{
    public int C = CsCalc.Reckon(1);
    public int Sum;

    public CsPairA(int reckon)
    {
        Sum = reckon;
    }
}

[EmitInFile("cs-pair")]
public class CsPairB
{
    public int C = CsCalc.Reckon(2);
    public int Sum;

    public CsPairB(int reckon)
    {
        var Reckon = reckon + 1;
        Sum = Reckon;
    }
}

public struct CsPt
{
    public int X;
    public int Y;

    public CsPt(int x, int y)
    {
        X = x;
        Y = y;
    }
}

public sealed class CsStructHolder
{
    public CsPt Field;
    public CsPt Auto { get; set; }
    public CsPt Explicit = default;
}

[Name("csZzCopy")]
public class CsZzCopy
{
    public static readonly CsZzCopy Empty = new(5);

    public static int Seed = 4;

    public int V;

    public int Twice => V * CsZzCopy.Seed;

    public static CsZzCopy Make() => new(3);

    public CsZzCopy(int csZzCopy)
    {
        V = csZzCopy;
    }
}

public static class ConstructorShadowProbes
{
    public static string UninitializedStructMembersStartAtZero()
    {
        var h = new CsStructHolder();

        return $"{h.Explicit.X}:{h.Field.X}:{h.Auto.Y}:{h.Field.Equals(default(CsPt))}";
    }

    public static string NestedClassesShareOneAlias()
    {
        var a = new CsOuter(3);
        var b = new CsOuter.Inner(3);

        return $"{a.C},{a.Sum},{b.C},{b.Sum}";
    }

    public static string CoLocatedClassesShareOneAlias()
    {
        var a = new CsPairA(3);
        var b = new CsPairB(3);

        return $"{a.C},{a.Sum},{b.C},{b.Sum}";
    }

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

    public static string OwnTypeNamedLikeAParameterOutsideInitializers()
    {
        var made = CsZzCopy.Make();

        return $"{CsZzCopy.Empty.V},{CsZzCopy.Empty.Twice},{made.V},{made.Twice}";
    }
}
