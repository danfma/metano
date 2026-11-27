using Metano.Annotations;

namespace Conformance;

public class W15Card
{
    public string Seen;

    public W15Card(
        string title,
        string? sub = null,
        Action? onClose = null,
        params string[] content
    )
    {
        Seen = $"{title}/{sub ?? "-"}:{content.Length}:{string.Join(",", content)}";
    }

    public string Count(string label, int pad = 0, params string[] items) =>
        $"{label}/{pad}:{items.Length}:{string.Join(",", items)}";

    public static string Total(string label, int pad = 0, params string[] items) =>
        $"{label}/{pad}:{items.Length}:{string.Join(",", items)}";
}

public static class W15CardExtensions
{
    public static string Tally(this W15Card card, int pad = 0, params string[] items) =>
        $"{pad}:{items.Length}:{string.Join(",", items)}";
}

public class W15FancyCard : W15Card
{
    public W15FancyCard(string[] parts)
        : base("fancy", content: parts) { }
}

/// <summary>A params argument in normal form spreads into the rest parameter (#829).</summary>
public static class ParamsNormalFormProbes
{
    static int _order;

    static string[] Parts()
    {
        _order = _order * 10 + 1;
        return ["p", "q"];
    }

    static string Title()
    {
        _order = _order * 10 + 2;
        return "t";
    }

    public static string ConstructorNamedArray()
    {
        string[] arr = ["a", "b", "c"];

        return new W15Card("t", onClose: () => { }, content: arr).Seen;
    }

    public static string ConstructorNamedCollectionLiteral() =>
        new W15Card("t", content: ["x", "y"]).Seen;

    public static string ConstructorNamedLiteralWithSpread()
    {
        string[] more = ["m", "n"];

        return new W15Card("t", content: ["x", .. more]).Seen;
    }

    public static string ConstructorPositionalArray()
    {
        string[] arr = ["a", "b"];

        return new W15Card("t", null, null, arr).Seen;
    }

    public static string ConstructorExpanded() => new W15Card("t", null, null, "a", "b", "c").Seen;

    public static string ConstructorNamedSingleElement() => new W15Card("t", content: "solo").Seen;

    public static string InstanceNamedArray()
    {
        string[] arr = ["a", "b"];

        return new W15Card("t").Count("x", items: arr);
    }

    public static string InstancePositionalArray()
    {
        string[] arr = ["a", "b", "c"];

        return new W15Card("t").Count("x", 1, arr);
    }

    public static string InstanceExpanded() => new W15Card("t").Count("x", 1, "a", "b");

    public static string StaticNamedArray()
    {
        string[] arr = ["a"];

        return W15Card.Total("x", items: arr);
    }

    public static string ExtensionNamedArray()
    {
        string[] arr = ["a", "b"];

        return new W15Card("t").Tally(items: arr);
    }

    public static string BaseConstructorNamedArray() => new W15FancyCard(["u", "v"]).Seen;

    public static string ReorderedNamedArguments()
    {
        _order = 0;
        var card = new W15Card(content: Parts(), title: Title());

        return $"{card.Seen} order={_order}";
    }

    public static string EmptyNamedArray()
    {
        string[] none = [];

        return new W15Card("t", content: none).Seen;
    }
}

[PlainObject]
public record W15Dto(string Title, string? Sub = null, params string[] Tags);

/// <summary>A [PlainObject] params parameter is one array-valued property (#829).</summary>
public static class PlainObjectParamsProbes
{
    static string Show(W15Dto dto) => $"{dto.Title}:{dto.Tags.Length}:{string.Join(",", dto.Tags)}";

    public static string Normal()
    {
        string[] tags = ["a", "b", "c"];

        return Show(new W15Dto("t", null, tags));
    }

    public static string Named()
    {
        string[] tags = ["a"];

        return Show(new W15Dto("t", Tags: tags));
    }

    public static string Literal() => Show(new W15Dto("t", Tags: ["x", "y"]));

    public static string Expanded() => Show(new W15Dto("t", null, "a", "b"));

    public static string Omitted() => Show(new W15Dto("t"));
}
