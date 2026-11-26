using System.Collections;

namespace Conformance;

// Wave 13 group A (#804): object and collection initializers, and every other binding
// of a temporary, lower without a closure. These rows pin the evaluation order and the
// scoping of the temporaries in each position the lowering distinguishes.

public class NcLog
{
    private readonly List<string> _entries = new();

    public T Note<T>(string entry, T value)
    {
        _entries.Add(entry);

        return value;
    }

    public void Add(string entry) => _entries.Add(entry);

    public override string ToString() => string.Join(",", _entries);
}

public class NcBox
{
    public int V { get; set; }

    public int W { get; init; }

    public NcBox? Inner { get; set; }

    public static NcBox Failing() => throw new InvalidOperationException("no box");
}

public class NcBag : IEnumerable<int>
{
    private readonly List<int> _items = new();

    public void Add(int item)
    {
        if (item < 0)
            throw new ArgumentException("negative");

        _items.Add(item);
    }

    public void Add(NcBag nested) => _items.Add(nested.Sum());

    public int Sum() => _items.Sum();

    public int Count => _items.Count;

    public IEnumerator<int> GetEnumerator() => _items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

public class NcHolder
{
    public static readonly NcBox Shared = new NcBox { V = 9, W = 10 };

    public NcBox Own = new NcBox { V = 7, W = 8 };

    public NcBag Bag = new NcBag { 1, 2, 3 };

    public int Sum => Own.V + Own.W + Bag.Sum();
}

public static class NcBoxExtensions
{
    public static int Doubled(this NcBox box) => box.V * 2;
}

public static class NoClosureTemporaryProbes
{
    private static int Use(NcBox box) => box.V * 100 + box.W;

    private static int UseBoth(NcBox left, NcBox right) => Use(left) * 1000 + Use(right);

    private static NcBox MakeBox(int v) => new NcBox { V = v, W = v + 1 };

    private static int Total(NcBag bag) => bag.Sum();

    public static string StatementPositions()
    {
        var log = new NcLog();
        var declared = new NcBox { V = log.Note("v", 1), W = log.Note("w", 2) };
        NcBox assigned;
        assigned = new NcBox { W = log.Note("w2", 4), V = log.Note("v2", 3) };

        return $"{declared.V}{declared.W}{assigned.V}{assigned.W}|{log}|{MakeBox(5).W}";
    }

    public static string ExpressionPositions()
    {
        var log = new NcLog();
        var both = UseBoth(
            new NcBox { V = log.Note("a", 1), W = log.Note("b", 2) },
            new NcBox { V = log.Note("c", 3), W = log.Note("d", 4) }
        );
        var conditional = log.Note("cond", true) ? Use(new NcBox { V = 5 }) : -1;

        return $"{both}|{conditional}|{log}";
    }

    public static string NestedInitializers()
    {
        var log = new NcLog();
        var outer = new NcBox
        {
            V = log.Note("outer.v", 1),
            Inner = new NcBox
            {
                V = log.Note("inner.v", 2),
                Inner = new NcBox { W = log.Note("deep.w", 3) },
            },
            W = log.Note("outer.w", 4),
        };
        var bag = new NcBag
        {
            1,
            new NcBag { 2, 3 },
            4,
        };

        return $"{outer.V}{outer.Inner!.V}{outer.Inner.Inner!.W}{outer.W}|{bag.Sum()}|{log}";
    }

    public static string ExceptionInTheMiddle()
    {
        var log = new NcLog();
        NcBox? box = null;

        try
        {
            box = new NcBox
            {
                V = log.Note("v", 1),
                Inner = NcBox.Failing(),
                W = log.Note("w", 2),
            };
        }
        catch (InvalidOperationException e)
        {
            log.Add(e.Message);
        }

        var bagCount = -1;

        try
        {
            var bag = new NcBag { 1, -1, 3 };
            bagCount = bag.Count;
        }
        catch (ArgumentException e)
        {
            log.Add(e.Message);
        }

        return $"{box is null}|{bagCount}|{log}";
    }

    public static string InitializersInALoop()
    {
        var boxes = new List<NcBox>();
        var readers = new List<Func<int>>();

        for (var i = 0; i < 3; i++)
        {
            boxes.Add(new NcBox { V = i, W = i * 2 });

            var made = new NcBox { V = i + 10 };
            readers.Add(() => made.V);
        }

        return $"{string.Join(",", boxes.Select(Use))}|{string.Join(",", readers.Select(r => r()))}";
    }

    public static string InitializersInLambdas()
    {
        Func<int, NcBox> make = v => new NcBox { V = v, W = v };
        Func<int, int> used = v => Use(new NcBox { V = v });
        Action<List<NcBox>> append = list => list.Add(new NcBox { V = 4 });
        var target = new List<NcBox>();
        append(target);

        return $"{Use(make(1))}|{used(2)}|{target[0].V}|{Total(new NcBag { 5, 6 })}";
    }

    public static string ExpressionBodiedMembers() =>
        $"{MakeBox(3).W}|{Use(new NcBox { V = 1, W = 2 })}|{new NcBag { 7 }.Sum()}";

    public static string Generator() => string.Join(",", Boxes().Select(box => box.V));

    private static IEnumerable<NcBox> Boxes()
    {
        yield return new NcBox { V = 1 };
        yield return MakeBox(new NcBox { V = 2 }.V);

        for (var i = 3; i < 5; i++)
            yield return new NcBox { V = i };
    }

    public static async Task<string> AsyncInitializers()
    {
        var log = new NcLog();
        var box = new NcBox
        {
            V = log.Note("before", 1),
            W = await Task.FromResult(log.Note("awaited", 2)),
        };
        var used = Use(new NcBox { V = await Task.FromResult(3), W = log.Note("after", 4) });

        return $"{box.V}{box.W}|{used}|{log}";
    }

    public static string SwitchSections()
    {
        var parts = new List<int>();

        for (var k = 0; k < 4; k++)
        {
            switch (k)
            {
                case 0:
                    var first = new NcBox { V = 1 };
                    parts.Add(first.V);
                    break;
                case 1:
                    parts.Add(Use(new NcBox { V = 2 }));
                    break;
                case 2:
                    var second = new NcBox { V = 3 };
                    parts.Add(second.V);
                    break;
                default:
                    parts.Add(new NcBag { 4, 5 }.Sum());
                    break;
            }
        }

        return string.Join(",", parts);
    }

    public static string FieldAndStaticInitializers()
    {
        var holder = new NcHolder();
        var other = new NcHolder();
        other.Own.V = 100;

        return $"{holder.Sum}|{other.Sum}|{NcHolder.Shared.V}{NcHolder.Shared.W}";
    }

    public static string InitOnlyStores()
    {
        var box = new NcBox { W = 5 };
        var nested = new NcBox { Inner = new NcBox { W = 6 } };

        return $"{box.W}|{nested.Inner!.W}";
    }

    public static string TemporariesNeverCollide()
    {
        var a = new NcBox { V = 1 };
        var b = new NcBox
        {
            V = 2,
            Inner = new NcBox { V = 3 },
        };
        var sum = UseBoth(
            new NcBox { V = 4 },
            new NcBox
            {
                V = 5,
                Inner = new NcBox { V = 6 },
            }
        );
        var receiver = new NcBox { V = 7 };
        var t0 = new NcBox { V = 8 };

        return $"{a.V}{b.V}{b.Inner!.V}|{sum}|{receiver.V}{t0.V}";
    }

    public static string NullConditionalReceivers()
    {
        var log = new NcLog();
        var present = log.Note<NcBox?>("present", new NcBox { V = 4 })?.Doubled();
        var absent = log.Note<NcBox?>("absent", null)?.Doubled();

        return $"{present}|{absent?.ToString() ?? "null"}|{log}";
    }

    public static string CaseGuardsReadingDesignators()
    {
        var parts = new List<string>();
        object[] values = [new NcBox { V = 1 }, new NcBox { V = 5 }, "text"];

        foreach (var value in values)
        {
            switch (value)
            {
                case NcBox box when box.V > 2:
                    parts.Add($"big{box.V}");
                    break;
                case NcBox box:
                    parts.Add($"small{box.V}");
                    break;
                default:
                    parts.Add("other");
                    break;
            }
        }

        return string.Join(",", parts);
    }
}
