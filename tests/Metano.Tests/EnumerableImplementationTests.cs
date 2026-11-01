namespace Metano.Tests;

/// <summary>
/// A transpiled class implementing <c>IEnumerable&lt;T&gt;</c> is declared
/// <c>implements Iterable&lt;T&gt;</c>, so it carries a <c>[Symbol.iterator]()</c> member
/// delegating to its <c>getEnumerator()</c> — the member <c>for…of</c>, spread and the
/// LINQ runtime call. <c>GetEnumerator()</c> on a BCL sequence lowers to that
/// sequence's own iterator, and <c>IEnumerator&lt;T&gt;</c> maps to
/// <c>Iterator&lt;T&gt;</c>.
/// </summary>
public class EnumerableImplementationTests
{
    [Test]
    public async Task WrapperOverAList_MatchesTheGoldenOutput()
    {
        // Both GetEnumerator implementations are explicit: the generic one keeps the
        // name, the non-generic bridge is dropped.
        var result = TranspileHelper.Transpile(
            """
            using System.Collections;
            using System.Collections.Generic;

            [Transpile]
            public interface IBag<T> : IEnumerable<T>
            {
                int Size { get; }
            }

            [Transpile]
            public class Bag<T> : IBag<T>
            {
                private readonly List<T> _items = [];

                public void Add(T item) => _items.Add(item);

                public int Size => _items.Count;

                IEnumerator<T> IEnumerable<T>.GetEnumerator() => _items.GetEnumerator();

                IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable<T>)this).GetEnumerator();
            }
            """
        );

        await Assert
            .That(result["bag.ts"])
            .IsEqualTo(TranspileHelper.ReadExpected("enumerable-implementation.ts"));
        await Assert.That(result["i-bag.ts"]).DoesNotContain("Symbol.iterator");
    }

    [Test]
    public async Task IteratorMethod_IsReachedThroughSymbolIterator()
    {
        var result = TranspileHelper.Transpile(
            """
            using System.Collections;
            using System.Collections.Generic;

            [Transpile]
            public class Countdown : IEnumerable<int>
            {
                public virtual IEnumerator<int> GetEnumerator()
                {
                    yield return 2;
                    yield return 1;
                }

                IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
            }

            [Transpile]
            public class Liftoff : Countdown
            {
                public override IEnumerator<int> GetEnumerator()
                {
                    yield return 0;
                }
            }
            """
        );

        var countdown = result["countdown.ts"];
        await Assert.That(countdown).Contains("export class Countdown implements Iterable<number>");
        await Assert.That(countdown).Contains("*getEnumerator(): Generator<number> {");
        await Assert
            .That(countdown)
            .Contains(
                """
                  [Symbol.iterator](): Iterator<number> {
                    return this.getEnumerator();
                  }
                """
            );
        // The derived class inherits the base member, which calls its override.
        await Assert.That(result["liftoff.ts"]).DoesNotContain("Symbol.iterator");
    }

    [Test]
    public async Task NonGenericEnumerable_IteratesUnknown()
    {
        var result = TranspileHelper.Transpile(
            """
            using System.Collections;

            [Transpile]
            public class Legacy : IEnumerable
            {
                public IEnumerator GetEnumerator()
                {
                    yield return "x";
                }
            }
            """
        );

        var output = result["legacy.ts"];
        await Assert.That(output).Contains("*getEnumerator(): Generator<unknown> {");
        await Assert.That(output).Contains("[Symbol.iterator](): Iterator<unknown> {");
    }

    [Test]
    public async Task StructAndDictionaryWrapper_GetTheIteratorMember()
    {
        var result = TranspileHelper.Transpile(
            """
            using System.Collections;
            using System.Collections.Generic;

            [Transpile]
            public struct Pair : IEnumerable<int>
            {
                public int A;
                public int B;

                public IEnumerator<int> GetEnumerator()
                {
                    yield return A;
                    yield return B;
                }

                IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
            }

            [Transpile]
            public class Scores : IEnumerable<KeyValuePair<string, int>>
            {
                private readonly Dictionary<string, int> _map = new();

                public IEnumerator<KeyValuePair<string, int>> GetEnumerator() => _map.GetEnumerator();

                IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
            }
            """
        );

        await Assert.That(result["pair.ts"]).Contains("[Symbol.iterator](): Iterator<number> {");

        var scores = result["scores.ts"];
        await Assert.That(scores).Contains("getEnumerator(): Iterator<[string, number]> {");
        await Assert.That(scores).Contains("return this._map[Symbol.iterator]();");
        await Assert.That(scores).Contains("[Symbol.iterator](): Iterator<[string, number]> {");
    }

    [Test]
    public async Task GetEnumeratorOnBclSequences_LowersToTheirIterator()
    {
        var result = TranspileHelper.Transpile(
            """
            using System.Collections.Generic;

            [Transpile]
            public class Cursors
            {
                public IEnumerator<int> OfSequence(IEnumerable<int> xs) => xs.GetEnumerator();

                public IEnumerator<int> OfList(IList<int> xs) => xs.GetEnumerator();

                public IEnumerator<int> OfSet(HashSet<int> xs) => xs.GetEnumerator();
            }
            """
        );

        var output = result["cursors.ts"];
        await Assert.That(output).Contains("ofSequence(xs: Iterable<number>): Iterator<number> {");
        await Assert.That(output).Contains("return xs[Symbol.iterator]();");
        await Assert.That(output).DoesNotContain("getEnumerator()");
    }

    [Test]
    public async Task ArraysAndBclCollections_GetNoIteratorMember()
    {
        var result = TranspileHelper.Transpile(
            """
            using System.Collections.Generic;

            [Transpile]
            public class Holder
            {
                public List<int> Items { get; } = [];
                public int[] Values { get; } = [];
            }
            """
        );

        await Assert.That(result["holder.ts"]).DoesNotContain("Symbol.iterator");
    }
}
