using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// Object and collection initializers (<c>new T(args) { X = x, Y = { Z = z } }</c>,
/// <c>new List&lt;int&gt; { 1, 2 }</c>, <c>new Dictionary&lt;K, V&gt; { [k] = v, { k, v } }</c>)
/// lower to the construction followed by each member assignment, index
/// assignment and <c>Add</c> call against the new instance, in C# order (#254).
/// They used to be dropped silently.
/// </summary>
public class ObjectInitializerTests
{
    [Test]
    public async Task Initializers_MatchTheGoldenOutput()
    {
        var result = TranspileHelper.Transpile(
            """
            using System.Collections;
            using System.Collections.Generic;

            [Transpile]
            public class Inner
            {
                public int Z { get; set; }
                public List<int> Items { get; } = new();
            }

            [Transpile]
            public class Point
            {
                public Point() { }

                public Point(int seed) { }

                public int X { get; set; }
                public int Y { get; init; }
                public Inner Inner { get; } = new();
            }

            [Transpile]
            public record Tag(string Name)
            {
                public int Weight { get; init; }
            }

            [Transpile]
            public class Bag : IEnumerable<string>
            {
                private readonly List<string> _items = new();

                public void Add(string item) => _items.Add(item);

                public void Add(string key, int times) => _items.Add(key + times);

                [Ignore]
                public IEnumerator<string> GetEnumerator() => _items.GetEnumerator();

                [Ignore]
                IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
            }

            [Transpile]
            public class Factory
            {
                private readonly List<int> _defaults = new() { 7, 8 };

                public int Seed { get; set; }

                public int Build(int capacity)
                {
                    var point = new Point(Seed) { X = Seed + 1, Y = 2, Inner = { Z = 3, Items = { 4, 5 } } };
                    var tag = new Tag("t") { Name = "u", Weight = 1 };
                    var list = new List<int> { 1, 2 };
                    var sized = new List<int>(capacity) { 3 };
                    List<string> typed = new() { "a" };
                    var byIndex = new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 };
                    var byAdd = new Dictionary<string, int> { { "c", 3 } };
                    var set = new HashSet<string> { "x", "y" };
                    var nested = new List<List<int>> { new() { 1 }, new List<int> { 2 } };
                    var bag = new Bag { "a", { "b", 2 } };
                    return point.X + tag.Weight + list.Count + sized.Count + typed.Count
                        + byIndex.Count + byAdd.Count + set.Count + nested.Count + _defaults.Count;
                }
            }
            """
        );

        await Assert
            .That(result["factory.ts"])
            .IsEqualTo(TranspileHelper.ReadExpected("object-initializers.ts"));
    }

    [Test]
    public async Task PlainObjectInitializer_FoldsIntoTheObjectLiteral()
    {
        var output = TranspilePlainObjectFactory(
            "public static Dto Make() => new Dto(1) { Label = \"l\", Score = 2 };"
        );

        await Assert.That(output).Contains("return { id: 1, label: \"l\", score: 2 };");
    }

    [Test]
    public async Task PlainObjectInitializer_AssigningACtorParameter_LowersToAssignments()
    {
        var output = TranspilePlainObjectFactory(
            "public static Dto Make() => new Dto(1) { Id = 5 };"
        );

        await Assert.That(output).Contains("Object.assign(receiver$temp$0, { id: 5 });");
        await Assert.That(output).Contains("})({ id: 1 });");
    }

    [Test]
    public async Task NestedIndexInitializer_EvaluatesTheKeyOnce()
    {
        var result = TranspileHelper.Transpile(
            """
            using System.Collections.Generic;

            [Transpile]
            public class Cell
            {
                public int Value { get; set; }
            }

            [Transpile]
            public class Grid
            {
                private readonly Dictionary<int, Cell> _cells = new();

                public Dictionary<int, Cell> Cells => _cells;

                public int Next() => _cells.Count;

                public Grid Fill() => new Grid { Cells = { [Next()] = { Value = 1 } } };
            }
            """
        );

        var output = result["grid.ts"];

        await Assert.That(output).Contains("})(this.next());");
        await Assert.That(output).Contains(".cells.get(receiver$temp$1).value = 1;");
    }

    [Test]
    public async Task NestedIndexInitializer_ReadsAVariableKeyBeforeTheNestedWrites()
    {
        var result = TranspileHelper.Transpile(
            """
            using System.Collections.Generic;

            [Transpile]
            public class Cell
            {
                public int V { get; set; }
                public int W { get; set; }
            }

            [Transpile]
            public static class Cells
            {
                public static Dictionary<int, Cell> Make()
                {
                    var i = 0;
                    return new Dictionary<int, Cell> { [i] = { V = i++, W = 0 } };
                }
            }
            """
        );

        var output = result["cells.ts"];

        await Assert.That(output).Contains(".get(receiver$temp$1).v = i++;");
        await Assert.That(output).Contains(".get(receiver$temp$1).w = 0;");
        await Assert.That(output).Contains("})(i);");
    }

    [Test]
    public async Task MultiIndexInitializer_ReportsUnsupportedFeature()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
            public class Matrix
            {
                public int this[int row, int column]
                {
                    get => 0;
                    set { }
                }

                public static Matrix Identity() => new Matrix { [0, 0] = 1 };
            }
            """
        );

        var unsupported = diagnostics
            .Where(d => d.Code == DiagnosticCodes.UnsupportedFeature)
            .ToList();

        await Assert.That(unsupported).HasSingleItem();
        await Assert.That(unsupported[0].Message).Contains("more than one index");
    }

    [Test]
    public async Task AwaitInsideInitializer_MakesTheBindingAsync()
    {
        var result = TranspileHelper.Transpile(
            """
            using System.Threading.Tasks;

            [Transpile]
            public class Box
            {
                public int Value { get; set; }
            }

            [Transpile]
            public static class Factory
            {
                public static async Task<Box> Load(Task<int> value) => new Box { Value = await value };
            }
            """
        );

        await Assert
            .That(result["factory.ts"])
            .Contains(
                "return await (async (receiver$temp$0) => {\n"
                    + "      receiver$temp$0.value = await value;"
            );
    }

    [Test]
    public async Task ExtensionAdd_ReportsUnsupportedFeature()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using System.Collections;
            using System.Collections.Generic;

            [Transpile]
            public class Names : IEnumerable<string>
            {
                [Ignore]
                public IEnumerator<string> GetEnumerator() => new List<string>().GetEnumerator();

                [Ignore]
                IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
            }

            public static class NamesExtensions
            {
                public static void Add(this Names names, string name) { }
            }

            [Transpile]
            public static class Factory
            {
                public static Names Build() => new Names { "a" };
            }
            """
        );

        var unsupported = diagnostics
            .Where(d => d.Code == DiagnosticCodes.UnsupportedFeature)
            .ToList();

        await Assert.That(unsupported).HasSingleItem();
        await Assert
            .That(unsupported[0].Message)
            .Contains("does not bind to an instance 'Add' method");
    }

    [Test]
    public async Task ParamsAdd_SpreadsAnArrayPassedInNormalForm()
    {
        var output = TranspileWithEnumerableBag(
            "public void Add(params int[] values) { }",
            "public static Bag Make(int[] values) => new Bag { values, { 1, 2 } };"
        );

        await Assert.That(output).Contains("receiver$temp$0.add(...values);");
        await Assert.That(output).Contains("receiver$temp$0.add(1, 2);");
    }

    [Test]
    public async Task EmitAdd_ReportsUnsupportedFeature()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            EnumerableBagSource(
                "[Emit(\"$0.pushIt($1)\")] public void Add(int value) { }",
                "public static Bag Make() => new Bag { 1 };"
            )
        );

        await AssertSingleUnsupported(diagnostics, "does not bind to an instance 'Add' method");
    }

    [Test]
    public async Task ExtensionPropertyInitializer_WritesThroughTheSetterHelper()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Box
            {
                public int Value { get; set; }

                public static Box Make() => new Box { Twice = 4 };
            }

            [Transpile]
            public static class BoxExtensions
            {
                extension(Box box)
                {
                    public int Twice
                    {
                        get => box.Value * 2;
                        set => box.Value = value / 2;
                    }
                }
            }
            """
        );

        await Assert.That(result["box.ts"]).Contains("twice$set(receiver$temp$0, 4);");
    }

    [Test]
    public async Task CustomInitAccessor_ReportsUnsupportedFeature()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
            public class Doubled
            {
                private int _value;

                public int Value
                {
                    get => _value;
                    init => _value = value * 2;
                }

                public static Doubled Make() => new Doubled { Value = 3 };
            }
            """
        );

        await AssertSingleUnsupported(diagnostics, "a custom init accessor");
    }

    [Test]
    public async Task EmptyInitializer_EmitsTheBareConstruction()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Box
            {
                public static Box Make() => new Box { };
            }
            """
        );

        await Assert.That(result["box.ts"]).Contains("return new Box();");
    }

    [Test]
    public async Task DartBackend_LowersInitializersToAClosure()
    {
        var (files, _) = TranspileHelper.TranspileDart(
            """
            using System.Collections.Generic;
            using Metano.Annotations;

            [Transpile]
            public class Box
            {
                public int Value { get; set; }

                public static Box Make() => new Box { Value = 1 };

                public static List<int> Numbers() => new List<int> { 1, 2 };
            }
            """
        );

        var output = files.Values.Single();

        await Assert
            .That(output)
            .Contains(
                "((receiver$temp$0) { receiver$temp$0.value = 1; return receiver$temp$0; })(Box())"
            );
        await Assert
            .That(output)
            .Contains("receiver$temp$0.add(1); receiver$temp$0.add(2); return receiver$temp$0; }");
    }

    private static string TranspilePlainObjectFactory(string member) =>
        TranspileHelper.Transpile(
            $$"""
            [PlainObject]
            [Transpile]
            public record Dto(int Id)
            {
                public string Label { get; init; } = "";
                public int Score { get; set; }
            }

            [Transpile]
            public static class Factory
            {
                {{member}}
            }
            """
        )["factory.ts"];

    private static async Task AssertSingleUnsupported(
        IReadOnlyList<MetanoDiagnostic> diagnostics,
        string messageFragment
    )
    {
        var unsupported = diagnostics
            .Where(d => d.Code == DiagnosticCodes.UnsupportedFeature)
            .ToList();

        await Assert.That(unsupported).HasSingleItem();
        await Assert.That(unsupported[0].Message).Contains(messageFragment);
    }

    private static string TranspileWithEnumerableBag(string addMethod, string factory) =>
        TranspileHelper.Transpile(EnumerableBagSource(addMethod, factory))["bag.ts"];

    private static string EnumerableBagSource(string addMethod, string factory) =>
        $$"""
            using System.Collections;
            using System.Collections.Generic;

            [Transpile]
            public class Bag : IEnumerable<int>
            {
                {{addMethod}}

                {{factory}}

                [Ignore]
                public IEnumerator<int> GetEnumerator() => new List<int>().GetEnumerator();

                [Ignore]
                IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
            }
            """;
}
