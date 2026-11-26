using System;

namespace Conformance;

/// <summary>A public base whose internal method an internal descendant forwards its interface to (#819).</summary>
public abstract class OwnerNode
{
    public int Disposed;

    internal void Dispose() => Disposed++;
}

internal abstract class EffectNode : OwnerNode, IDisposable
{
    void IDisposable.Dispose() => Dispose();
}

internal sealed class SplitEffect<T> : EffectNode { }

/// <summary>A public sibling of the internal descendant: not <c>IDisposable</c>, no public <c>Dispose</c> (#821).</summary>
public abstract class ComputedNode : OwnerNode { }

public sealed class Computed<T> : ComputedNode { }

public interface IWorkItem
{
    string Run();
}

internal class WorkBase : IWorkItem
{
    public virtual string Run() => "base";
}

internal sealed class WorkDerived : WorkBase
{
    public override string Run() => "derived";
}

public static class InternalRuntimeTypeProbes
{
    public static IDisposable Effect<T>() => new SplitEffect<T>();

    public static IWorkItem Work() => new WorkDerived();

    public static string IsIDisposableOfAnInternalDescendant()
    {
        object effect = Effect<int>();
        object owner = new SplitEffect<string>();
        object text = "s";

        return string.Join(",", effect is IDisposable, owner is IDisposable, text is IDisposable);
    }

    public static string DisposeThroughTheInterfaceOfAnInternalDescendant()
    {
        var effect = new SplitEffect<int>();

        ((IDisposable)effect).Dispose();
        Effect<int>().Dispose();

        return effect.Disposed.ToString();
    }

    public static string UsingOverAnInternalDescendant()
    {
        var effect = new SplitEffect<int>();

        using (effect) { }

        return effect.Disposed.ToString();
    }

    public static string IsIDisposableOfASiblingOfTheInternalDescendant()
    {
        object computed = new Computed<int>();
        object node = new SplitEffect<int>();

        return string.Join(
            ",",
            computed is IDisposable,
            computed is OwnerNode,
            node is IDisposable
        );
    }

    public static string DispatchOnTheInterfaceOfAnInternalDescendant() =>
        string.Join(",", Describe(Effect<int>()), Describe(new object()), Describe(Work()));

    public static string UserInterfaceOfAnInternalDescendant()
    {
        object work = Work();

        return string.Join(",", work is IWorkItem, work is IDisposable, ((IWorkItem)work).Run());
    }

    private static string Describe(IDisposable value) => "disposable";

    private static string Describe(IWorkItem value) => "work";

    private static string Describe(object value) => "object";
}
