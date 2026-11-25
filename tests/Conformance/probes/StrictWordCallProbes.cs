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

    public static string VarConcatDeconstructionWrite()
    {
        var s = "a";
        (s, var k) = (Null(), 1);
        s += "d" + k;

        return s;
    }

    public static string VarConcatOtherSwitchSection()
    {
        var kind = Null() is null ? 2 : 1;
        string result = "";

        switch (kind)
        {
            case 1:
                var s = "a";
                result = s;
                break;
            case 2:
                s = Null()!;
                s += "z";
                result = s;
                break;
        }

        return result;
    }

    static void FillOut(out string? value) => value = null;

    static void FillRef(ref string? value) => value = null;

    public static string VarConcatOutArgument()
    {
        var s = "a";
        FillOut(out s!);
        s += "o";

        return s;
    }

    public static string VarConcatRefArgument()
    {
        var s = "a";
        FillRef(ref s!);
        s += "f";

        return s;
    }
}
