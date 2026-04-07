namespace MetaSharp.Tests;

public class FieldTranspileTests
{
    [Test]
    public async Task PrivateField_GeneratesPrivateFieldMember()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Counter
            {
                private int _count = 0;
            }
            """
        );

        var output = result["counter.ts"];
        await Assert.That(output).Contains("private");
        await Assert.That(output).Contains("count");
    }

    [Test]
    public async Task PrivateReadonlyField_GeneratesReadonly()
    {
        var result = TranspileHelper.Transpile(
            """
            using System.Collections.Generic;

            [Transpile]
            public class TodoList
            {
                private readonly List<int> _items = new();
            }
            """
        );

        var output = result["todo-list.ts"];
        await Assert.That(output).Contains("private");
        await Assert.That(output).Contains("readonly");
        await Assert.That(output).Contains("items");
    }

    [Test]
    public async Task PrivateField_WithInitializer()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class State
            {
                private string _status = "idle";
            }
            """
        );

        var output = result["state.ts"];
        await Assert.That(output).Contains("private");
        await Assert.That(output).Contains("status");
        await Assert.That(output).Contains("\"idle\"");
    }

    [Test]
    public async Task ProtectedField_GeneratesProtected()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Base
            {
                protected int _value = 10;
            }
            """
        );

        var output = result["base.ts"];
        await Assert.That(output).Contains("protected");
        await Assert.That(output).Contains("value");
    }

    [Test]
    public async Task PublicField_NoAccessModifier()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Config
            {
                public int MaxRetries = 3;
            }
            """
        );

        var output = result["config.ts"];
        await Assert.That(output).Contains("maxRetries: number = 3");
        // public is default in TS, shouldn't be printed
        await Assert.That(output).DoesNotContain("public maxRetries");
    }

    [Test]
    public async Task BackingField_NotDuplicated()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public record Simple(int X);
            """
        );

        var output = result["simple.ts"];
        // Auto-property backing fields should NOT appear as separate fields
        // Only the constructor param should exist
        var fieldCount = output.Split("x:").Length - 1;
        await Assert.That(fieldCount).IsEqualTo(1);
    }
}
