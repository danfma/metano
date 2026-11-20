using System.Collections.Immutable;

namespace Conformance;

/// <summary>
/// Spec 011 stage 011h: <c>is</c> / <c>as</c> / pattern tests of an immutable
/// value against the mutable list interface (W4-66). The forms are group J's
/// descriptor tests (D31); a list-shaped type pattern still lowers to the
/// unsupported-pattern placeholder (W4-17), which fails the module at load,
/// so these rows live apart from <see cref="StageHCollectionProbes"/>.
/// </summary>
public static class StageHInterfaceTestProbes
{
    public static string ImmutableAsIList()
    {
        object o = ImmutableList.Create(1, 2);
        var isList = o is IList<int>;
        var isReadOnly = o is IReadOnlyList<int>;
        return $"{isList},{isReadOnly}";
    }

    public static string ImmutableAsIListBinding()
    {
        object o = ImmutableList.Create(1, 2);

        try
        {
            if (o is IList<int> list)
            {
                list.Add(5);
                return $"added:{list.Count}";
            }
            return "no";
        }
        catch (NotSupportedException ex)
        {
            return ex.Message;
        }
    }

    public static string ImmutableAsIListAs()
    {
        object o = ImmutableArray.Create(1, 2);
        var list = o as IList<int>;
        return list is null ? "null" : list.Count.ToString();
    }

    public static string ImmutableAsIListSwitch()
    {
        object o = ImmutableList.Create(1, 2, 3);
        return o switch
        {
            IList<int> l => $"list:{l.Count}",
            _ => "other",
        };
    }
}
