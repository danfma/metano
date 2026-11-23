namespace Conformance;

/// <summary>Wave 6 group G: patterns, delegates, params and statements.</summary>
public static class W6PatternProbes
{
    const double MissingValue = double.NaN;

    static bool IsArithmeticNaN(double v) => v is 0.0 / 0.0;

    static bool IsMissingValue(double v) => v is MissingValue;

    static int ClassifyNaN(double v)
    {
        switch (v)
        {
            case MissingValue:
                return 1;
            case 0:
                return 2;
            default:
                return 3;
        }
    }

    public static string NaNConstantsByValue() =>
        $"{IsArithmeticNaN(double.NaN)} {IsArithmeticNaN(1)} {IsMissingValue(double.NaN)} {IsMissingValue(0)} {ClassifyNaN(double.NaN)} {ClassifyNaN(0)} {ClassifyNaN(5)}";

    static bool HasNoInner(Exception e) => e is { InnerException: null };

    static bool HasTwoElements(int[] a) => a is { LongLength: 2 };

    static string InnerMessage(Exception e) =>
        e is { InnerException: { Message: var message } } ? message : "none";

    public static string MappedPropertySubpatterns() =>
        $"{HasNoInner(new Exception("a"))} {HasNoInner(new Exception("a", new Exception("b")))} {HasTwoElements([1, 2])} {HasTwoElements([1])} {InnerMessage(new Exception("a", new Exception("b")))} {InnerMessage(new Exception("a"))}";

    public abstract record W6Shape;

    public sealed record W6Circle(int R) : W6Shape;

    public sealed record W6Square(int S) : W6Shape;

    static int MeasureShape(W6Shape s)
    {
        switch (s)
        {
            case W6Circle c:
                const int a = 1;
                const int k = a + 1;
                const int j = k + 1;
                return c.R * j;
            case W6Square q:
                return q.S + j;
            default:
                return 0;
        }
    }

    public static string HoistedSwitchConstants() =>
        $"{MeasureShape(new W6Circle(2))} {MeasureShape(new W6Square(1))}";
}
