namespace Conformance;

public struct DefaultPoint
{
    public int X;
    public int Y;

    public DefaultPoint(int x, int y)
    {
        X = x;
        Y = y;
    }
}

public static class StructDefaultProbes
{
    static DefaultPoint Returned() => default;

    static string Show(DefaultPoint p) => p.X + ":" + p.Y;

    public static string DefaultInEveryPosition()
    {
        var flag = Returned().X == 0;
        DefaultPoint local = default;
        DefaultPoint assigned = new DefaultPoint(1, 2);
        assigned = default;
        var picked = flag ? default(DefaultPoint) : new DefaultPoint(3, 4);

        return Show(Returned())
            + "|"
            + Show(default)
            + "|"
            + Show(local)
            + "|"
            + Show(assigned)
            + "|"
            + Show(picked);
    }

    public static string DefaultEqualsNew() =>
        (Returned().X == new DefaultPoint().X).ToString() + "," + Returned().Y;
}
