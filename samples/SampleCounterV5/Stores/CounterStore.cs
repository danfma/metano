using Metano.TypeScript.SolidJs;
using SampleCounterV5.Models;

namespace SampleCounterV5.Stores;

public sealed class CounterStore
{
    private readonly System.Func<Counter> _get;
    private readonly ISignalSetter<Counter> _set;

    private CounterStore()
    {
        var (get, set) = Solid.CreateSignal(Counter.Zero);
        _get = get;
        _set = set;

        Solid.CreateEffect(() =>
        {
            Console.WriteLine($"Counter has changed: {State().Count}");
        });
    }

    public Counter State() => _get();

    public void Increment() => _set.Invoke(x => x.Increment());

    public void Decrement() => _set.Invoke(x => x.Decrement());

    public static CounterStore Create() => new();
}
