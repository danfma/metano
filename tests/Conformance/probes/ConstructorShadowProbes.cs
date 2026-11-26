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

public static class ConstructorShadowProbes
{
    public static string OutVariableNamedLikeACalledFunction()
    {
        var o = new CsOutShadow("12");

        return $"{o.C},{o.T},{o.Sum}";
    }
}
