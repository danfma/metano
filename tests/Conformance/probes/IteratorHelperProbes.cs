namespace Conformance;

/// <summary>
/// #745: a runtime helper referenced only inside an iterator (<c>yield return</c>) body
/// keeps its import (the type test <c>isInt32</c> and the decimal narrowing helper are
/// used nowhere else in this module).
/// </summary>
public static class IteratorHelperProbes
{
    static IEnumerable<int> Classify(object[] values, decimal scale)
    {
        foreach (var value in values)
            yield return value is int ? 1 : 0;

        yield return (int)(scale * 2.5m);
    }

    public static string HelpersInsideYield() => string.Join(",", Classify([1, "a", 2L, 3], 3m));
}
