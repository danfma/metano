namespace Conformance;

public abstract record SrShape;

public sealed record SrCircle(int Radius) : SrShape;

public sealed record SrSquare(int Side) : SrShape;

/// <summary>
/// A switch expression returned directly (#735): the scrutinee runs once, arms are tried in
/// order, a <c>when</c> guard that fails falls to the next arm, designators are arm-local, and an
/// unmatched value throws.
/// </summary>
public static class SwitchReturnProbes
{
    static int _calls;

    static SrShape Next(SrShape shape)
    {
        _calls++;

        return shape;
    }

    static string Describe(SrShape shape) =>
        Next(shape) switch
        {
            SrCircle { Radius: > 10 } => "big-circle",
            SrCircle c when c.Radius > 5 => "mid-circle:" + c.Radius,
            SrCircle c => "circle:" + c.Radius,
            SrSquare s when s.Side == 0 => "empty-square",
            SrSquare s => "square:" + s.Side,
            _ => "other",
        };

    static string Partial(int value) =>
        value switch
        {
            1 => "one",
            2 when value > 5 => "never",
            2 => "two",
        };

    public static string ArmOrderAndGuards()
    {
        _calls = 0;

        var text =
            Describe(new SrCircle(20))
            + ","
            + Describe(new SrCircle(7))
            + ","
            + Describe(new SrCircle(1))
            + ","
            + Describe(new SrSquare(0))
            + ","
            + Describe(new SrSquare(3));

        return text + ":" + _calls;
    }

    public static string ScrutineeRunsOnce()
    {
        _calls = 0;
        Describe(new SrSquare(2));

        return "" + _calls;
    }

    public static string UnmatchedValueThrows()
    {
        try
        {
            return Partial(1) + "," + Partial(2) + "," + Partial(3);
        }
        catch (InvalidOperationException exception)
        {
            return exception.GetType().Name;
        }
    }
}
