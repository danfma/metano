using System;
using System.Collections.Generic;
using Metano.Annotations;

namespace Conformance;

[Transpile]
public enum W5Color
{
    Red = 0,
    Green = 1,
}

[Transpile]
public enum W5NonZero
{
    A = 1,
    B = 2,
}

[Transpile]
[Flags]
public enum W5FlagZero
{
    None = 0,
    Read = 1,
}

[Transpile]
public enum W5BigEnum : long
{
    A = 1,
}

[Transpile]
[StringEnum]
public enum W5Status
{
    Active,
    Inactive,
}

[Transpile]
public readonly record struct W5RecPoint(int X, int Y);

[Transpile]
public struct W5CtorStruct
{
    public int X;

    public W5CtorStruct()
    {
        X = 5;
    }
}

[Transpile]
public struct W5GenericStruct<T>
{
    public int Tag;

    public bool Is(object o) => o is T;
}

[Transpile]
public struct W5OuterGenericStruct<T>
{
    public W5GenericStruct<T> G;
    public W5GenericStruct<int> G2;
}

[Transpile]
public struct W5InnerStruct
{
    public int X;
}

[Transpile]
public struct W5OuterStruct
{
    public W5InnerStruct F;
}

[Transpile]
public class W5ParamsSum<T>
{
    public string First;

    public W5ParamsSum(string label, params int[] items)
    {
        First = label + ":" + (items.Length > 0 ? items[0] : -1);
    }

    public bool Is(object o) => o is T;
}

[Transpile]
public class W5OverBox<T>
{
    public string Which;

    public W5OverBox()
    {
        Which = "empty";
    }

    public W5OverBox(int x)
    {
        Which = "int:" + x;
    }

    public bool Is(object o) => o is T;
}

public static class Wave5Fix1Probes
{
    private static T? Zero<T>()
    {
        var r = typeof(T);
        return default;
    }

    private static T Make<T>()
        where T : new() => new T();

    private static bool IsA<T>(object value) => value is T;

    public static string EnumDefaults()
    {
        var c = (int)(object)Zero<W5Color>()!;
        var nz = (int)(object)Zero<W5NonZero>()!;
        var fz = Zero<W5FlagZero>() == W5FlagZero.None;
        var big = (long)(object)Zero<W5BigEnum>()!;
        var st = Zero<W5Status>() == W5Status.Active;

        return $"{c}|{nz}|{fz}|{big}|{st}";
    }

    public static string StructDefaultsAndCreate()
    {
        var recZero = Zero<W5RecPoint>();
        var recEq = recZero == new W5RecPoint(0, 0);
        var ctorZero = Zero<W5CtorStruct>().X;
        var recMake = Make<W5RecPoint>().X;
        W5GenericStruct<int> gs = default;

        return $"{recZero.X}|{recZero.Y}|{recEq}|{ctorZero}|{recMake}|{gs.Is(1)}|{gs.Is("a")}";
    }

    public static string ExplicitCtorStructNewT()
    {
        return Make<W5CtorStruct>().X.ToString();
    }

    public static string ParamsCtorWithSlot()
    {
        var p = new W5ParamsSum<int>("L", 10, 20);
        return $"{p.First}|{p.Is(1)}|{p.Is("a")}";
    }

    public static string OverloadedGenericCreateAndSlot()
    {
        var empty = Make<W5OverBox<object>>().Which;
        var withInt = new W5OverBox<int>(5);
        return $"{empty}|{withInt.Is(1)}|{withInt.Is("a")}";
    }

    public static string DecimalAndTemporalDefaults()
    {
        var d0 = Zero<decimal>() == 0m;
        var d1 = Make<decimal>() == 0m;
        var date0 = Zero<DateOnly>().Year;
        var time0 = Make<TimeOnly>().Hour;

        return $"{d0}|{d1}|{date0}|{time0}";
    }

    public static string UndescribableReifiedArgumentsThrow()
    {
        Func<string> g = () => "hi";

        return string.Join(
            "|",
            Probe(() => IsA<(int, string)>("x")),
            Probe(() => IsA<KeyValuePair<string, int>>("s")),
            Probe(() => IsA<IFormattable>("s")),
            Probe(() => IsA<int[]>(new string[] { "a" })),
            Probe(() => IsA<Func<int>>(g)),
            Probe(() => IsA<System.Text.StringBuilder>("str")),
            Probe(() => IsA<DayOfWeek>(1))
        );
    }

    public static string UndescribableReifiedNullsReturnFalse() =>
        string.Join(
            "|",
            Probe(() => IsA<IFormattable>(null!)),
            Probe(() => IsA<(int, string)>(null!)),
            Probe(() => IsA<Func<int>>(null!)),
            Probe(() => IsA<int[]>(null!)),
            Probe(() => IsA<KeyValuePair<string, int>>(null!)),
            Probe(() => new W5OverBox<(int, string)>().Is(null!))
        );

    public static string NestedGenericStructDefaultSlots()
    {
        W5OuterGenericStruct<long> o = default;
        return $"{o.G.Is(1L)}|{o.G.Is(1)}|{o.G2.Is(1)}|{o.G2.Is(1L)}";
    }

    public static string NonGenericStructDefaultWithNestedStruct()
    {
        var rt = typeof(W5OuterStruct);
        W5OuterStruct d1 = default;
        var d2 = default(W5OuterStruct);
        var d3 = new W5OuterStruct();
        return $"{d1.F.X}|{d2.F.X}|{d3.F.X}";
    }

    private static string Probe(Func<bool> fn)
    {
        try
        {
            return fn().ToString();
        }
        catch (NotSupportedException)
        {
            return "NotSupportedException";
        }
    }
}
