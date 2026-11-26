namespace Conformance;

public static class EmissionNoiseProbes
{
    const string Joined = "a" + "b";
    const string Interpolated = $"x{Joined}y";

    static void Quiet(int k)
    {
        if (k > 0)
            throw new InvalidOperationException("quiet" + k);
    }

    static string? Null() => null;

    public static string EmptyCatchSwallows()
    {
        var log = "";

        try
        {
            Quiet(1);
        }
        catch (Exception) { }

        try
        {
            Quiet(0);
            log += "no-throw;";
        }
        catch { }

        return log + "done";
    }

    public static string CatchVariableBindsTheMessage()
    {
        try
        {
            Quiet(2);

            return "none";
        }
        catch (Exception error)
        {
            return error.Message;
        }
    }

    public static string CatchVariableRebound()
    {
        try
        {
            Quiet(3);

            return "none";
        }
        catch (Exception error)
        {
            error = new Exception("rebound");

            return error.Message;
        }
    }

    public static string StringConstantsFold() =>
        Joined + "|" + Interpolated + "|" + ConstantLocal();

    static string ConstantLocal()
    {
        const string inner = "p" + "q";

        return inner;
    }

    public static string ElseIfChain()
    {
        var parts = new List<string>();

        for (var i = 0; i < 4; i++)
        {
            if (i == 0)
                parts.Add("zero");
            else if (i == 1)
                parts.Add("one");
            else if (i == 2)
                parts.Add("two");
            else
                parts.Add("many");
        }

        return string.Join(",", parts);
    }

    public static string IsNotNullTests()
    {
        string? none = Null();
        string? some = "x";

        return (none is not null) + "," + (some is not null);
    }

    public static string CompoundStringAppend()
    {
        var log = "a";
        log += "b";
        string? maybe = Null();
        string tail = "t";
        tail += maybe;

        return log + "|" + tail;
    }

    public static string NullableArrayElementRead()
    {
        var items = new string?[3];
        items[1] = "mid";

        return (items[0] is null) + "," + items[1] + "," + (items[2] is null);
    }

    public static string StrictModeNames()
    {
        var @static = 1;
        var package = 2;
        var @public = @static + package;
        var @interface = @public * 2;
        var arguments = @interface + 1;

        @private:
        while (true)
        {
            arguments += 1;

            break @private;
        }

        return Join(@static, package, @public, @interface, arguments);
    }

    static string Join(int @static, int @package, int @protected, int @implements, int arguments) =>
        @static + "," + package + "," + @protected + "," + @implements + "," + arguments;

    public static string NullForgivingLiteral()
    {
        IProbePort missing = null!;
        var viaDefault = Describe(default!);

        return (missing is null)
            + ","
            + Describe(null!)
            + ","
            + viaDefault
            + ","
            + Describe((IProbePort)null!);
    }

    static string Describe(IProbePort port) => port is null ? "null" : "port";

    public static string VarLocalReassignedWithNull()
    {
        var first = new ProbeNode(1) { Next = new ProbeNode(2) { Next = new ProbeNode(3) } };
        var current = first;
        var count = 0;

        while (current is not null)
        {
            count++;
            current = current.Next;
        }

        return count.ToString();
    }

    public static string UserOperatorConcatInit()
    {
        var s = new NullableConcatLeft() + "a";
        s += "b";

        return s;
    }

    public static string UserOperatorConcatAssign()
    {
        var s = "x";
        s = new NullableConcatLeft() + "a";
        s += "b";

        return s;
    }

    public static string UserOperatorCompoundConcat()
    {
        var s = "a";
        s += new NullableConcatRight();
        s += "b";

        return s;
    }
}

public sealed class NullableConcatLeft
{
    public string? Value;

    public static string? operator +(NullableConcatLeft left, string right) =>
        left.Value == null ? null : left.Value + right;
}

public sealed class NullableConcatRight
{
    public static string? operator +(string left, NullableConcatRight right) => null;
}

public interface IProbePort
{
    void Run();
}

public sealed class ProbeNode(int value)
{
    public int Value { get; } = value;

    public ProbeNode? Next { get; set; }
}
