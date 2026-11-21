using System;
using System.Collections.Generic;
using Metano.Annotations;

namespace Conformance;

[Transpile]
internal interface IRtReifiedChecker
{
    bool Check<T>(object value);
}

[Transpile]
internal class RtReifiedBase : IRtReifiedChecker
{
    public virtual bool Check<T>(object value) => false;

    public virtual string Check(string value) => "str:" + value;
}

[Transpile]
internal class RtReifiedDerived : RtReifiedBase
{
    public override bool Check<T>(object value) => value is T;
}

public static class ReifiedTypeParameterProbes
{
    private static bool IsA<T>(object value) => value is T;

    private static T? AsA<T>(object value)
        where T : class => value as T;

    private static T CastA<T>(object value) => (T)value;

    private static string TypeNameOf<T>() => typeof(T).Name;

    private static bool OuterIsA<U>(object value) => IsA<U>(value);

    private static string TryOptionalParams<T>(object value, int n = 1, params string[] rest) =>
        (value is T) + ":" + n + ":" + rest.Length;

    public static string IsTypePrimitivesAndClasses()
    {
        object num = 42;
        object text = "hi";
        object circle = new RtCircle();
        object list = new List<string> { "a" };

        return IsA<int>(num)
            + "|"
            + IsA<string>(num)
            + "|"
            + IsA<string>(text)
            + "|"
            + IsA<RtCircle>(circle)
            + "|"
            + IsA<IRtArea>(circle)
            + "|"
            + IsA<RtSquare>(circle)
            + "|"
            + IsA<List<string>>(list)
            + "|"
            + IsA<List<int>>(list);
    }

    public static string AsAndCastBehaviour()
    {
        object circle = new RtCircle();
        object square = new RtSquare();

        var hit = AsA<RtCircle>(circle) != null;
        var miss = AsA<RtCircle>(square) == null;
        var castOk = CastA<IRtArea>(circle).Area;
        var threw = false;

        try
        {
            CastA<RtCircle>(square);
        }
        catch (InvalidCastException)
        {
            threw = true;
        }

        return hit + "|" + miss + "|" + castOk + "|" + threw;
    }

    public static string TypeOfParameter()
    {
        return TypeNameOf<RtCircle>() + "|" + TypeNameOf<int>();
    }

    public static string OverrideAndOverloadDispatch()
    {
        IRtReifiedChecker iface = new RtReifiedDerived();
        RtReifiedBase @base = new RtReifiedDerived();
        object circle = new RtCircle();
        object square = new RtSquare();

        return iface.Check<RtCircle>(circle)
            + "|"
            + iface.Check<RtCircle>(square)
            + "|"
            + @base.Check<RtCircle>(circle)
            + "|"
            + @base.Check("ok");
    }

    public static string MethodGroupDelegate()
    {
        Func<object, bool> f = IsA<RtCircle>;

        return f(new RtCircle()) + "|" + f(new RtSquare());
    }

    public static string OptionalAndParamsPlacement()
    {
        object circle = new RtCircle();

        return TryOptionalParams<RtCircle>(circle)
            + "|"
            + TryOptionalParams<RtCircle>(circle, 7, "a", "b");
    }

    public static string TransitiveForwarding()
    {
        return OuterIsA<RtCircle>(new RtCircle()) + "|" + OuterIsA<RtCircle>(new RtSquare());
    }
}
