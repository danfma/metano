namespace Conformance;

public static class DerivedProbes
{
    static string B(bool b) => b ? "T" : "F";

    public static string DerivedToString()
    {
        var m = new Money3(5);
        Money2 b = m;
        object o = m;
        return m.ToString()
            + "|"
            + m.ToString("X")
            + "|"
            + b.ToString()
            + "|"
            + b.ToString("Y")
            + "|"
            + o.ToString()
            + "|"
            + $"{m}"
            + "|"
            + ("" + b)
            + "|"
            + B(m.Equals(new Money3(5)))
            + B(m.Equals((object)new Money2(5)))
            + B(b.Equals(m));
    }
}
