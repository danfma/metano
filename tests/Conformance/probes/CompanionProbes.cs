namespace Conformance;

/// <summary>A branded value object whose companion is a pure object literal (spec 011 FR-040).</summary>
[Metano.Annotations.InlineWrapper]
public readonly record struct Slug(string Value)
{
    public static Slug FromText(string text) => new(text.Trim().ToLowerInvariant());

    public static Slug From(string text) => FromText(text);

    public static Slug From(int number) => new("n" + number);

    public static Slug Join(Slug a, Slug b) => new(a.Value + "-" + b.Value);
}

/// <summary>Companion calls: factories, an overload dispatcher, a companion-to-companion call, Guid.</summary>
public static class CompanionProbes
{
    public static string Calls()
    {
        var a = Slug.From("  Hello ");
        var b = Slug.From(42);
        var joined = Slug.Join(a, b);

        return a.Value
            + "|"
            + b.Value
            + "|"
            + joined.Value
            + "|"
            + (Slug.FromText("X") == new Slug("x"));
    }

    public static string GuidCompanion()
    {
        var parsed = Guid.Parse("{0F8FAD5B-D9CB-469F-A165-70867728950E}");
        var ok = Guid.TryParse("not a guid", out var none);

        return parsed + "|" + ok + "|" + none + "|" + (Guid.Empty == none);
    }
}
