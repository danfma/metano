using System.Collections.Immutable;
using Metano.Interop;

namespace Conformance;

/// <summary>
/// Spec 011 FR-056 (#737): <c>AsJsArray()</c> over <c>IReadOnlyList&lt;T&gt;</c>
/// and its runtime implementers, matching .NET output on Bun and Node.
/// </summary>
public static class ReadOnlyListViewProbes
{
    public static string AsJsArrayOnIReadOnlyListFromList()
    {
        var list = new List<int> { 10, 20, 30 };
        IReadOnlyList<int> view = list;
        var arr = view.AsJsArray();
        var doubled = arr.Map(x => x * 2);
        return $"{arr.Length},{arr[0]},{arr.Join(",")}|{doubled.Join(",")}";
    }

    public static string AsJsArrayOnReadOnlyAndImmutables()
    {
        var list = new List<int> { 1, 2 };
        var ro = list.AsReadOnly();
        var immList = ImmutableList.Create(3, 4);
        var immArray = ImmutableArray.Create(5, 6);
        IReadOnlyList<int> fromArray = new[] { 7, 8 };
        IReadOnlyList<int> roInterface = ro;

        return string.Join(
            "|",
            ro.AsJsArray().Join(","),
            immList.AsJsArray().Join(","),
            immArray.AsJsArray().Join(","),
            fromArray.AsJsArray().Join(","),
            InteropExtensions.AsJsArray(roInterface).Join(",")
        );
    }

    public static string AsJsArrayNullIReadOnlyListThrows()
    {
        try
        {
            IReadOnlyList<int> missing = null!;
            var _ = missing.AsJsArray();
            return "no-throw";
        }
        catch (ArgumentNullException ex)
        {
            return $"{ex.ParamName}|{ex.Message}";
        }
    }
}
