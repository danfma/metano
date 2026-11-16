namespace BclReference;

/// <summary>
/// The rows for <c>metano-runtime/system/string-members</c> (#661): each one runs the C#
/// member the helper stands for.
/// </summary>
internal static class StringRows
{
    private const string Text = "abcdef";

    private static readonly object Omitted = FixtureWriter.Omitted;

    public static void AddTo(FixtureWriter rows)
    {
        AddSubstring(rows);
        AddRemoveInsertPad(rows);
        AddSearch(rows);
        AddComparisons(rows);
        AddTrim(rows);
        AddSplit(rows);
        AddCharArrayAndJoin(rows);
    }

    private static void AddSubstring(FixtureWriter rows)
    {
        rows.Each(
            "stringSubstring",
            a => ((string)a[0]!).Substring((int)a[1]!),
            [Text, 0],
            [Text, 2],
            [Text, 6],
            [Text, 7],
            [Text, -1],
            ["", 0],
            ["", 1]
        );

        rows.Each(
            "stringSubstring",
            a => ((string)a[0]!).Substring((int)a[1]!, (int)a[2]!),
            [Text, 1, 2],
            [Text, 0, 6],
            [Text, 6, 0],
            [Text, 1, 6],
            [Text, 1, 5],
            [Text, 7, 0],
            [Text, -1, 2],
            [Text, 1, -1],
            [Text, 0, 0],
            ["", 0, 0],
            ["", 0, 1]
        );
    }

    private static void AddRemoveInsertPad(FixtureWriter rows)
    {
        rows.Each(
            "stringRemove",
            a => ((string)a[0]!).Remove((int)a[1]!),
            [Text, 0],
            [Text, 2],
            [Text, 6],
            [Text, 7],
            [Text, -1]
        );

        rows.Each(
            "stringRemove",
            a => ((string)a[0]!).Remove((int)a[1]!, (int)a[2]!),
            [Text, 2, 2],
            [Text, 0, 6],
            [Text, 6, 0],
            [Text, 2, 5],
            [Text, 2, -1],
            [Text, -1, 1],
            [Text, 7, 0]
        );

        rows.Each(
            "stringInsert",
            a => ((string)a[0]!).Insert((int)a[1]!, (string)a[2]!),
            [Text, 0, "x"],
            [Text, 2, "xy"],
            [Text, 6, "x"],
            [Text, 7, "x"],
            [Text, -1, "x"],
            [Text, 2, null]
        );

        rows.Each(
            "stringPadLeft",
            a => ((string)a[0]!).PadLeft((int)a[1]!),
            [Text, 8],
            [Text, 3],
            [Text, 0],
            [Text, -1]
        );

        rows.Each(
            "stringPadLeft",
            a => ((string)a[0]!).PadLeft((int)a[1]!, ((string)a[2]!)[0]),
            [Text, 8, "*"],
            [Text, 3, "*"]
        );

        rows.Each(
            "stringPadRight",
            a => ((string)a[0]!).PadRight((int)a[1]!),
            [Text, 8],
            [Text, 3],
            [Text, -1]
        );

        rows.Each(
            "stringPadRight",
            a => ((string)a[0]!).PadRight((int)a[1]!, ((string)a[2]!)[0]),
            [Text, 8, "*"],
            [Text, 3, "*"]
        );
    }

    private static void AddSearch(FixtureWriter rows)
    {
        rows.Each(
            "stringIndexOf",
            a => ((string)a[0]!).IndexOf((string)a[1]!, (int)a[2]!),
            [Text, "c", 0],
            [Text, "c", 3],
            [Text, "c", 2],
            [Text, "", 6],
            [Text, "", 3],
            [Text, "c", 6],
            [Text, "c", 7],
            [Text, "c", -1],
            ["abcabc", "bc", 2],
            [Text, null, 0]
        );

        rows.Each(
            "stringIndexOf",
            a => ((string)a[0]!).IndexOf((string)a[1]!, (int)a[2]!, (int)a[3]!),
            [Text, "c", 1, 2],
            [Text, "c", 1, 1],
            [Text, "cd", 2, 2],
            [Text, "cd", 2, 1],
            [Text, "c", 1, 9],
            [Text, "c", 1, -1],
            [Text, "c", 7, 0],
            [Text, "", 2, 0],
            [Text, "c", 6, 0]
        );

        rows.Each(
            "stringIndexOf",
            a => ((string)a[0]!).IndexOf((string)a[1]!, (int)a[2]!, (int)a[3]!, Comparison(a[4]!)),
            [Text, "C", 1, 4, 5],
            [Text, "C", 1, 4, 4],
            [Text, "CD", 0, 6, 5],
            [Text, "CD", 0, 3, 5]
        );

        rows.Each(
            "stringLastIndexOf",
            a => ((string)a[0]!).LastIndexOf((string)a[1]!, (int)a[2]!),
            [Text, "c", 5],
            [Text, "cd", 3],
            [Text, "cd", 2],
            [Text, "c", 6],
            [Text, "c", 7],
            [Text, "c", -1],
            [Text, "c", 2],
            [Text, "c", 1],
            [Text, "a", 0],
            [Text, "", 3],
            [Text, "", 5],
            [Text, "", 6],
            ["", "", 0],
            ["", "x", 0],
            ["abcabc", "bc", 5],
            ["abcabc", "bc", 3],
            [Text, "abc", 1],
            [Text, "abc", 2],
            [Text, "abcdef", 5],
            [Text, "abcdefg", 5]
        );

        rows.Each(
            "stringLastIndexOf",
            a => ((string)a[0]!).LastIndexOf((string)a[1]!, (int)a[2]!, (int)a[3]!),
            [Text, "c", 3, 2],
            [Text, "c", 3, 1],
            [Text, "cd", 3, 2],
            [Text, "cd", 3, 1],
            [Text, "c", 5, 6],
            [Text, "c", 5, 7],
            [Text, "c", 2, -1],
            [Text, "c", 2, 4],
            [Text, "a", 0, 1]
        );

        rows.Each(
            "stringLastIndexOf",
            a => ((string)a[0]!).LastIndexOf((string)a[1]!, Comparison(a[4]!)),
            [Text, "C", Omitted, Omitted, 5],
            [Text, "C", Omitted, Omitted, 4],
            [Text, "", Omitted, Omitted, 4]
        );

        rows.Each(
            "stringLastIndexOf",
            a => ((string)a[0]!).LastIndexOf((string)a[1]!, (int)a[2]!, Comparison(a[4]!)),
            [Text, "C", 5, Omitted, 5],
            [Text, "C", 1, Omitted, 5]
        );
    }

    private static void AddComparisons(FixtureWriter rows)
    {
        rows.Each(
            "stringStartsWith",
            a => ((string)a[0]!).StartsWith((string)a[1]!, Comparison(a[2]!)),
            ["Abc", "a", 5],
            ["Abc", "a", 4],
            ["Abc", "AB", 5],
            ["Abc", "", 4],
            ["Abc", "abcd", 5],
            ["Abc", null, 4]
        );

        rows.Each(
            "stringEndsWith",
            a => ((string)a[0]!).EndsWith((string)a[1]!, Comparison(a[2]!)),
            ["Abc", "C", 5],
            ["Abc", "C", 4],
            ["Abc", "", 4],
            ["Abc", "xabc", 5],
            ["Abc", null, 4]
        );

        rows.Each(
            "stringContains",
            a => ((string)a[0]!).Contains((string)a[1]!, Comparison(a[2]!)),
            ["Abc", "B", 5],
            ["Abc", "B", 4],
            ["Abc", "", 4],
            ["Abc", null, 4]
        );

        rows.Each(
            "stringReplace",
            a => ((string)a[0]!).Replace((string)a[1]!, (string?)a[2], Comparison(a[3]!)),
            ["aXbxX", "x", "-", 5],
            ["aXbxX", "x", "-", 4],
            ["aXbxX", "x", "-", 3],
            ["aXbxX", "x", "-", 0],
            ["aXbxX", "X", null, 1],
            ["aXbxX", "xx", "$&", 5],
            ["\u00C0\u00E0B", "\u00E0", "_", 5],
            ["abc", "", "z", 4],
            ["abc", null, "z", 5]
        );
    }

    private static void AddTrim(FixtureWriter rows)
    {
        rows.Each(
            "stringTrim",
            a => ((string)a[0]!).Trim(((string)a[1]!)[0]),
            ["aabcaa", "a"],
            ["aaaa", "a"],
            ["bcd", "a"],
            ["", "a"]
        );

        rows.Each(
            "stringTrim",
            a => ((string)a[0]!).Trim(Chars(a, 1)),
            ["abxba", "a", "b"],
            ["abxba", "b", "a"],
            [" abxba ", "a", "b"],
            ["xyz", "x", "y", "z"]
        );

        rows.Each("stringTrim", a => ((string)a[0]!).Trim(), ["  x  "], [""]);

        rows.Each(
            "stringTrimStart",
            a => ((string)a[0]!).TrimStart(((string)a[1]!)[0]),
            ["aabcaa", "a"],
            ["aaaa", "a"]
        );

        rows.Each(
            "stringTrimStart",
            a => ((string)a[0]!).TrimStart(Chars(a, 1)),
            ["abxba", "a", "b"]
        );

        rows.Each(
            "stringTrimEnd",
            a => ((string)a[0]!).TrimEnd(((string)a[1]!)[0]),
            ["aabcaa", "a"],
            ["aaaa", "a"]
        );

        rows.Each("stringTrimEnd", a => ((string)a[0]!).TrimEnd(Chars(a, 1)), ["abxba", "a", "b"]);
    }

    private static void AddSplit(FixtureWriter rows)
    {
        // stringSplit(value, separator, isCharArray, count, options)
        rows.Each(
            "stringSplit",
            a => ((string)a[0]!).Split(((string)a[1]!)[0]),
            ["a,b,,c", ",", false],
            ["", ",", false],
            [",", ",", false],
            ["abc", ",", false]
        );

        rows.Each(
            "stringSplit",
            a => ((string)a[0]!).Split(((string)a[1]!)[0], (int)a[3]!),
            ["a,b,,c", ",", false, 2],
            ["a,b,,c", ",", false, 1],
            ["a,b,,c", ",", false, 0],
            ["a,b,,c", ",", false, 3],
            ["a,b,,c", ",", false, 9],
            ["a,b,,c", ",", false, -1]
        );

        rows.Each(
            "stringSplit",
            a => ((string)a[0]!).Split(((string)a[1]!)[0], (StringSplitOptions)(int)a[4]!),
            ["a,b,,c", ",", false, Omitted, 0],
            ["a,b,,c", ",", false, Omitted, 1],
            ["a , b ,, c", ",", false, Omitted, 2],
            ["a , b ,  , c", ",", false, Omitted, 3],
            ["", ",", false, Omitted, 1],
            [",,", ",", false, Omitted, 1],
            ["  ", ",", false, Omitted, 3]
        );

        rows.Each(
            "stringSplit",
            a =>
                ((string)a[0]!).Split(
                    ((string)a[1]!)[0],
                    (int)a[3]!,
                    (StringSplitOptions)(int)a[4]!
                ),
            ["a,,b,c", ",", false, 2, 1],
            ["a,,b,c", ",", false, 3, 1],
            ["a,,b,c", ",", false, 1, 1],
            [",,", ",", false, 1, 1],
            [" a , b , c ", ",", false, 2, 2],
            [" a , b , c ", ",", false, 1, 3]
        );

        rows.Each(
            "stringSplit",
            a => ((string)a[0]!).Split((string?)a[1]),
            ["xabyabz", "ab", false],
            ["xab", "", false],
            ["x ab", null, false]
        );

        rows.Each(
            "stringSplit",
            a => ((string)a[0]!).Split((string?)a[1], (int)a[3]!, StringSplitOptions.None),
            ["xabyabz", "ab", false, 2],
            ["xabyabz", "ab", false, 1]
        );

        rows.Each(
            "stringSplit",
            a => ((string)a[0]!).Split(Strings(a[1]!), (StringSplitOptions)(int)a[4]!),
            ["x,a;b", new[] { ",", ";" }, true, Omitted, 0],
            ["xabab", new[] { "ab", "a" }, true, Omitted, 0],
            ["xabab", new[] { "a", "ab" }, true, Omitted, 0],
            ["xabab", new[] { "", "b" }, true, Omitted, 0],
            ["x,,a", new[] { "," }, true, Omitted, 1],
            ["x ab", new string[0], true, Omitted, 0]
        );

        rows.Each(
            "stringSplit",
            a => ((string)a[0]!).Split(Strings(a[1]!), (int)a[3]!, (StringSplitOptions)(int)a[4]!),
            ["x,a;b", new[] { ",", ";" }, true, 2, 0]
        );

        rows.Each(
            "stringSplit",
            a => ((string)a[0]!).Split(Chars(a, 1), (int)a[3]!),
            ["x,a;b", new[] { ",", ";" }, true, 2],
            ["x a", new string[0], true, 5]
        );

        rows.Each(
            "stringSplit",
            a => ((string)a[0]!).Split(Chars(a, 1), (StringSplitOptions)(int)a[4]!),
            ["x,,a;b", new[] { ",", ";" }, true, Omitted, 1],
            ["x  a", new string[0], true, Omitted, 1],
            ["x  a", new string[0], true, Omitted, 0]
        );

        rows.Each(
            "stringSplitChars",
            a => ((string)a[0]!).Split(Chars(a, 1)),
            ["x,a;b", ",", ";"],
            ["x a\tb\nc"],
            ["x a\u0085b﻿c"],
            ["", ","],
            ["abc"]
        );
    }

    private static void AddCharArrayAndJoin(FixtureWriter rows)
    {
        rows.Each("stringToCharArray", a => ((string)a[0]!).ToCharArray(), [Text], [""]);

        rows.Each(
            "stringToCharArray",
            a => ((string)a[0]!).ToCharArray((int)a[1]!, (int)a[2]!),
            [Text, 1, 2],
            [Text, 0, 6],
            [Text, 6, 0],
            [Text, 1, 6],
            [Text, 7, 0],
            [Text, -1, 1],
            [Text, 1, -1]
        );

        var values = new[] { "a", "b", "c", "d" };

        rows.Each(
            "stringJoinRange",
            a => string.Join((string?)a[0], Strings(a[1]!), (int)a[2]!, (int)a[3]!),
            [",", values, 1, 2],
            [",", values, 0, 4],
            [",", values, 4, 0],
            [",", values, 1, 5],
            [",", values, -1, 2],
            [",", values, 1, -1],
            [null, values, 1, 2],
            ["", values, 1, 2],
            [",", null, 0, 0]
        );
    }

    private static StringComparison Comparison(object value) => (StringComparison)(int)value;

    private static string[] Strings(object value) => (string[])value;

    private static char[] Chars(object?[] arguments, int first) =>
        arguments.Length > first && arguments[first] is string[] set
            ? set.Select(s => s[0]).ToArray()
            : arguments.Skip(first).Select(s => ((string)s!)[0]).ToArray();
}
