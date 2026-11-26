using Metano.Annotations;

namespace Lib;

/// <summary>
/// A class named like a strict-mode reserved word (declared as <c>let_</c>) whose
/// constructor selectors and static dispatcher a referencing package reaches (#791).
/// </summary>
public class let
{
    public string K = "p";

    public let(Func<int> f)
    {
        K = "fi" + f();
    }

    [Name("Lbl")]
    public let(Func<string> f)
    {
        K = "fs" + f();
    }

    public let(int n)
    {
        K = "n" + n;
    }

    public static string Pick(int x) => "pi" + x;

    public static string Pick(string x) => "ps" + x;
}

/// <summary>A renamed static class beside a generic of the same C# name (#776).</summary>
[Name("LSignals")]
public static class LSignal
{
    public static string Describe(int x) => "int" + x;

    public static string Describe(string x) => "str" + x;
}

public class LSignal<T>(T value)
{
    public T Value { get; } = value;
}
