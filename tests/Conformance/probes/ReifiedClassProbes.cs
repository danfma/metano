using System;
using System.Collections.Generic;
using Metano.Annotations;

namespace Conformance;

internal class OpaqueProbeA { }

internal class OpaqueProbeB { }

[Transpile]
internal class RtClassBox<T>
{
    private readonly Type _initType = typeof(T);
    public Type CtorType { get; }

    public RtClassBox()
    {
        CtorType = typeof(T);
    }

    public bool Matches(object o) => o is T;

    public bool InitMatchesCtor() => _initType == CtorType;

    public static bool StaticIs(object o) => o is T;

    public static string StaticTypeName() => typeof(T).Name;
}

[Transpile]
internal class RtClassIntBox : RtClassBox<int> { }

[Transpile]
internal class RtClassPair<U, V> : RtClassBox<List<U>>
{
    public bool MatchesSecond(object o) => o is V;
}

[Transpile]
internal record RtClassBoxRec<T>(T Value)
{
    public bool Matches(object o) => o is T;
}

[Transpile]
internal struct RtClassZeroPoint
{
    public int X { get; set; }
}

public static class ReifiedClassProbes
{
    private static T Make<T>()
        where T : new() => new T();

    private static T? Zero<T>() => default;

    private static T? ZeroOf<T>(object o) => o is T ? (T)o : default;

    public static string GenericClassSlotAndIdentity()
    {
        var intBox = new RtClassBox<int>();
        var strBox = new RtClassBox<string>();

        return intBox.Matches(42)
            + "|"
            + intBox.Matches("hi")
            + "|"
            + strBox.Matches("hi")
            + "|"
            + intBox.InitMatchesCtor()
            + "|"
            + (intBox.GetType() == typeof(RtClassBox<int>))
            + "|"
            + (intBox.GetType() == typeof(RtClassBox<string>));
    }

    public static string DerivedClosedAndOpenBase()
    {
        var intBox = new RtClassIntBox();
        var pair = new RtClassPair<string, int>();
        object strList = new List<string> { "a" };
        object intList = new List<int> { 1 };

        return intBox.Matches(42)
            + "|"
            + intBox.Matches("no")
            + "|"
            + pair.Matches(strList)
            + "|"
            + pair.Matches(intList)
            + "|"
            + pair.MatchesSecond(42);
    }

    public static string RecordWithPreservesSlot()
    {
        var rec = new RtClassBoxRec<int>(1);
        var copy = rec with { Value = 2 };

        return copy.Value + "|" + copy.Matches(99) + "|" + copy.Matches("no");
    }

    public static string StaticMembersOnGenericClass()
    {
        return RtClassBox<int>.StaticIs(42)
            + "|"
            + RtClassBox<int>.StaticIs("no")
            + "|"
            + RtClassBox<string>.StaticTypeName();
    }

    public static string NewConstraintAndDefault()
    {
        var c = Make<RtCircle>();
        var p = Make<RtClassZeroPoint>();
        var dInt = ZeroOf<int>(new object());
        var dBool = ZeroOf<bool>(new object());
        var dStr = ZeroOf<string>(new object());
        var dPoint = ZeroOf<RtClassZeroPoint>(new object());

        return (c is RtCircle)
            + "|"
            + p.X
            + "|"
            + dInt
            + "|"
            + dBool
            + "|"
            + (dStr == null)
            + "|"
            + dPoint.X;
    }

    public static string OpaqueSlotAndDictionaryViews()
    {
        object list = new List<OpaqueProbeA>();
        var d = new Dictionary<string, int> { ["a"] = 1 };
        var keys = d.Keys;

        return (list is List<OpaqueProbeA>)
            + "|"
            + (list is List<OpaqueProbeB>)
            + "|"
            + (keys is ICollection<string>)
            + "|"
            + (keys is ICollection<int>);
    }
}
