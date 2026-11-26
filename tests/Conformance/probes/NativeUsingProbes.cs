using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Conformance;

/// <summary>A resource whose Dispose itself runs a using whose body and disposal both throw.</summary>
public sealed class DispNested : IDisposable
{
    private readonly List<string> _log;

    public DispNested(List<string> log)
    {
        _log = log;
    }

    public void Dispose()
    {
        using var inner = new DispLog(_log, "inner", throws: true);

        throw new ArgumentException("nested dispose");
    }
}

/// <summary>Counts its disposals.</summary>
public sealed class DispCounter : IDisposable
{
    public int Count;

    public void Dispose() => Count++;
}

/// <summary>A ref struct disposed through its pattern-based Dispose, which no interface reaches.</summary>
public ref struct DispLease
{
    private readonly List<string> _log;

    public DispLease(List<string> log)
    {
        _log = log;
        _log.Add("lease");
    }

    public void Dispose() => _log.Add("lease disposed");
}

/// <summary>
/// C# <c>using</c> emitted as a TypeScript <c>using</c> declaration (#817, FR-111): disposal
/// order, null resources, exceptions from the body and from <c>Dispose</c> (a
/// <c>SuppressedError</c> in JavaScript, unwrapped by every generated catch), jumps, loops,
/// iterators and async methods.
/// </summary>
public static class NativeUsingProbes
{
    public static string BothThrowTheSpecificCatchSeesTheDisposeException()
    {
        var log = new List<string>();

        try
        {
            try
            {
                using var r = new DispLog(log, "r", throws: true);

                throw new ArgumentException("body");
            }
            catch (ArgumentException e)
            {
                log.Add("wrong " + e.Message);
            }
        }
        catch (InvalidOperationException e)
        {
            log.Add("caught " + e.Message);
        }

        return string.Join(",", log);
    }

    public static string BothResourcesAndTheBodyThrowTheFirstResourceWins()
    {
        var log = new List<string>();

        try
        {
            using var a = new DispLog(log, "a", throws: true);
            using var b = new DispLog(log, "b", throws: true);

            throw new ArgumentException("body");
        }
        catch (InvalidOperationException e)
        {
            log.Add("caught " + e.Message);
        }
        catch (Exception e)
        {
            log.Add("other " + e.GetType().Name);
        }

        return string.Join(",", log);
    }

    public static string DisposeThrowsAfterANormalBody()
    {
        var log = new List<string>();

        try
        {
            using (new DispLog(log, "r", throws: true))
            {
                log.Add("body");
            }

            log.Add("unreached");
        }
        catch (InvalidOperationException e)
        {
            log.Add("caught " + e.Message);
        }

        return string.Join(",", log);
    }

    public static string ChainedSuppressionUnwrapsToTheInnermostDisposeException()
    {
        var log = new List<string>();

        try
        {
            using var r = new DispNested(log);

            throw new ArgumentException("body");
        }
        catch (InvalidOperationException e)
        {
            log.Add("caught " + e.Message);
        }
        catch (Exception e)
        {
            log.Add("other " + e.GetType().Name + " " + e.Message);
        }

        return string.Join(",", log);
    }

    public static string BodyExceptionWithAWhenFilter()
    {
        var log = new List<string>();

        try
        {
            using var r = new DispLog(log, "r", throws: true);

            throw new ArgumentException("body");
        }
        catch (Exception e) when (e is InvalidOperationException)
        {
            log.Add("filtered " + e.Message);
        }

        return string.Join(",", log);
    }

    public static string ReturnValueIsReadBeforeDisposal()
    {
        var counter = new DispCounter();
        var read = ReadThenDispose(counter);

        return read + "," + counter.Count;
    }

    public static string NullResourcesOfEveryForm()
    {
        var log = new List<string>();
        IDisposable? none = null;

        using (none)
        {
            log.Add("expression");
        }

        using (DispLog? declared = null)
        {
            log.Add("declared");
        }

        using var missing = Nothing();
        log.Add("declaration");

        return string.Join(",", log);
    }

    public static string InterfaceAndGenericResources()
    {
        var log = new List<string>();

        using (IDisposable viaInterface = new DispLog(log, "iface"))
        {
            log.Add("body");
        }

        DisposeGeneric(new DispLog(log, "generic"), log);

        return string.Join(",", log);
    }

    public static string LoopsDisposeEveryIteration()
    {
        var log = new List<string>();

        foreach (var name in new[] { "x", "y", "z" })
        {
            using var r = new DispLog(log, name);

            if (name == "y")
                continue;

            log.Add("body " + name);
        }

        var i = 0;

        while (true)
        {
            using var w = new DispLog(log, "w" + i);

            if (i++ == 1)
                break;
        }

        return string.Join(",", log);
    }

    public static string NestedLoopsWithAnEarlyReturn()
    {
        var log = new List<string>();

        log.Add("found " + FindFirst(log));

        return string.Join(",", log);
    }

    public static string IteratorFullyEnumeratedDisposesAfterTheLastYield()
    {
        var log = new List<string>();

        foreach (var value in Generate(log))
            log.Add("got " + value);

        log.Add("after");

        return string.Join(",", log);
    }

    public static string IteratorThatThrowsDisposesBeforeTheCatch()
    {
        var log = new List<string>();

        try
        {
            foreach (var value in GenerateThenFail(log))
                log.Add("got " + value);
        }
        catch (InvalidOperationException e)
        {
            log.Add("caught " + e.Message);
        }

        return string.Join(",", log);
    }

    public static string RefStructPatternDispose()
    {
        var log = new List<string>();

        {
            using var lease = new DispLease(log);
            log.Add("body");
        }

        log.Add("after");

        return string.Join(",", log);
    }

    public static async Task<string> AsyncDisposesAfterTheAwait()
    {
        var log = new List<string>();
        var result = await Work(log);

        log.Add("got " + result);

        return string.Join(",", log);
    }

    public static async Task<string> AsyncBothThrowTheSpecificCatchSeesTheDisposeException()
    {
        var log = new List<string>();

        try
        {
            await FailAfterAwait(log);
        }
        catch (InvalidOperationException e)
        {
            log.Add("caught " + e.Message);
        }
        catch (Exception e)
        {
            log.Add("other " + e.GetType().Name);
        }

        return string.Join(",", log);
    }

    private static DispLog? Nothing() => null;

    private static int ReadThenDispose(DispCounter counter)
    {
        using (counter)
        {
            return counter.Count;
        }
    }

    private static void DisposeGeneric<T>(T resource, List<string> log)
        where T : IDisposable
    {
        using (resource)
        {
            log.Add("generic body");
        }
    }

    private static string FindFirst(List<string> log)
    {
        using var outer = new DispLog(log, "outer");

        for (var i = 0; i < 2; i++)
        {
            using var row = new DispLog(log, "row" + i);

            for (var j = 0; j < 2; j++)
            {
                using var cell = new DispLog(log, "cell" + i + j);

                if (i == 1 && j == 0)
                    return "" + i + j;
            }
        }

        return "none";
    }

    private static IEnumerable<int> Generate(List<string> log)
    {
        using var r = new DispLog(log, "gen");
        log.Add("start");

        yield return 1;
        yield return 2;

        log.Add("end");
    }

    private static IEnumerable<int> GenerateThenFail(List<string> log)
    {
        using var r = new DispLog(log, "gen");

        yield return 1;

        throw new InvalidOperationException("generator failed");
    }

    private static async Task<int> Work(List<string> log)
    {
        using var r = new DispLog(log, "async");
        log.Add("before");

        var value = await Task.FromResult(5);

        log.Add("after await");

        return value;
    }

    private static async Task FailAfterAwait(List<string> log)
    {
        using var r = new DispLog(log, "r", throws: true);

        await Task.FromResult(0);

        throw new ArgumentException("body");
    }
}
