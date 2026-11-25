namespace Conformance;

/// <summary>Discard assignments evaluate their expression and drop the value (#773).</summary>
public static class DiscardProbes
{
    private sealed class Counter
    {
        public int Reads;

        public int Value => ++Reads;
    }

    private static void Run(Action action) => action();

    private static bool Next(Counter counter, out int value)
    {
        value = counter.Value;

        return value > 0;
    }

    public static string DiscardEvaluatesOnce()
    {
        var counter = new Counter();

        _ = counter.Value;
        Run(() => _ = counter.Value);
        Next(counter, out _);
        var sum = (_ = counter.Value) + 10;

        return $"{counter.Reads} {sum}";
    }

    public static string LocalNamedUnderscore()
    {
        var _ = 4;
        _ = _ * 2;

        return $"{_}";
    }
}
