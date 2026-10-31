namespace Metano.Tests;

/// <summary>
/// <c>new</c> of the BCL collection, pair and tuple types lowers through the
/// declarative constructor mappings in <c>Metano/Runtime</c>
/// (<c>[MapMethod(typeof(T), ".ctor")]</c>) instead of the old
/// <c>new Object(...)</c> fallback (#254). Overloads that share an arity are
/// told apart by <c>WhenParameterTypes</c>.
/// </summary>
public class CollectionConstructionTests
{
    [Test]
    public async Task CollectionConstructions_MatchTheGoldenOutput()
    {
        var result = TranspileHelper.Transpile(
            """
            using System;
            using System.Collections.Generic;

            [Transpile]
            public record Tag(string Name);

            [Transpile]
            public class Shelf
            {
                private readonly List<int> _items = new();
                private readonly HashSet<Tag> _tags = new(4);
                private readonly Dictionary<string, Tag?> _byName = new();

                public int Size(List<int> source, int capacity)
                {
                    var empty = new List<string?>();
                    var sized = new List<int>(capacity);
                    var copy = new List<int>(source);
                    var map = new Dictionary<string, int>(capacity);
                    var mapCopy = new Dictionary<string, int>(map);
                    var set = new HashSet<Tag>(_tags);
                    var pair = new KeyValuePair<string, int>("k", 1);
                    var fromPairs = new Dictionary<string, int>(new List<KeyValuePair<string, int>>());
                    var tuple = new Tuple<int, string>(1, "s");
                    return empty.Count + sized.Count + copy.Count + map.Count + mapCopy.Count
                        + set.Count + pair.Value + pair.Key.Length + fromPairs.Count
                        + tuple.Item1 + tuple.Item2.Length + _items.Count + _byName.Count;
                }
            }
            """
        );

        await Assert
            .That(result["shelf.ts"])
            .IsEqualTo(TranspileHelper.ReadExpected("collection-construction.ts"));
    }

    [Test]
    public async Task MappedConstructions_NeverFallBackToTheObjectConstructor()
    {
        var result = TranspileHelper.Transpile(
            """
            using System;
            using System.Collections.Generic;

            [Transpile]
            public class Shelf
            {
                public object Build() => new List<int>();
                public object Pair() => new KeyValuePair<int, int>(1, 2);
                public object Tuple() => new Tuple<int>(1);
            }
            """
        );

        var output = result["shelf.ts"];

        await Assert.That(output).DoesNotContain("new Object(");
        await Assert.That(output).Contains("return [] as Array<number>;");
        await Assert.That(output).Contains("return [1, 2] as [number, number];");
        await Assert.That(output).Contains("return [1] as [number];");
    }

    [Test]
    public async Task ParameterlessKeyValuePair_DoesNotPrintUnsuppliedPlaceholders()
    {
        var result = TranspileHelper.Transpile(
            """
            using System.Collections.Generic;

            [Transpile]
            public class Shelf
            {
                public KeyValuePair<string, int> Empty() => new KeyValuePair<string, int>();
            }
            """
        );

        await Assert.That(result["shelf.ts"]).DoesNotContain("$0");
    }

    [Test]
    public async Task DroppedSideEffect_BindsAnEarlierReadBeforeTheEffect()
    {
        var result = TranspileHelper.Transpile(
            """
            using Metano.Annotations;

            [assembly: MapMethod(
                typeof(Widget),
                MapMethodAttribute.ConstructorName,
                JsTemplate = "makeWidget($0)"
            )]

            public class Widget
            {
                public Widget(int keep, int drop) { }
            }

            [Transpile]
            public class Factory
            {
                private int _n;

                private int Next() => ++_n;

                public Widget Build() => new Widget(_n, Next());
            }
            """
        );

        await Assert
            .That(result["factory.ts"])
            .Contains("return (($arg0, $arg1) => makeWidget($arg0))(this._n, this.next());");
    }

    [Test]
    public async Task DroppedSideEffect_KeepsSourceOrderWithKeptArguments()
    {
        var result = TranspileHelper.Transpile(
            """
            using Metano.Annotations;

            [assembly: MapMethod(
                typeof(Widget),
                MapMethodAttribute.ConstructorName,
                JsTemplate = "makeWidget($0)"
            )]

            public class Widget
            {
                public Widget(int keep, int drop) { }
            }

            [Transpile]
            public class Factory
            {
                private int _next;

                private int Next() => ++_next;

                public Widget Build() => new Widget(Next(), Next() * 10);
            }
            """
        );

        await Assert
            .That(result["factory.ts"])
            .Contains(
                "return (($arg0, $arg1) => makeWidget($arg0))(this.next(), this.next() * 10);"
            );
    }

    [Test]
    public async Task SideEffectingCapacity_IsStillEvaluatedOnce()
    {
        var result = TranspileHelper.Transpile(
            """
            using System.Collections.Generic;

            [Transpile]
            public class Shelf
            {
                private int _calls;

                private int NextCapacity() => ++_calls;

                public List<int> Build() => new List<int>(NextCapacity());

                public List<int> BuildPure(int capacity) => new List<int>(capacity);
            }
            """
        );

        var output = result["shelf.ts"];

        await Assert
            .That(output)
            .Contains("return (($arg0) => [] as Array<number>)(this.nextCapacity());");
        await Assert.That(output).Contains("return [] as Array<number>;");
    }

    [Test]
    public async Task HashSetConstructedOnlyInABody_ImportsTheRuntimeSet()
    {
        var result = TranspileHelper.Transpile(
            """
            using System.Collections.Generic;

            [Transpile]
            public class Shelf
            {
                public int Distinct(List<int> values)
                {
                    var seen = new HashSet<int>(values);
                    return seen.Count;
                }
            }
            """
        );

        var output = result["shelf.ts"];

        await Assert.That(output).Contains("import { HashSet } from \"metano-runtime\";");
        await Assert.That(output).Contains("const seen = new HashSet<number>(values);");
    }

    [Test]
    public async Task HashSetInSignatureAndBody_ImportsTheRuntimeSetOnce()
    {
        var result = TranspileHelper.Transpile(
            """
            using System.Collections.Generic;

            [Transpile]
            public class Shelf
            {
                public HashSet<int> Copy(HashSet<int> values) => new HashSet<int>(values);
            }
            """
        );

        var output = result["shelf.ts"];
        var importCount = output
            .Split('\n')
            .Where(line => line.StartsWith("import "))
            .Sum(line => line.Split("HashSet").Length - 1);
        await Assert.That(importCount).IsEqualTo(1);
    }

    [Test]
    public async Task KeyValuePairMembers_ReadTheTuplePositions()
    {
        var result = TranspileHelper.Transpile(
            """
            using System.Collections.Generic;

            [Transpile]
            public class Shelf
            {
                public string Describe(Dictionary<string, int> map)
                {
                    var text = "";
                    foreach (var entry in map)
                        text += entry.Key + entry.Value;
                    return text;
                }
            }
            """
        );

        await Assert.That(result["shelf.ts"]).Contains("entry[0] + entry[1]");
    }

    [Test]
    public async Task WhenParameterTypes_SelectsTheMatchingMethodOverload()
    {
        var result = TranspileHelper.Transpile(
            """
            using System;
            using Metano.Annotations;

            [assembly: MapMethod(
                typeof(Probe),
                "Take",
                JsTemplate = "takeNumber($0)",
                WhenParameterTypes = new[] { typeof(int) }
            )]
            [assembly: MapMethod(
                typeof(Probe),
                "Take",
                JsTemplate = "takeText($0)",
                WhenParameterTypes = new[] { typeof(string) }
            )]

            // Not transpiled: no [Transpile] attribute.
            public static class Probe
            {
                public static int Take(int value) => value;
                public static int Take(string value) => value.Length;
            }

            [Transpile]
            public class Caller
            {
                public int Both() => Probe.Take(1) + Probe.Take("a");
            }
            """
        );

        var output = result["caller.ts"];
        await Assert.That(output).Contains("takeNumber(1) + takeText(\"a\")");
    }
}
