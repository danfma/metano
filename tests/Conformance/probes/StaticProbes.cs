namespace Conformance;

public class Registry
{
    public static readonly List<string> Log = new();
    public static int A = Init("A");

    static Registry()
    {
        Log.Add("cctor:" + A + ":" + B);
        C = 9;
    }

    public static int B = Init("B");
    public static int C;

    static int Init(string n)
    {
        Log.Add(n);
        return n.Length;
    }
}

public class Lazy1
{
    public static string Value;

    static Lazy1()
    {
        Value = "L1(" + Lazy2.Value + ")";
    }
}

public class Lazy2
{
    public static string Value = "L2";

    static Lazy2()
    {
        Value += "!";
    }

    public static string Other() => Lazy1.Value;
}

public static class StaticHolder
{
    public static readonly Dictionary<string, int> Map;

    static StaticHolder()
    {
        Map = new Dictionary<string, int>();
        Map["a"] = 1;
        Map["b"] = 2;
    }
}

public static class StaticProbes
{
    public static string Order() => string.Join(",", Registry.Log) + "|" + Registry.C;

    public static string Cross() => Lazy1.Value + "|" + Lazy2.Value;

    public static string StaticClass() => StaticHolder.Map.Count + "|" + StaticHolder.Map["b"];
}
