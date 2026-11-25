using System;
using System.Collections.Generic;

namespace Conformance;

/// <summary>A resource that logs its disposal, and optionally throws from it.</summary>
public sealed class DispLog : IDisposable
{
    private readonly List<string> _log;
    private readonly string _name;
    private readonly bool _throws;

    public DispLog(List<string> log, string name, bool throws = false)
    {
        _log = log;
        _name = name;
        _throws = throws;
        _log.Add("open " + name);
    }

    public void Dispose()
    {
        _log.Add("dispose " + _name);

        if (_throws)
            throw new InvalidOperationException("dispose " + _name);
    }
}

/// <summary>Implements the interface explicitly only.</summary>
public sealed class DispExplicit : IDisposable
{
    public int Count;

    void IDisposable.Dispose() => Count++;
}

/// <summary>Derives from an implementer.</summary>
public class DispBase : IDisposable
{
    public int Count;

    public void Dispose() => Count++;
}

public sealed class DispDerived : DispBase { }

/// <summary>Has a Dispose method but does not implement the interface.</summary>
public class DispOwner
{
    public int Count;

    internal void Dispose() => Count++;
}

/// <summary>Forwards the interface to the inherited method that claims its name.</summary>
public sealed class DispEffect : DispOwner, IDisposable
{
    void IDisposable.Dispose() => Dispose();
}

public static class DisposableProbes
{
    public static string NestedAndMultiDeclaratorOrder()
    {
        var log = new List<string>();

        using (
            DispLog a = new DispLog(log, "a"),
                b = new DispLog(log, "b")
        )
        {
            using var c = new DispLog(log, "c");
            using var d = new DispLog(log, "d");
            log.Add("body");
        }

        log.Add("after");

        return string.Join(",", log);
    }

    public static string NullResourceIsSkipped()
    {
        var log = new List<string>();
        DispLog? missing = null;

        using (missing)
        {
            log.Add("body");
        }

        using (var also = Nothing())
        {
            log.Add("body2");
        }

        return string.Join(",", log);
    }

    public static string BodyThrowsDisposesAndPropagates()
    {
        var log = new List<string>();

        try
        {
            using var r = new DispLog(log, "r");
            log.Add("body");

            throw new InvalidOperationException("body failed");
        }
        catch (InvalidOperationException e)
        {
            log.Add("caught " + e.Message);
        }

        return string.Join(",", log);
    }

    public static string DisposeThrowsReplacesThePendingException()
    {
        var log = new List<string>();

        try
        {
            using var r = new DispLog(log, "r", throws: true);

            throw new InvalidOperationException("body failed");
        }
        catch (InvalidOperationException e)
        {
            log.Add("caught " + e.Message);
        }

        return string.Join(",", log);
    }

    public static string LaterInitializerThrowsDisposesTheEarlierOnes()
    {
        var log = new List<string>();

        try
        {
            using DispLog a = new DispLog(log, "a"),
                b = Fail(log);
            log.Add("body");
        }
        catch (InvalidOperationException e)
        {
            log.Add("caught " + e.Message);
        }

        return string.Join(",", log);
    }

    public static string ReturnBreakAndContinueDispose()
    {
        var log = new List<string>();

        log.Add("returned " + ReturnInside(log));

        for (var i = 0; i < 3; i++)
        {
            using var r = new DispLog(log, "loop" + i);

            if (i == 0)
                continue;

            if (i == 1)
                break;
        }

        return string.Join(",", log);
    }

    public static string IteratorAbandonedEarlyDisposes()
    {
        var log = new List<string>();

        foreach (var value in Generate(log))
        {
            log.Add("got " + value);

            break;
        }

        log.Add("after");

        return string.Join(",", log);
    }

    public static string IsIDisposable()
    {
        object implementer = new DispBase();
        object derived = new DispDerived();
        object explicitOnly = new DispExplicit();
        object owner = new DispOwner();
        object effect = new DispEffect();
        object? nothing = null;
        object text = "s";

        return string.Join(
            ",",
            implementer is IDisposable,
            derived is IDisposable,
            explicitOnly is IDisposable,
            owner is IDisposable,
            effect is IDisposable,
            nothing is IDisposable,
            text is IDisposable
        );
    }

    public static string AsAndInterfaceCalls()
    {
        object explicitOnly = new DispExplicit();
        var asDisposable = explicitOnly as IDisposable;
        asDisposable?.Dispose();
        asDisposable?.Dispose();

        var effect = new DispEffect();
        IDisposable viaInterface = effect;
        viaInterface.Dispose();

        object owner = new DispOwner();
        var notDisposable = owner as IDisposable;

        return string.Join(
            ",",
            ((DispExplicit)explicitOnly).Count,
            effect.Count,
            notDisposable is null
        );
    }

    private static DispLog? Nothing() => null;

    private static DispLog Fail(List<string> log)
    {
        log.Add("fail");

        throw new InvalidOperationException("init failed");
    }

    private static int ReturnInside(List<string> log)
    {
        using var r = new DispLog(log, "ret");

        return 7;
    }

    private static IEnumerable<int> Generate(List<string> log)
    {
        using var r = new DispLog(log, "gen");

        yield return 1;
        yield return 2;
    }
}
