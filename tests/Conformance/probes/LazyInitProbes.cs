namespace Conformance;

// Lazy static constructors (spec 001 FR-066, #681): a type that declares a static
// constructor runs it, after its static field initializers, right before its first
// use. Every row touches its own types, so the .NET run (one process for every probe)
// and the per-module Bun / Node runs see the same first use.

public static class LazyLog
{
    public static readonly List<string> Lines = new();

    public static void Write(string line) => Lines.Add(line);

    public static string Take(string tail)
    {
        Lines.Add(tail);
        var text = string.Join(",", Lines);
        Lines.Clear();
        return text;
    }
}

public class LzInstanceFirst
{
    static int F = Init();

    static LzInstanceFirst()
    {
        LazyLog.Write("cctor");
    }

    public LzInstanceFirst()
    {
        LazyLog.Write("ctor");
    }

    static int Init()
    {
        LazyLog.Write("F");
        return 1;
    }
}

public class LzMethodFirst
{
    static int F = Init();

    static LzMethodFirst()
    {
        LazyLog.Write("cctor");
    }

    public static void M() => LazyLog.Write("M");

    static int Init()
    {
        LazyLog.Write("F");
        return 1;
    }
}

public class LzReadFirst
{
    public static int F = Init();

    static LzReadFirst()
    {
        LazyLog.Write("cctor");
        F = 5;
    }

    static int Init()
    {
        LazyLog.Write("F");
        return 1;
    }
}

public class LzWriteFirst
{
    public static int F = Init();

    static LzWriteFirst()
    {
        LazyLog.Write("cctor");
    }

    static int Init()
    {
        LazyLog.Write("F");
        return 1;
    }
}

public class LzConst
{
    public const int C = 7;

    static LzConst()
    {
        LazyLog.Write("cctor");
    }
}

public static class LazyOrderProbes
{
    public static string InstanceFirst()
    {
        LazyLog.Write("start");
        new LzInstanceFirst();
        return LazyLog.Take("end");
    }

    public static string StaticMethodFirst()
    {
        LazyLog.Write("start");
        LzMethodFirst.M();
        return LazyLog.Take("end");
    }

    public static string FieldReadFirst()
    {
        LazyLog.Write("start");
        var value = LzReadFirst.F;
        return LazyLog.Take("read=" + value);
    }

    public static string FieldWriteFirst()
    {
        LazyLog.Write("start");
        LzWriteFirst.F = 9;
        return LazyLog.Take("after=" + LzWriteFirst.F);
    }

    public static string ConstRead()
    {
        LazyLog.Write("start");
        var value = LzConst.C;
        return LazyLog.Take("c=" + value);
    }
}

public class LzBase
{
    public static int Shared = 1;

    static LzBase()
    {
        LazyLog.Write("base-cctor");
    }

    public LzBase()
    {
        LazyLog.Write("base-ctor");
    }

    public static void M() => LazyLog.Write("M");
}

public class LzDerived : LzBase
{
    static int F = Init();

    static LzDerived()
    {
        LazyLog.Write("derived-cctor");
    }

    public LzDerived()
    {
        LazyLog.Write("derived-ctor");
    }

    static int Init()
    {
        LazyLog.Write("derived-F");
        return 1;
    }
}

public class LzMethodBase
{
    static LzMethodBase()
    {
        LazyLog.Write("base-cctor");
    }

    public static void M() => LazyLog.Write("M");
}

public class LzMethodDerived : LzMethodBase
{
    static LzMethodDerived()
    {
        LazyLog.Write("derived-cctor");
    }
}

public class LzFieldBase
{
    public static int F = 1;

    static LzFieldBase()
    {
        LazyLog.Write("base-cctor");
    }
}

public class LzFieldDerived : LzFieldBase
{
    static LzFieldDerived()
    {
        LazyLog.Write("derived-cctor");
    }
}

public class LzPlainBase
{
    static LzPlainBase()
    {
        LazyLog.Write("base-cctor");
    }

    public LzPlainBase()
    {
        LazyLog.Write("base-ctor");
    }
}

public class LzPlainDerived : LzPlainBase
{
    public LzPlainDerived()
    {
        LazyLog.Write("derived-ctor");
    }
}

public static class LazyDerivedProbes
{
    public static string NewDerived()
    {
        LazyLog.Write("start");
        new LzDerived();
        return LazyLog.Take("end");
    }

    public static string InheritedStaticMethod()
    {
        LazyLog.Write("start");
        LzMethodDerived.M();
        return LazyLog.Take("end");
    }

    public static string WriteThroughDerivedName()
    {
        LazyLog.Write("start");
        LzFieldDerived.F = 5;
        return LazyLog.Take("sum=" + (LzFieldBase.F + LzFieldDerived.F));
    }

    public static string DerivedWithoutStaticConstructor()
    {
        LazyLog.Write("start");
        new LzPlainDerived();
        return LazyLog.Take("end");
    }
}

public class LzThrows
{
    public static int F = 1;

    static LzThrows()
    {
        LazyLog.Write("cctor");
        throw new InvalidOperationException("boom");
    }

    public static void M() { }
}

public static class LazyFailureProbes
{
    public static string RethrowsTheSameException()
    {
        LazyLog.Write("start");
        TypeInitializationException? first = null;
        TypeInitializationException? second = null;

        try
        {
            LzThrows.M();
        }
        catch (TypeInitializationException e)
        {
            first = e;
        }

        try
        {
            LazyLog.Write("F=" + LzThrows.F);
        }
        catch (TypeInitializationException e)
        {
            second = e;
        }

        return LazyLog.Take(
            (first?.InnerException is InvalidOperationException)
                + ":"
                + first?.InnerException?.Message
                + ":"
                + ReferenceEquals(first, second)
        );
    }
}

public class LzCycleA
{
    public static int X = LzCycleB.Y + 1;

    static LzCycleA()
    {
        LazyLog.Write("A:" + X);
    }
}

public class LzCycleB
{
    public static int Y = LzCycleA.X + 10;

    static LzCycleB()
    {
        LazyLog.Write("B:" + Y);
    }
}

public class LzTextA
{
    public static string X = "a<" + (LzTextB.Y ?? "null") + ">";

    static LzTextA() { }
}

public class LzTextB
{
    public static string Y = "b<" + (LzTextA.X ?? "null") + ">";

    static LzTextB() { }
}

public class LzReentry
{
    public static LzReentry? Instance;

    static LzReentry()
    {
        LazyLog.Write("cctor-start");
        Instance = new LzReentry();
        LazyLog.Write("cctor-end");
    }

    public LzReentry()
    {
        LazyLog.Write("ctor");
    }
}

public static class LazyCycleProbes
{
    public static string IntCycle()
    {
        LazyLog.Write("start");
        return LazyLog.Take("X=" + LzCycleA.X + ":Y=" + LzCycleB.Y);
    }

    public static string ReentryReadsNull() => LzTextA.X + "|" + LzTextB.Y;

    public static string ReentryOwnInstance()
    {
        LazyLog.Write("start");
        var instance = LzReentry.Instance;
        return LazyLog.Take("null=" + (instance is null));
    }
}

public class LzCounter
{
    public static int Next = 1;

    public static int Take() => Next++;

    public static void Reset() => Next = 100;
}

public class LzFieldOrder
{
    public int A = LzCounter.Take();

    static LzFieldOrder()
    {
        LzCounter.Reset();
    }
}

public struct LzStruct
{
    public int V;

    static LzStruct()
    {
        LazyLog.Write("cctor");
    }

    public LzStruct(int v)
    {
        V = v;
        LazyLog.Write("ctor");
    }

    public int Get() => V;
}

public static class LazyFieldProbes
{
    public static string InstanceFieldInitializerAfterCctor() => "a=" + new LzFieldOrder().A;

    public static string StructDefaultDoesNotTrigger()
    {
        LazyLog.Write("start");
        var implicitDefault = new LzStruct();
        LazyLog.Write("default");
        var value = implicitDefault.Get();
        LazyLog.Write("get=" + value);
        var declared = new LzStruct(3);
        return LazyLog.Take("v=" + declared.V);
    }
}

public struct LzParamsNone
{
    public int W = 7;
    public int Count;

    static LzParamsNone()
    {
        LazyLog.Write("cctor");
    }

    public LzParamsNone(params int[] values)
    {
        Count = values.Length;
    }
}

public struct LzParamsEmpty
{
    public int W = 7;
    public int Count;

    static LzParamsEmpty()
    {
        LazyLog.Write("cctor");
    }

    public LzParamsEmpty(params int[] values)
    {
        Count = values.Length;
    }
}

public struct LzParamsTwo
{
    public int W = 7;
    public int Count;

    static LzParamsTwo()
    {
        LazyLog.Write("cctor");
    }

    public LzParamsTwo(params int[] values)
    {
        Count = values.Length;
    }
}

public struct LzDispatched
{
    public int W = 7;
    public int P { get; set; } = 8;
    public int V;

    static LzDispatched()
    {
        LazyLog.Write("cctor");
    }

    public LzDispatched(int v)
    {
        V = v;
    }

    public LzDispatched(string text)
    {
        V = text.Length;
    }

    public int Get() => V;
}

public struct LzDispatchedProperty
{
    public int W = 7;
    public int P { get; set; } = 8;
    public int V;

    static LzDispatchedProperty()
    {
        LazyLog.Write("cctor");
    }

    public LzDispatchedProperty(int v)
    {
        V = v;
    }

    public LzDispatchedProperty(string text)
    {
        V = text.Length;
    }

    public int Get() => V;
}

public struct LzParamsDispatched
{
    public int W = 7;
    public int Count;

    static LzParamsDispatched()
    {
        LazyLog.Write("cctor");
    }

    public LzParamsDispatched(params int[] values)
    {
        Count = values.Length;
    }

    public LzParamsDispatched(string text)
    {
        Count = -text.Length;
    }
}

public struct LzEagerParams
{
    public int W = 7;
    public int Count;

    public LzEagerParams(params int[] values)
    {
        Count = values.Length;
    }
}

public struct LzBareStruct
{
    public int V;

    static LzBareStruct()
    {
        LazyLog.Write("cctor");
    }

    public int Get() => V;
}

public static class LazyStructProbes
{
    public static string ParamsConstructorWithoutArguments()
    {
        LazyLog.Write("start");
        var empty = new LzParamsNone();
        return LazyLog.Take("W=" + empty.W + ":n=" + empty.Count);
    }

    public static string ParamsConstructorWithoutArgumentsNoStaticConstructor()
    {
        var empty = new LzEagerParams();
        return "W=" + empty.W + ":n=" + empty.Count;
    }

    public static string ParamsConstructorWithEmptyArray()
    {
        LazyLog.Write("start");
        var empty = new LzParamsEmpty(new int[0]);
        return LazyLog.Take("W=" + empty.W + ":n=" + empty.Count);
    }

    public static string ParamsConstructorWithArguments()
    {
        LazyLog.Write("start");
        var two = new LzParamsTwo(1, 2);
        return LazyLog.Take("W=" + two.W + ":n=" + two.Count);
    }

    public static string DispatchedParamsConstructorWithEmptyArray()
    {
        LazyLog.Write("start");
        var empty = new LzParamsDispatched(new int[0]);
        return LazyLog.Take("W=" + empty.W + ":n=" + empty.Count);
    }

    public static string ImplicitDefaultOfDispatchedStruct()
    {
        LazyLog.Write("start");
        var zero = new LzDispatched();
        LazyLog.Write("W=" + zero.W);
        var value = zero.Get();
        var one = new LzDispatched(1);
        return LazyLog.Take("get=" + value + ":W=" + one.W + ":P=" + one.P + ":V=" + one.V);
    }

    public static string StructAutoPropertyRead()
    {
        LazyLog.Write("start");
        var zero = new LzDispatchedProperty();
        LazyLog.Write("P=" + zero.P);
        return LazyLog.Take("get=" + zero.Get());
    }

    public static string DefaultOfBareStruct()
    {
        LazyLog.Write("start");
        var zero = default(LzBareStruct);
        var made = new LzBareStruct();
        LazyLog.Write("built");
        return LazyLog.Take("get=" + zero.Get() + made.V);
    }
}
