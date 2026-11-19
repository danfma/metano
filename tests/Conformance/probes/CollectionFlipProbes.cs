namespace Conformance;

/// <summary>
/// Spec 011 stage 011c, hand-in C2: the BCL collections as metano-runtime
/// classes, end to end (element updates, index-from-end, ranges, initializers,
/// params collections, array views, the lowered foreach, LINQ terminals).
/// </summary>
public static class CollectionFlipProbes
{
    static int _calls;

    static int Next() => _calls++;

    public static string SortNumeric()
    {
        var l = new List<int> { 10, 9, 100, 1, -5 };
        l.Sort();
        var d = new List<double> { 2.5, 10.0, 1.25 };
        d.Sort();
        return string.Join(",", l) + "|" + string.Join(",", d);
    }

    public static string UpdatesInExpressions()
    {
        var l = new List<int> { 1, 2, 3 };
        IList<int> i = new List<int> { 10, 20 };
        var a = l[0]++;
        var b = ++l[1];
        var c = (l[2] *= 4);
        var e = i[0]++;
        var f = (i[1] += 5);
        var n = new List<int?> { null, 4 };
        var g = (n[0] ??= 7);
        var h = (n[1] ??= 9);
        return $"{a},{b},{c},{e},{f},{g},{h}|{string.Join(",", l)}|{string.Join(",", i)}|{n[0]},{n[1]}";
    }

    public static string UpdatesEvaluateOnce()
    {
        _calls = 0;
        var l = new List<int> { 0, 0, 0 };
        l[Next()] += 5;
        l[Next()]++;
        var d = new Dictionary<string, int> { ["a"] = 1 };
        d["a"] += 2;
        var x = d["a"]++;
        return $"{_calls}|{string.Join(",", l)}|{d["a"]},{x}";
    }

    public static string FromEndAndRanges()
    {
        var l = new List<int> { 1, 2, 3, 4, 5 };
        IReadOnlyList<int> r = l;
        var arr = new[] { 7, 8, 9 };
        l[^1] = 50;
        l[^2] += 1;
        return $"{l[^1]},{r[^2]},{arr[^1]}|{string.Join(",", l[1..^1])}|{string.Join(",", l[..2])}";
    }

    public static string Initializers()
    {
        var l = new List<int>(4) { 1, 2 };
        var s = new HashSet<string> { "a", "b", "a" };
        var d = new Dictionary<string, int> { ["x"] = 1, ["x"] = 2 };
        var dup = "none";

        try
        {
            var built = new Dictionary<string, int> { { "k", 1 }, { "k", 2 } };
            dup = built.Count.ToString();
        }
        catch (ArgumentException ex)
        {
            dup = ex.Message;
        }
        List<int> fromExpr = [3, .. l];
        return $"{string.Join(",", l)}|{s.Count}|{d["x"]}|{dup}|{string.Join(",", fromExpr)}";
    }

    static int Total(params List<int> xs) => xs.Sum();

    static int CountAll(params IReadOnlyList<int> xs) => xs.Count;

    public static string ParamsCollections() =>
        $"{Total(1, 2, 3)},{Total()},{CountAll(4, 5)},{Total([7, 8])}";

    public static string ArrayViews()
    {
        int[] arr = [1, 2, 3];
        IReadOnlyList<int> view = arr;
        IList<int> list = arr;
        list[0] = 9;
        var thrown = "none";

        try
        {
            list.Add(4);
        }
        catch (NotSupportedException ex)
        {
            thrown = ex.Message;
        }
        return $"{view.Count},{view[0]},{arr[0]},{list.IsReadOnly}|{thrown}";
    }

    public static string ForeachModified()
    {
        var l = new List<int> { 1, 2, 3 };
        var seen = 0;

        try
        {
            foreach (var x in l)
            {
                seen += x;

                if (x == 2)
                    l.Add(4);
            }
        }
        catch (InvalidOperationException ex)
        {
            return $"{seen}|{ex.Message}";
        }
        return $"{seen}|none";
    }

    public static string ForeachBreakAfterMutation()
    {
        var l = new List<int> { 1, 2, 3 };
        var seen = 0;

        foreach (var x in l)
        {
            seen += x;

            if (x == 2)
            {
                l.Add(9);
                break;
            }
        }

        return $"{seen}|{l.Count}";
    }

    public static string SetsQueuesStacks()
    {
        var s = new HashSet<int>();
        var first = s.Add(1);
        var again = s.Add(1);
        var q = new Queue<int>();
        q.Enqueue(1);
        q.Enqueue(2);
        var st = new Stack<int>();
        st.Push(1);
        st.Push(2);
        return $"{first},{again},{s.Count}|{q.Dequeue()},{q.Peek()}|{st.Pop()},{string.Join(",", st)}";
    }

    public static string LinqTerminals()
    {
        int[] xs = [3, 1, 2, 3];
        var list = xs.Where(x => x > 1).ToList();
        list.Add(10);
        var set = xs.ToHashSet();
        var dict = xs.Distinct().ToDictionary(x => x, x => x * 10);
        var dup = "none";

        try
        {
            var built = xs.ToDictionary(x => x);
            dup = built.Count.ToString();
        }
        catch (ArgumentException ex)
        {
            dup = ex.Message;
        }
        return $"{string.Join(",", list)}|{set.Count}|{dict[3]},{dict.Count}|{dup}";
    }

    public static string OutOfRange()
    {
        var l = new List<int> { 1 };

        try
        {
            return l[5].ToString();
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return ex.Message;
        }
    }

    public static string AssignmentAsValue()
    {
        var l = new List<int> { 1, 2, 3 };
        IList<int> s = new List<int> { 4 };
        var d = new Dictionary<string, int>();
        var x = (l[1] = 5);
        var y = 0;

        y = l[2] = 9;

        var t = s[0] = s[0] * 2;
        var z = (l[^1] = 4);

        return $"{x},{y},{t},{z},{d["k"] = 6}|{string.Join(",", l)}|{s[0]}|{d["k"]}";
    }

    public static string ForeachWithCommaUpdates()
    {
        _calls = 0;
        var l = new List<int> { 1, 2 };
        IList<int> counts = new List<int> { 0, 0 };
        var total = 0;

        foreach (var x in l)
        {
            counts[Next() % 2] += x;
            total += counts[Next() % 2]++;

            foreach (var y in l)
                total += y;
        }

        return $"{total}|{string.Join(",", counts)}|{_calls}";
    }

    static string Shape(List<int> l) =>
        l switch
        {
            [] => "empty",
            [var a] => "one" + a,
            [var a, .., var z] => "many" + a + z,
        };

    public static string ListPatternsOnAList()
    {
        var l = new List<int> { 1, 2, 3 };
        var mid = l is [var a, .. var m, var z] ? $"{a}:{string.Join(",", m)}:{z}" : "no";
        var rest = l is [_, .. var r] ? string.Join(",", r) : "no";

        return $"{Shape([])},{Shape([4])},{Shape(l)}|{l is [50, ..]},{l is [1, ..]}|{mid}|{rest}";
    }

    public static string ListPatternsOnInterfaces()
    {
        IList<int> il = new List<int> { 1, 2, 3 };
        IReadOnlyList<int> ro = new List<int> { 1, 2, 3 };
        IList<int> adopted = new[] { 7, 8 };

        var nested = new List<List<int>>
        {
            new() { 1 },
            new() { 2, 3 },
        };

        var inner = nested is [[var x], [_, var y]] ? x + y : -1;

        return $"{il is [1, _, 3]},{il is [1, _]},{ro is [.., 3]},{ro is [2, ..]}|"
            + $"{adopted is [7, 8]},{adopted is [_, .., 9]}|{inner}";
    }

    public static string CastObjectToListInterface()
    {
        var list = new List<int> { 1, 2, 3 };
        var view = (IList<int>)(object)list;

        view[0] = 10;
        view.Add(4);

        var read = (IReadOnlyList<int>)(IEnumerable<int>)list;
        object? none = null;

        return $"{view.Count},{view[1]},{ReferenceEquals(view, list)}|{read[^1]},{read.Count}|"
            + $"{(IList<int>?)none == null}|{string.Join(",", list)}";
    }

    public static string CastObjectArrayToListInterface()
    {
        var array = new[] { 1, 2, 3 };
        var view = (IList<int>)(object)array;
        string add;

        view[0] = 10;

        try
        {
            view.Add(4);
            add = "added";
        }
        catch (NotSupportedException ex)
        {
            add = ex.Message;
        }

        var read = (IReadOnlyCollection<int>)(object)array;

        return $"{view.Count},{view[2]},{view.IsReadOnly}|{array[0]}|{add}|{read.Count}";
    }

    public static string CastStringToListInterface() =>
        CastFailure(() => (IList<char>)(object)"ab");

    public static string CastNumberToListInterface() =>
        CastFailure(() => (IReadOnlyList<int>)(object)5);

    static string CastFailure(Func<object> cast)
    {
        try
        {
            return $"cast {cast()}";
        }
        catch (InvalidCastException ex)
        {
            return $"InvalidCastException: {ex.Message}";
        }
    }
}
