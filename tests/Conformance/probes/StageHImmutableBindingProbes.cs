using System.Collections.Immutable;

namespace Conformance;

/// <summary>
/// Spec 011 stage 011h: a mutable-interface view of an immutable collection
/// (contracts/runtime-collections.md § Mutable-interface conversions of
/// immutables). In .NET the immutable collections implement <c>ISet&lt;T&gt;</c>,
/// <c>IDictionary&lt;K, V&gt;</c> and <c>IList&lt;T&gt;</c> with mutators that throw
/// <c>NotSupportedException</c>; the runtime classes cannot reproduce that, so
/// every form that would mutate through the interface is reported (MS0001) and
/// lowers to a placeholder that fails loudly. The plain test answers as .NET.
/// </summary>
public static class StageHImmutableBindingProbes
{
    static string Attempt(Func<string> action)
    {
        try
        {
            return action();
        }
        catch (NotSupportedException ex)
        {
            return $"NotSupportedException:{ex.Message}";
        }
    }

    static void MutateList<TList>(TList list)
        where TList : IList<int>
    {
        list.Add(99);
    }

    public static string ImmutableDictionaryAs()
    {
        object o = ImmutableDictionary<string, int>.Empty.Add("a", 1);

        return Attempt(() =>
        {
            var d = o as IDictionary<string, int>;
            d!["b"] = 2;
            return $"set:{d.Count}";
        });
    }

    public static string ImmutableGenericConstraint()
    {
        var il = ImmutableList.Create(1, 2);

        return Attempt(() =>
        {
            MutateList(il);
            return $"no-throw:{il.Count}";
        });
    }

    public static string ImmutableSetCast()
    {
        object o = ImmutableHashSet.Create(1, 2);

        return Attempt(() =>
        {
            var s = (ISet<int>)o;
            s.Add(3);
            return $"added:{s.Count}";
        });
    }

    public static string ImmutableDictionaryCast()
    {
        IImmutableDictionary<string, int> o = ImmutableDictionary<string, int>.Empty.Add("a", 1);

        return Attempt(() =>
        {
            var d = (IDictionary<string, int>)o;
            d.Add("b", 2);
            return $"added:{d.Count}";
        });
    }

    public static string ImmutableConstraintMethodGroup()
    {
        var il = ImmutableList.Create(1, 2);

        return Attempt(() =>
        {
            Action<ImmutableList<int>> fn = MutateList<ImmutableList<int>>;
            fn(il);
            return $"no-throw:{il.Count}";
        });
    }

    public static string ImmutableArrayBuilderCountSetter()
    {
        var b = ImmutableArray.CreateBuilder<int>();
        b.Add(10);
        b.Add(20);
        b.Add(30);
        b.Count = 1;
        return $"{b.Count}:{b[0]}";
    }
}

/// <summary>A declaration pattern lowers to a placeholder that fails the module at load; one module per probe.</summary>
public static class StageHImmutableSetBindingProbes
{
    public static string ImmutableSetBinding()
    {
        object o = ImmutableHashSet.Create(1, 2);

        try
        {
            if (o is ISet<int> s)
            {
                s.Add(3);
                return $"added:{s.Count}";
            }

            return "no";
        }
        catch (NotSupportedException ex)
        {
            return $"NotSupportedException:{ex.Message}";
        }
    }
}

public static class StageHImmutableDictionaryBindingProbes
{
    public static string ImmutableDictionaryBinding()
    {
        object o = ImmutableDictionary<string, int>.Empty.Add("a", 1);

        try
        {
            if (o is IDictionary<string, int> d)
            {
                d.Add("b", 2);
                return $"added:{d.Count}";
            }

            return "no";
        }
        catch (NotSupportedException ex)
        {
            return $"NotSupportedException:{ex.Message}";
        }
    }
}
