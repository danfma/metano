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
}
