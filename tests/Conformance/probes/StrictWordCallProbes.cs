namespace Conformance;

public static class StrictWordCallProbes
{
    static string? Null() => null;

    public static string ModuleCallFromAnotherFile() => StrictWords.Package("k") + "k".Static();

    public static string LocalAndFunctionOfTheSameWord()
    {
        var @package = "v";

        return @package + StrictWords.Package("k");
    }

    public static string ReservedClassName() => new @implements().V.ToString();

    public static string VarConcatInLambda()
    {
        var s = "a";
        Action act = () => s += "q";
        s = null!;
        act();
        var t = "b";
        Action local = () =>
        {
            t += "r";
        };
        t = Null()!;
        local();

        return s + "|" + t;
    }

    public static string VarConcatArrayHole()
    {
        var arr = new string[2];
        var s4 = arr[0];
        s4 += "w";

        return s4;
    }
}
