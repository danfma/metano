namespace MetaSharp.Tests;

public class AssignmentTests
{
    [Test]
    public async Task SimpleAssignment_GeneratesCorrectly()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Counter
            {
                private int _count;

                public void Increment()
                {
                    _count = _count + 1;
                }
            }
            """
        );

        var output = result["counter.ts"];
        await Assert.That(output).DoesNotContain("unsupported");
        await Assert.That(output).Contains("this._count = this._count + 1");
    }

    [Test]
    public async Task PropertyAssignment_GeneratesCorrectly()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Item
            {
                public string Name { get; private set; }

                public Item(string name) { Name = name; }

                public void Rename(string newName)
                {
                    Name = newName;
                }
            }
            """
        );

        var output = result["item.ts"];
        await Assert.That(output).DoesNotContain("unsupported");
        await Assert.That(output).Contains("this.name = newName");
    }

    [Test]
    public async Task CompoundAssignment_GeneratesCorrectly()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Accumulator
            {
                private int _total;

                public void Add(int value)
                {
                    _total += value;
                }
            }
            """
        );

        var output = result["accumulator.ts"];
        await Assert.That(output).DoesNotContain("unsupported");
        await Assert.That(output).Contains("this._total += value");
    }
}
