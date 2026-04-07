namespace MetaSharp.Tests;

public class InterfaceTranspileTests
{
    [Test]
    public async Task Interface_GeneratesTsInterface_KeepsCSharpName()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public interface IShape
            {
                double Area { get; }
                string Name { get; }
            }
            """
        );

        // Without [Name], keeps the C# name including I prefix
        var output = result["i-shape.ts"];
        await Assert.That(output).Contains("export interface IShape");
        await Assert.That(output).Contains("readonly area: number;");
        await Assert.That(output).Contains("readonly name: string;");
    }

    [Test]
    public async Task Interface_WithNameAttribute_UsesCustomName()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile, Name("Readable")]
            public interface IReadable
            {
                string Content { get; }
            }
            """
        );

        // [Name("Readable")] overrides → file is Readable.ts
        await Assert.That(result).ContainsKey("readable.ts");
        await Assert.That(result).DoesNotContainKey("i-readable.ts");
        await Assert.That(result["readable.ts"]).Contains("export interface Readable");
    }

    [Test]
    public async Task Record_ImplementsInterface_KeepsCSharpName()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App
            {
                [Transpile]
                public interface IEntity
                {
                    string Id { get; }
                }

                [Transpile]
                public record User(string Id, string Name) : IEntity;
            }
            """
        );

        var userTs = result["user.ts"];
        await Assert.That(userTs).Contains("implements IEntity");
        await Assert.That(userTs).Contains("import type { IEntity } from \"./i-entity\"");
    }

    [Test]
    public async Task Record_ImplementsInterface_WithNameOverride()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App
            {
                [Transpile, Name("Entity")]
                public interface IEntity
                {
                    string Id { get; }
                }

                [Transpile]
                public record User(string Id, string Name) : IEntity;
            }
            """
        );

        var userTs = result["user.ts"];
        await Assert.That(userTs).Contains("implements Entity");
        await Assert.That(userTs).Contains("import type { Entity } from \"./entity\"");
    }

    [Test]
    public async Task Record_ImplementsMultipleInterfaces()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App
            {
                [Transpile]
                public interface INamed { string Name { get; } }

                [Transpile]
                public interface ITagged { string Tag { get; } }

                [Transpile]
                public record Item(string Name, string Tag, int Price) : INamed, ITagged;
            }
            """
        );

        var itemTs = result["item.ts"];
        await Assert.That(itemTs).Contains("implements INamed, ITagged");
    }

    [Test]
    public async Task Record_ExtendsAndImplements()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App
            {
                [Transpile]
                public interface ILabeled { string Label { get; } }

                [Transpile]
                public record Base(int Id);

                [Transpile]
                public record Child(int Id, string Label) : Base(Id), ILabeled;
            }
            """
        );

        var childTs = result["child.ts"];
        await Assert.That(childTs).Contains("extends Base");
        await Assert.That(childTs).Contains("implements ILabeled");
    }

    [Test]
    public async Task NonTranspiledInterface_NotImplemented()
    {
        var result = TranspileHelper.Transpile(
            """
            public interface IHidden { string X { get; } }

            [Transpile]
            public record Visible(string X) : IHidden;
            """
        );

        var output = result["visible.ts"];
        await Assert.That(output).DoesNotContain("implements");
    }
}
