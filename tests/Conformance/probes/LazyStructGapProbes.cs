using Metano.Annotations;

namespace Conformance;

public static class LazyStructGapProbes
{
    public static string AutoPropertyWrite()
    {
        LazyLog.Write("start");
        var value = default(LzWriteProperty);
        LazyLog.Write("built");
        value.P = 4;
        return LazyLog.Take("P=" + value.P);
    }

    public static string GetOnlyAutoPropertyRead()
    {
        LazyLog.Write("start");
        var value = default(LzReadOnlyProperty);
        LazyLog.Write("built");
        return LazyLog.Take("P=" + value.P);
    }

    public static string SynthesizedRecordStructEquals()
    {
        LazyLog.Write("start");
        var left = default(LzEmptyRecordStruct);
        var right = default(LzEmptyRecordStruct);
        LazyLog.Write("built");
        return LazyLog.Take("equal=" + left.Equals(right));
    }

    public static string PositionalRecordStructProperty()
    {
        LazyLog.Write("start");
        var value = new LzPositionalRecordStruct(5);
        return LazyLog.Take("P=" + value.P);
    }

    public static string PositionalRecordStructCopyPreservesStorage()
    {
        var value = new LzCopiedRecordStruct(5);
        var copy = value with { P = 6 };
        return "P=" + value.P + ":copy=" + copy.P;
    }

    public static string InstanceFieldDoesNotTrigger()
    {
        LazyLog.Write("start");
        var value = default(LzFieldOnly);
        return LazyLog.Take("field=" + value.Value);
    }

    public static string PlainStructInheritedEqualsDoesNotTrigger()
    {
        LazyLog.Write("start");
        var left = default(LzInheritedEquality);
        var right = default(LzInheritedEquality);
        return LazyLog.Take("equal=" + left.Equals(right));
    }

    public static string FailingAutoPropertyCachesException()
    {
        var value = default(LzFailProperty);
        System.TypeInitializationException? first = null;

        try
        {
            _ = value.P;
        }
        catch (System.TypeInitializationException ex)
        {
            first = ex;
        }

        try
        {
            value.P = 2;
            return "did-not-throw";
        }
        catch (System.TypeInitializationException ex)
        {
            return "TypeInitializationException:same=" + ReferenceEquals(first, ex);
        }
    }
}

public struct LzWriteProperty
{
    public int P { get; set; }

    static LzWriteProperty() => LazyLog.Write("cctor");
}

public struct LzReadOnlyProperty
{
    public int P { get; }

    static LzReadOnlyProperty() => LazyLog.Write("cctor");
}

public record struct LzEmptyRecordStruct
{
    static LzEmptyRecordStruct() => LazyLog.Write("cctor");
}

public readonly record struct LzPositionalRecordStruct(int P)
{
    static LzPositionalRecordStruct() => LazyLog.Write("cctor");
}

public readonly record struct LzCopiedRecordStruct(int P)
{
    static LzCopiedRecordStruct() { }
}

public struct LzFieldOnly
{
    public int Value;

    static LzFieldOnly() => LazyLog.Write("cctor");
}

public struct LzFailProperty
{
    public int P { get; set; }

    static LzFailProperty() => throw new System.InvalidOperationException("property");
}

public struct LzInheritedEquality
{
    public int Value;

    static LzInheritedEquality() => LazyLog.Write("cctor");
}
