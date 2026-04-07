namespace MetaSharp.Tests;

public class GenericTranspileTests
{
    [Test]
    public async Task GenericRecord_EmitsTypeParameter()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public record Result<T>(T Value, bool Success);
            """
        );

        var expected = TranspileHelper.ReadExpected("generic-record.ts");
        await Assert.That(result["result.ts"]).IsEqualTo(expected);
    }

    [Test]
    public async Task GenericRecord_MultipleTypeParams()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public record Pair<K, V>(K Key, V Value);
            """
        );

        var expected = TranspileHelper.ReadExpected("generic-multi-param.ts");
        await Assert.That(result["pair.ts"]).IsEqualTo(expected);
    }

    [Test]
    public async Task GenericRecord_WithConstraint()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App
            {
                [Transpile]
                public interface IEntity { string Id { get; } }

                [Transpile]
                public record Repo<T>(T Item) where T : IEntity;
            }
            """
        );

        var expected = TranspileHelper.ReadExpected("generic-constraint.ts");
        await Assert.That(result["repo.ts"]).IsEqualTo(expected);
    }

    [Test]
    public async Task GenericInterface_EmitsTypeParameter()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public interface IRepository<T>
            {
                System.Collections.Generic.IReadOnlyList<T> Items { get; }
            }
            """
        );

        var expected = TranspileHelper.ReadExpected("generic-interface.ts");
        await Assert.That(result["i-repository.ts"]).IsEqualTo(expected);
    }

    [Test]
    public async Task GenericRecord_Inheritance()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App
            {
                [Transpile]
                public record Result<T>(T Value, bool Success);

                [Transpile]
                public record Ok<T>(T Value) : Result<T>(Value, true);
            }
            """
        );

        var okTs = result["ok.ts"];
        await Assert.That(okTs).Contains("class Ok<T> extends Result<T>");
        await Assert.That(okTs).Contains("super(value, true)");
    }

    [Test]
    public async Task GenericMethod_EmitsTypeParameter()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile, ExportedAsModule]
            public static class Parser
            {
                public static T Identity<T>(T value) => value;
            }
            """
        );

        var output = result["parser.ts"];
        await Assert.That(output).Contains("function identity<T>(value: T): T");
    }

    [Test]
    public async Task GenericMethod_WithConstraint()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App
            {
                [Transpile]
                public interface IEntity { string Id { get; } }

                [Transpile, ExportedAsModule]
                public static class Finder
                {
                    public static T Find<T>(T[] items, string id) where T : IEntity
                    {
                        return items[0];
                    }
                }
            }
            """
        );

        var output = result["finder.ts"];
        await Assert.That(output).Contains("function find<T extends IEntity>(items: T[], id: string): T");
    }

    [Test]
    public async Task ConcreteGenericType_PreservesTypeArguments()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public record Wrapper(System.Collections.Generic.List<int> Numbers);
            """
        );

        var output = result["wrapper.ts"];
        // List<int> → number[] (already handled by TypeMapper)
        await Assert.That(output).Contains("numbers: number[]");
    }

    [Test]
    public async Task PartialInWith_IsStructural()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public record Box<T>(T Content);
            """
        );

        var output = result["box.ts"];
        // Partial<Box<T>> should be structural, not a string hack
        await Assert.That(output).Contains("Partial<Box<T>>");
        await Assert.That(output).Contains("Box<T>");
    }

    [Test]
    public async Task GenericRecord_ImplementsGenericInterface()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App
            {
                [Transpile]
                public interface IContainer<T> { T Value { get; } }

                [Transpile]
                public record Box<T>(T Value) : IContainer<T>;
            }
            """
        );

        var boxTs = result["box.ts"];
        await Assert.That(boxTs).Contains("implements IContainer<T>");
    }
}
