using System.Collections.Immutable;
using Metano.Interop;

namespace Conformance;

/// <summary>
/// Spec 011 FR-056a (#737): <c>ReadOnlyJsArray&lt;T&gt;</c>,
/// <c>AsReadOnlyJsArray()</c>, and <c>ToJsArray()</c> across
/// <c>List&lt;T&gt;</c> and <c>IReadOnlyList&lt;T&gt;</c> implementers,
/// matching .NET output on Bun and Node.
/// </summary>
public static class ReadOnlyListViewProbes
{
    public static string AsReadOnlyJsArrayOnIReadOnlyListFromList()
    {
        var list = new List<int> { 10, 20, 30 };
        IReadOnlyList<int> view = list;
        var arr = view.AsReadOnlyJsArray();
        var doubled = arr.Map(x => x * 2);

        return $"{arr.Length},{arr[0]},{arr.Join(",")}|{doubled.Join(",")}";
    }

    public static string AsReadOnlyJsArrayReflectsLaterListMutations()
    {
        var list = new List<int> { 1, 2 };
        var directView = list.AsReadOnlyJsArray();
        IReadOnlyList<int> roInterface = list;
        var interfaceView = roInterface.AsReadOnlyJsArray();
        var readOnlyView = list.AsReadOnly().AsReadOnlyJsArray();

        list.Add(3);
        list[0] = 9;

        JsArray<int> jsArr = [4, 5];
        ReadOnlyJsArray<int> implicitFromJsArray = jsArr;

        jsArr.Push(6);

        return string.Join(
            "|",
            $"{directView.Length}:{directView.Join(",")}",
            $"{interfaceView.Length}:{interfaceView.Join(",")}",
            $"{readOnlyView.Length}:{readOnlyView.Join(",")}",
            $"{implicitFromJsArray.Length}:{implicitFromJsArray.Join(",")}"
        );
    }

    public static string AsReadOnlyJsArrayOnReadOnlyAndImmutables()
    {
        var list = new List<int> { 1, 2 };
        var ro = list.AsReadOnly();
        var immList = ImmutableList.Create(3, 4);
        var immArray = ImmutableArray.Create(5, 6);
        IReadOnlyList<int> fromArray = new[] { 7, 8 };
        IReadOnlyList<int> roInterface = ro;

        return string.Join(
            "|",
            ro.AsReadOnlyJsArray().Join(","),
            immList.AsReadOnlyJsArray().Join(","),
            immArray.AsReadOnlyJsArray().Join(","),
            fromArray.AsReadOnlyJsArray().Join(","),
            InteropExtensions.AsReadOnlyJsArray(roInterface).Join(",")
        );
    }

    public static string ToJsArrayOnIReadOnlyListCopies()
    {
        var list = new List<int> { 1, 2 };
        IReadOnlyList<int> ro = list;
        var copy = ro.ToJsArray();

        copy.Push(99);
        list.Add(3);

        return $"{list.Count}:{string.Join(",", list)}|{copy.Length}:{copy.Join(",")}";
    }

    public static string AsReadOnlyJsArrayNullIReadOnlyListThrows()
    {
        try
        {
            IReadOnlyList<int> missing = null!;
            var _ = missing.AsReadOnlyJsArray();

            return "no-throw";
        }
        catch (ArgumentNullException ex)
        {
            return $"{ex.ParamName}|{ex.Message}";
        }
    }
}
