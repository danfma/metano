namespace Metano.Tests;

/// <summary>
/// A record's <c>hashCode</c> adds a tuple member element by element (#403):
/// the runtime cannot tell a tuple from a C# array (an array member hashes by
/// reference, as its equality compares it), so only the compiler, which knows
/// the static type, spreads the tuple. An array member keeps one
/// <c>hc.add</c>.
/// </summary>
public class RecordTupleHashTests
{
    [Test]
    public async Task TupleMember_AddsEachElement_ArrayMemberAddsItself()
    {
        var output = TranspileHelper.Transpile(
            """
            [Transpile]
            public record Shape(((int, int), string) Pair, int[] Items);
            """
        )["shape.ts"];

        await Assert.That(output).Contains("hc.add(this.pair[0][0]);");
        await Assert.That(output).Contains("hc.add(this.pair[0][1]);");
        await Assert.That(output).Contains("hc.add(this.pair[1]);");
        await Assert.That(output).Contains("hc.add(this.items);");
    }
}
