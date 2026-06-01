using Metano.TypeScript.SolidJs;
using SampleCounterV5.Models;

namespace SampleCounterV5.Stores;

public sealed class CounterStore
{
    private readonly ISignal<Counter> _counter;

    private CounterStore()
    {
        _counter = Solid.CreateSignal(Counter.Zero);

        Solid.CreateEffect(() =>
        {
            Console.WriteLine($"Counter has changed: {State().Count}");
        });
    }

    public Counter State() => _counter.Value;

    public void Increment() => _counter.Set(x => x.Increment());

    public void Decrement() => _counter.Set(x => x.Decrement());

    public static CounterStore Create() => new();
}
