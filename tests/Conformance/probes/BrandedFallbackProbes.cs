using Metano.Annotations;

namespace Conformance;

/// <summary>A record a non-primitive branded wrapper holds.</summary>
public sealed record BfInner(int X);

/// <summary>[Branded] over a record lowers as the record it is (#288).</summary>
[Branded]
public readonly record struct BfBoxed(BfInner Value)
{
    public static BfBoxed Make(int x) => new(new BfInner(x));

    public int Twice() => Value.X * 2;
}

/// <summary>A primitive-backed brand with a static method a delegate names.</summary>
[Branded]
public readonly record struct BfTag(long Value)
{
    public static string Show(BfTag tag) => "t" + tag.Value;
}

/// <summary>A C# 14 extension block whose renamed property keeps its accessors.</summary>
public static class BfTextExtensions
{
    extension(string s)
    {
        [Name("bfLength")]
        public int BfSize => s.Length;
    }
}

/// <summary>
/// Branded fallback, explicit delegate creation and a renamed extension
/// property (#288, #634, extension-block [Name]).
/// </summary>
public static class BrandedFallbackProbes
{
    public static string RecordFallback()
    {
        var made = BfBoxed.Make(3);
        var built = new BfBoxed(new BfInner(3));
        var other = built with { Value = new BfInner(4) };

        return (made == built)
            + "|"
            + (made == other)
            + "|"
            + built.Twice()
            + "|"
            + (made.GetHashCode() == built.GetHashCode());
    }

    public static string DelegateCreation()
    {
        var show = new Func<BfTag, string>(BfTag.Show);
        var inc = new Func<int, int>(x => x + 1);
        var again = new Func<int, int>(inc);

        return show(new BfTag(7)) + "|" + inc(1) + "|" + again(2);
    }

    public static string RenamedExtensionProperty() => "abc".BfSize + "|" + "".BfSize;
}
