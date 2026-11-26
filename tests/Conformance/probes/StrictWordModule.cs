using Metano.Annotations;

namespace Conformance;

[ExportedAsModule]
public static class StrictWords
{
    public static string Package(string @let) => @let + "!";

    public static string Static(this string s) => s + "?";
}

public sealed class @implements
{
    public int V = 42;
}
