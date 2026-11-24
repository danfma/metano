using System.Collections;
using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using Metano.Annotations;
using Metano.Interop;

// Probe-local mappings: the product maps neither the static JsonSerializer
// calls nor JsonSerializerContext.Default (it emits `default_`, while the
// generated context exposes `static get default()`; tracked in #741).
[assembly: MapMethod(
    typeof(JsonSerializer),
    nameof(JsonSerializer.Serialize),
    JsTemplate = "JSON.stringify(JsonSerializer.serialize($0, $1))",
    RuntimeImports = "JsonSerializer",
    WhenArgCount = 2
)]
[assembly: MapMethod(
    typeof(JsonSerializer),
    nameof(JsonSerializer.Deserialize),
    JsTemplate = "JsonSerializer.deserializeText($0, $1)",
    RuntimeImports = "JsonSerializer",
    WhenArgCount = 2
)]
[assembly: MapMethod(
    typeof(Conformance.ReadOnlyListViewProbes),
    "GetDefaultContext",
    JsTemplate = "$T0.default"
)]

namespace Conformance;

public sealed record ReadOnlyJsArrayBag(ReadOnlyJsArray<int> Items, ReadOnlyJsArray<string>? Names);

[JsonSerializable(typeof(ReadOnlyJsArrayBag))]
public partial class ReadOnlyJsArrayBagContext : JsonSerializerContext;

public class UserReadOnlyList : IReadOnlyList<int>
{
    private readonly List<int> _items;

    public UserReadOnlyList(List<int> items) => _items = items;

    public int Count => _items.Count;

    public int this[int index] => _items[index];

    public IEnumerator<int> GetEnumerator() => _items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

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

    public static string AsReadOnlyJsArrayOnUserIReadOnlyListClass()
    {
        IReadOnlyList<int> custom = new UserReadOnlyList(new List<int> { 10, 20, 30 });
        var view = custom.AsReadOnlyJsArray();

        return $"{view.Length}:{view[0]},{view[1]},{view[2]}|{view.Join(",")}";
    }

    private static T GetDefaultContext<T>()
        where T : JsonSerializerContext => (T)(object)ReadOnlyJsArrayBagContext.Default;

    public static string ReadOnlyJsArrayJsonRoundTrip()
    {
        var ctx = GetDefaultContext<ReadOnlyJsArrayBagContext>();
        var full = new ReadOnlyJsArrayBag([1, 2, 3], ["a", "b"]);
        var withNull = new ReadOnlyJsArrayBag([4, 5], null);
        var fullJson = JsonSerializer.Serialize(full, ctx.ReadOnlyJsArrayBag);
        var nullJson = JsonSerializer.Serialize(withNull, ctx.ReadOnlyJsArrayBag);
        var roundFull = JsonSerializer.Deserialize(fullJson, ctx.ReadOnlyJsArrayBag)!;
        var roundNull = JsonSerializer.Deserialize(nullJson, ctx.ReadOnlyJsArrayBag)!;

        return string.Join(
            "|",
            fullJson,
            nullJson,
            $"{roundFull.Items.Length}:{roundFull.Items.Join(",")}:{roundFull.Names!.Join(",")}",
            $"{roundNull.Items.Length}:{roundNull.Items.Join(",")}:{(roundNull.Names is null ? "null" : "nonnull")}"
        );
    }
}
