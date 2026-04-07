namespace MetaSharp.Tests;

public class StaticReferenceTests
{
    [Test]
    public async Task EnumAccess_KeepsPascalCase()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile, StringEnum]
            public enum Status { Active, Inactive }

            [Transpile]
            public class Item
            {
                public Status GetDefault() { return Status.Active; }
            }
            """
        );

        var output = result["item.ts"];
        await Assert.That(output).Contains("Status.Active");
        await Assert.That(output).DoesNotContain("status.active");
    }

    [Test]
    public async Task StaticMethodCall_KeepsTypePascalCase()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Factory
            {
                public static Factory Create() { return new Factory(); }
            }

            [Transpile]
            public class Consumer
            {
                public Factory Get() { return Factory.Create(); }
            }
            """
        );

        var output = result["consumer.ts"];
        await Assert.That(output).Contains("Factory.create()");
        await Assert.That(output).DoesNotContain("factory.create()");
    }

    [Test]
    public async Task TypeIdentifier_NotCamelCased()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class MyService
            {
                public MyService Instance { get; }
                public MyService(MyService instance) { Instance = instance; }
            }
            """
        );

        var output = result["my-service.ts"];
        // Type references in constructor should stay PascalCase
        await Assert.That(output).Contains("MyService");
        await Assert.That(output).DoesNotContain("myService");
    }
}
