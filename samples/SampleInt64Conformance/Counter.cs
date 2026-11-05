using Metano.Annotations;

namespace SampleInt64Conformance;

/// <summary>
/// 64-bit increments and compound assignments on a field, a property and an
/// array element whose index has a side effect, which the read-modify-write
/// lowering must evaluate once (spec 012 FR-024). Emitted beside
/// <see cref="Overflow"/>, so the generated module has no relative import and
/// runs under Node's type stripping as it is.
/// </summary>
[EmitInFile("overflow")]
public class Counter
{
    private readonly long[] _slots;

    private long _field;

    private int _slotReads;

    public Counter(long seed)
    {
        _field = seed;
        _slots = new long[] { 0, seed };
        Total = seed;
    }

    public long Total { get; set; }

    public long IncrementField()
    {
        _field++;

        return _field;
    }

    public long AddToTotal(long amount)
    {
        Total += amount;

        return Total;
    }

    /// <summary>The product stored in the slot, or <c>-1</c> when the index was not read exactly once.</summary>
    public long MultiplySlot(long factor)
    {
        _slots[NextSlot()] *= factor;

        return _slotReads == 1 ? _slots[1] : -1;
    }

    /// <summary>The value before the increment, or <c>-1</c> when the index was not read exactly once.</summary>
    public long IncrementSlot()
    {
        var previous = _slots[NextSlot()]++;

        return _slotReads == 1 ? previous : -1;
    }

    private int NextSlot()
    {
        _slotReads++;

        return 1;
    }
}
