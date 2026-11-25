using System;

namespace Conformance;

// Wave 9 group S: string.Equals / string.Compare with a StringComparison (#769).

public static class StringComparisonProbes
{
    private static readonly string[][] FoldPairs =
    [
        ["ß", "SS"],
        ["ß", "ẞ"],
        ["İ", "i"],
        ["ı", "I"],
        ["ﬀ", "FF"],
        ["ς", "σ"],
        ["σ", "Σ"],
        ["ſ", "S"],
        ["K", "k"],
        ["µ", "Μ"],
        ["ǅ", "ǆ"],
        ["ᾀ", "ᾈ"],
        ["𐐨", "𐐀"],
        ["Hello", "hELLO"],
    ];

    private static string Flag(bool value) => value ? "T" : "F";

    public static string OrdinalIgnoreCaseEquality()
    {
        var text = "";

        foreach (var pair in FoldPairs)
            text += Flag(string.Equals(pair[0], pair[1], StringComparison.OrdinalIgnoreCase));

        return text;
    }

    public static string InstanceEqualityPerComparison()
    {
        var text = "";
        var left = "Straße";

        foreach (var right in new[] { "Straße", "STRAßE", "Strasse", "straße" })
        {
            text += Flag(left.Equals(right, StringComparison.Ordinal));
            text += Flag(left.Equals(right, StringComparison.OrdinalIgnoreCase));
            text += Flag(left.Equals(right, StringComparison.InvariantCulture));
            text += Flag(left.Equals(right, StringComparison.InvariantCultureIgnoreCase));
            text += Flag(left.Equals(right, StringComparison.CurrentCulture));
            text += Flag(left.Equals(right, StringComparison.CurrentCultureIgnoreCase));
            text += "|";
        }

        return text;
    }

    public static string NullEquality()
    {
        string? none = null;

        return Flag(string.Equals(none, none, StringComparison.OrdinalIgnoreCase))
            + Flag(string.Equals("a", none, StringComparison.Ordinal))
            + Flag(string.Equals(none, "a", StringComparison.InvariantCulture))
            + Flag("a".Equals(none, StringComparison.OrdinalIgnoreCase))
            + Flag(string.Equals(none, none))
            + Flag("a".Equals((string?)"a"));
    }

    private static readonly string[][] ComparePairs =
    [
        ["a", "B"],
        ["_", "a"],
        ["abc", "ABD"],
        ["ab", "a"],
        ["\uFFFF", "\uD801\uDC28"],
        ["\u00E9a", "\u00C9b"],
        ["r\u00E9sum\u00E9", "resume"],
        ["coop", "co-op"],
        ["a", "A"],
    ];

    public static string OrdinalCompareSigns()
    {
        var text = "";

        foreach (var pair in ComparePairs)
        {
            text += Math.Sign(string.Compare(pair[0], pair[1], StringComparison.Ordinal));
            text += ",";
            text += Math.Sign(string.Compare(pair[0], pair[1], StringComparison.OrdinalIgnoreCase));
            text += ",";
            text += Math.Sign(string.CompareOrdinal(pair[0], pair[1]));
            text += "|";
        }

        return text;
    }

    // The recorded .NET run uses InvariantGlobalization, where the culture comparisons
    // are ordinal; the runtime compares with ICU collation, as .NET does with ICU.
    public static string CultureCompareSigns()
    {
        var text = "";

        foreach (var pair in ComparePairs)
        {
            text += Math.Sign(string.Compare(pair[0], pair[1], StringComparison.InvariantCulture));
            text += ",";
            text += Math.Sign(
                string.Compare(pair[0], pair[1], StringComparison.InvariantCultureIgnoreCase)
            );
            text += ",";
            text += Math.Sign(string.Compare(pair[0], pair[1]));
            text += ",";
            text += Math.Sign(string.Compare(pair[0], pair[1], true));
            text += ",";
            text += Math.Sign(pair[0].CompareTo(pair[1]));
            text += "|";
        }

        return text;
    }

    private static string IgnoreCaseEqualities(string[][] pairs)
    {
        var text = "";

        foreach (var pair in pairs)
        {
            text += Flag(
                string.Equals(pair[0], pair[1], StringComparison.InvariantCultureIgnoreCase)
            );
            text += Flag(
                string.Equals(pair[0], pair[1], StringComparison.CurrentCultureIgnoreCase)
            );
            text += Math.Sign(string.Compare(pair[0], pair[1], true));
            text += "|";
        }

        return text;
    }

    // A width form folds its case but keeps its width; U+3000 equals a space ignoring case
    // (.NET on ICU: TT0|TT0|). The recording runs with InvariantGlobalization (ordinal).
    public static string CultureIgnoreCaseWidthPairs() =>
        IgnoreCaseEqualities([
            ["\uFF21", "\uFF41"],
            ["\u3000", " "],
        ]);

    // Known gaps against .NET on ICU, which answers TT0 for the first three pairs and
    // FF-1 for the small kana (the runtime: FF1 throughout). The recording runs with
    // InvariantGlobalization, where .NET also answers FF1, so the row records the
    // runtime's answer rather than the ICU divergence (pinned in string-comparison.test.ts).
    public static string CultureIgnoreCaseExoticPairs() =>
        IgnoreCaseEqualities([
            ["\u0130", "i\u0307"],
            ["\uFF76\uFF9E", "\uFF76\u3099"],
            ["\uFFE8", "\u2502"],
            ["\uFF71", "\uFF67"],
            ["\u30A2", "\u30A1"],
        ]);

    public static string CompareNulls()
    {
        string? none = null;

        return string.Compare(none, none, StringComparison.Ordinal)
            + ","
            + string.Compare(none, "a", StringComparison.OrdinalIgnoreCase)
            + ","
            + string.Compare("a", none, StringComparison.InvariantCulture)
            + ","
            + "a".CompareTo(none);
    }

    public static string UnknownComparisonThrows()
    {
        try
        {
            return Flag(string.Equals("a", "a", (StringComparison)9));
        }
        catch (ArgumentException e)
        {
            return e.Message;
        }
    }

    public static string IgnoreCaseSearchFold()
    {
        return Flag("ı".Contains("I", StringComparison.OrdinalIgnoreCase))
            + Flag("ſ".StartsWith("s", StringComparison.OrdinalIgnoreCase))
            + Flag("xᾀ".EndsWith("ᾈ", StringComparison.OrdinalIgnoreCase))
            + "x𐐨".IndexOf("𐐀", StringComparison.OrdinalIgnoreCase);
    }

    private static int ThroughComparable<T>(T left, T right)
        where T : IComparable<T> => Math.Sign(left.CompareTo(right));

    public static string InstanceFormsAndComparables()
    {
        string? none = null;
        var text = "";

        try
        {
            text += Flag(none!.Equals("a", StringComparison.Ordinal));
        }
        catch (NullReferenceException)
        {
            text += "NRE";
        }

        try
        {
            text += "a".CompareTo((object)5);
        }
        catch (ArgumentException e)
        {
            text += "|" + e.Message;
        }

        text += "|" + "a".CompareTo((object?)null);
        text += "|" + Math.Sign(((IComparable<string>)"a").CompareTo("b"));
        text += "|" + ThroughComparable("b", "a") + ThroughComparable(1, 2);

        text +=
            "|"
            + Flag(
                "Hello".GetHashCode(StringComparison.OrdinalIgnoreCase)
                    == "hELLO".GetHashCode(StringComparison.OrdinalIgnoreCase)
            );

        return text;
    }
}
