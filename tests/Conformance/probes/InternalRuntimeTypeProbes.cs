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
