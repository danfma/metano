using Metano.Annotations;

namespace Conformance;

[PlainObject]
public sealed record RtDto(string Name);

[PlainObject]
public sealed record RtOtherDto(string Name, int Rank);

[PlainObject]
public sealed record RtAreaDto(double Area, string Unit) : IRtArea;

public class RtBox<T>
{
    public T? Value { get; set; }
}

[Flags]
public enum RtPerm
{
    None = 0,
    Read = 1,
    Write = 2,
}

public enum RtTiny : byte
{
    One = 1,
}

public enum RtBig : uint
{
    A = 1,
    B = 3000000000,
}

[StringEnum]
public enum RtSe
{
    X,
    Y,
}

/// <summary>
/// Spec 011 stage 011b B2: plain objects as shapes (FR-020), plain-object
/// implementers (FR-019), closed generic descriptors (FR-023) and the enum
/// helpers over a descriptor (FR-024).
/// </summary>
public static class RuntimeTypeShapeProbes
{
    public static string PlainObjectIsStructural()
    {
        object dto = new RtDto("a");
        object other = new RtOtherDto("b", 1);
        object text = "a";

        return (dto is RtDto) + "|" + (other is RtDto) + "|" + (text is RtDto);
    }

    public static string InterfaceWithPlainImplementer()
    {
        object dto = new RtAreaDto(2.0, "m2");
        object lookalike = new RtLookalike();
        object circle = new RtCircle();

        return (dto is IRtArea) + "|" + (lookalike is IRtArea) + "|" + (circle is IRtArea);
    }

    public static string PlainObjectCast()
    {
        object dto = new RtDto("a");

        return ((RtDto)dto).Name;
    }

    public static string PlainObjectCastFail()
    {
        try
        {
            object text = "x";

            return ((RtDto)text).Name;
        }
        catch (InvalidCastException e)
        {
            return e.GetType().Name;
        }
    }

    public static string ClosedGenericName() =>
        typeof(RtBox<int>).Name + "|" + typeof(RtBox<int>).Namespace;

    public static string EnumIsDefined() =>
        Enum.IsDefined(typeof(RtColor), 1)
        + "|"
        + Enum.IsDefined(typeof(RtColor), 7)
        + "|"
        + Enum.IsDefined(RtColor.Green)
        + "|"
        + Enum.IsDefined((RtPerm)3);

    public static string EnumParse() =>
        (int)Enum.Parse<RtColor>("Green")
        + "|"
        + (int)Enum.Parse<RtPerm>("Read, Write")
        + "|"
        + (int)Enum.Parse<RtColor>("1")
        + "|"
        + (int)Enum.Parse<RtColor>("green", true)
        + "|"
        + (int)(RtColor)Enum.Parse(typeof(RtColor), "Red");

    public static string EnumParseUnknown()
    {
        try
        {
            return ((int)Enum.Parse<RtColor>("Purple")).ToString();
        }
        catch (ArgumentException e)
        {
            return e.GetType().Name;
        }
    }

    public static string EnumParseCaseSensitive()
    {
        try
        {
            return ((int)Enum.Parse<RtColor>("green")).ToString();
        }
        catch (ArgumentException e)
        {
            return e.GetType().Name;
        }
    }

    public static string EnumToObjectUndeclared()
    {
        var raw = Enum.ToObject(typeof(RtColor), 9);

        return (int)(RtColor)raw + "|" + Enum.IsDefined(typeof(RtColor), raw);
    }

    public static string PlainObjectGetType()
    {
        var dto = new RtDto("a");

        return dto.GetType().Name + "|" + new RtDto("b").GetType().Name;
    }

    private static int s_ticks;

    private static string Tick()
    {
        s_ticks++;

        return "t";
    }

    public static string PlainObjectGetTypeKeepsEffects()
    {
        s_ticks = 0;

        var name = new RtDto(Tick()).GetType().Name;

        return name + "|" + s_ticks;
    }

    public static string PlainObjectGetTypeOfNull()
    {
        try
        {
            RtDto? dto = null;

            return dto!.GetType().Name;
        }
        catch (NullReferenceException e)
        {
            return e.GetType().Name;
        }
    }

    private static string Failure(Exception e) => e.GetType().Name + ": " + e.Message;

    public static string EnumParseNumberInList()
    {
        try
        {
            return ((int)Enum.Parse<RtPerm>("Read, 8")).ToString();
        }
        catch (ArgumentException e)
        {
            return Failure(e);
        }
    }

    public static string EnumParseNumberList()
    {
        try
        {
            return ((int)Enum.Parse<RtPerm>("1, 2")).ToString();
        }
        catch (ArgumentException e)
        {
            return Failure(e);
        }
    }

    public static string EnumParseSignedNumber() => ((int)Enum.Parse<RtPerm>(" +3 ")).ToString();

    public static string EnumParseUnsigned() =>
        (uint)Enum.Parse<RtBig>("3000000000") + "|" + (uint)Enum.Parse<RtBig>("A, B");

    public static string EnumParseNotFoundNamesTheInput()
    {
        try
        {
            return ((int)Enum.Parse<RtPerm>("Read, Nope")).ToString();
        }
        catch (ArgumentException e)
        {
            return Failure(e);
        }
    }

    public static string EnumParseWhitespace()
    {
        try
        {
            return ((int)Enum.Parse<RtPerm>("   ")).ToString();
        }
        catch (ArgumentException e)
        {
            return Failure(e);
        }
    }

    public static string EnumParseOverflow()
    {
        try
        {
            return ((int)Enum.Parse<RtTiny>("300")).ToString();
        }
        catch (OverflowException e)
        {
            return Failure(e);
        }
    }

    public static string EnumParseInt32Overflow()
    {
        try
        {
            return ((int)Enum.Parse<RtPerm>("99999999999")).ToString();
        }
        catch (OverflowException e)
        {
            return Failure(e);
        }
    }

    public static string EnumParseStringEnumNumber() => Enum.Parse<RtSe>("1").ToString();

    public static string EnumToObjectStringEnum() => Enum.ToObject(typeof(RtSe), 1).ToString()!;

    public static string EnumToObjectLong() =>
        ((int)(RtColor)Enum.ToObject(typeof(RtColor), 1L)).ToString();

    public static string EnumToObjectWraps() =>
        ((int)(RtTiny)Enum.ToObject(typeof(RtTiny), 300)).ToString();

    public static string EnumToObjectNonIntegral()
    {
        try
        {
            return Enum.ToObject(typeof(RtColor), 1.5).ToString()!;
        }
        catch (ArgumentException e)
        {
            return Failure(e);
        }
    }
}
