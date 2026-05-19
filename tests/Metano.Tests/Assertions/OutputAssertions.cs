namespace Metano.Tests.Assertions;

/// <summary>
/// Shared helpers for asserting on transpiler output. Hoisted so the
/// per-file copies (LINQ fusion, extension property setter, extension
/// indexer) all hit the same code path — a future overflow / off-by-one
/// fix only happens here.
/// </summary>
public static class OutputAssertions
{
    /// <summary>
    /// Counts non-overlapping occurrences of <paramref name="needle"/>
    /// inside <paramref name="haystack"/>. Ordinal comparison so a test
    /// asserting <c>"where("</c> does not match <c>"Where("</c>. Empty
    /// needle returns 0 (mirrors the contract the per-file copies
    /// had).
    /// </summary>
    public static int CountOccurrences(string haystack, string needle)
    {
        if (string.IsNullOrEmpty(needle))
            return 0;
        var count = 0;
        var index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }
        return count;
    }
}
