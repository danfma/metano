using Metano.Annotations;

namespace Conformance;

public sealed class RnBox<T>(T value)
{
    public T Value { get; } = value;
}

[Name("RnPair")]
public sealed class RnBox<T1, T2>(T1 first, T2 second)
{
    public T1 First { get; } = first;

    public T2 Second { get; } = second;
}

public sealed class RnSignal<T>(T value)
{
    public T Value { get; } = value;
}

[Name("RnSignals")]
public static class RnSignal
{
    public static RnSignal<T> Create<T>(T value) => new(value);
}

public static class RenamedSameNameProbes
{
    public static string renamedArity()
    {
        var one = new RnBox<int>(1);
        var two = new RnBox<int, string>(2, "b");

        return $"{one.Value},{two.First},{two.Second}";
    }

    public static string renamedFactory() => RnSignal.Create(42).Value.ToString();
}
