namespace Conformance;

public interface IRtArea
{
    double Area { get; }
}

public abstract class RtShape { }

public class RtCircle : RtShape, IRtArea
{
    public double Area => 3.0;
}

public class RtSquare : RtShape { }

public class RtLookalike
{
    public double Area => 1.0;
}

public struct RtMeter
{
    public double Value;
}

public enum RtColor
{
    Red,
    Green,
}

public static class RtFactory
{
    public static RtShape Make(string kind) => kind == "c" ? new RtCircle() : new RtSquare();
}

public static class RuntimeTypeProbes
{
    public static string GetTypeOfDerived()
    {
        var shape = RtFactory.Make("c");

        return shape.GetType().Name
            + "|"
            + shape.GetType().FullName
            + "|"
            + shape.GetType().Namespace;
    }

    public static string GetTypeEquality()
    {
        var shape = RtFactory.Make("c");

        return (shape.GetType() == typeof(RtCircle))
            + "|"
            + (shape.GetType() == typeof(RtShape))
            + "|"
            + (shape.GetType() != typeof(RtSquare));
    }

    public static string InterfaceIsNominal()
    {
        object circle = new RtCircle();
        object square = new RtSquare();
        object lookalike = new RtLookalike();

        return (circle is IRtArea) + "|" + (square is IRtArea) + "|" + (lookalike is IRtArea);
    }

    public static string InterfaceAs()
    {
        object circle = new RtCircle();
        object square = new RtSquare();

        return (circle as IRtArea)?.Area + "|" + ((square as IRtArea) == null);
    }

    public static string InterfaceCastFail()
    {
        try
        {
            object square = new RtSquare();

            return ((IRtArea)square).Area.ToString();
        }
        catch (InvalidCastException e)
        {
            return e.Message;
        }
    }

    public static string TypeOfPrimitives() =>
        typeof(int).Name + "|" + typeof(string).FullName + "|" + typeof(int).Namespace;

    public static string RuntimeTypeOfValues()
    {
        object integer = 1;
        object fraction = 1.5;
        object text = "x";
        object flag = true;

        return integer.GetType().Name
            + "|"
            + fraction.GetType().Name
            + "|"
            + text.GetType().Name
            + "|"
            + flag.GetType().Name;
    }

    public static string FoldedValueReceivers() =>
        new RtMeter().GetType().Name
        + "|"
        + RtColor.Green.GetType().FullName
        + "|"
        + 5.GetType().Name;

    public static string EnumMembership()
    {
        object member = RtColor.Green;
        object stranger = 7;

        return (member is RtColor) + "|" + (stranger is RtColor);
    }

    public static string EnumCastFail()
    {
        try
        {
            object text = "x";

            return ((RtColor)text).ToString();
        }
        catch (InvalidCastException e)
        {
            return e.Message;
        }
    }

    public static string TypeSurface()
    {
        var area = typeof(IRtArea);
        var circle = typeof(RtCircle);

        return area.IsInterface
            + "|"
            + area.IsClass
            + "|"
            + circle.IsClass
            + "|"
            + area.IsInstanceOfType(new RtCircle())
            + "|"
            + area.IsAssignableFrom(circle)
            + "|"
            + circle.ToString();
    }
}
