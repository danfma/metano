namespace MetaSharp.Tests;

public class NestedTypesTests
{
    [Test]
    public async Task NestedClass_GeneratesCompanionNamespace()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Outer
            {
                public string Name { get; }
                public Outer(string name) { Name = name; }

                [Transpile]
                public class Inner
                {
                    public int Value { get; }
                    public Inner(int value) { Value = value; }
                }
            }
            """
        );

        var output = result["outer.ts"];
        await Assert.That(output).Contains("export class Outer");
        await Assert.That(output).Contains("export namespace Outer");
        await Assert.That(output).Contains("export class Inner");
    }

    [Test]
    public async Task NestedEnum_InsideClass()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Container
            {
                public string Name { get; }
                public Container(string name) { Name = name; }

                [Transpile, StringEnum]
                public enum Status { Active, Inactive }
            }
            """
        );

        var output = result["container.ts"];
        await Assert.That(output).Contains("export class Container");
        await Assert.That(output).Contains("export namespace Container");
        await Assert.That(output).Contains("Status");
    }

    [Test]
    public async Task NestedTypeReferenced_FromOutside()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Outer
            {
                public Outer() { }

                [Transpile]
                public class Inner
                {
                    public int Value { get; }
                    public Inner(int value) { Value = value; }
                }
            }

            [Transpile]
            public class Consumer
            {
                public Outer.Inner Make() => new Outer.Inner(42);
            }
            """
        );

        // Outer.Inner accessible via declaration merging
        var consumerOutput = result["consumer.ts"];
        await Assert.That(consumerOutput).Contains("Outer.Inner");
        await Assert.That(consumerOutput).Contains("import { Outer }");
    }

    [Test]
    public async Task NoNestedTypes_NoCompanionNamespace()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Plain
            {
                public string Name { get; }
                public Plain(string name) { Name = name; }
            }
            """
        );

        var output = result["plain.ts"];
        await Assert.That(output).DoesNotContain("namespace Plain");
    }
}
