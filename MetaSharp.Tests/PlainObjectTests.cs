namespace MetaSharp.Tests;

/// <summary>
/// Tests for the <c>[PlainObject]</c> attribute. A decorated record emits as a TS
/// interface (no class wrapper, no methods); <c>new T(args)</c> lowers to an object
/// literal; <c>with</c> lowers to a spread literal. Imports are type-only since the
/// type is erased at runtime.
/// </summary>
public class PlainObjectTests
{
    [Test]
    public async Task DecoratedRecord_EmitsAsInterface()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile, PlainObject]
            public record CreateTodoDto(string Title, int Count);
            """);

        var output = result["create-todo-dto.ts"];
        await Assert.That(output).Contains("export interface CreateTodoDto");
        await Assert.That(output).Contains("readonly title: string");
        await Assert.That(output).Contains("readonly count: number");
        // No class wrapper, no methods
        await Assert.That(output).DoesNotContain("export class CreateTodoDto");
        await Assert.That(output).DoesNotContain("equals(");
        await Assert.That(output).DoesNotContain("hashCode(");
    }

    [Test]
    public async Task NewExpression_LowersToObjectLiteral()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile, PlainObject]
            public record CreateTodoDto(string Title, int Count);

            [Transpile]
            public class Factory
            {
                public CreateTodoDto Make() => new CreateTodoDto("Buy milk", 5);
            }
            """);

        var output = result["factory.ts"];
        // Object literal, not `new CreateTodoDto(...)`
        await Assert.That(output).Contains("{ title: \"Buy milk\", count: 5 }");
        await Assert.That(output).DoesNotContain("new CreateTodoDto");
    }

    [Test]
    public async Task NamedArguments_StillMatchPropertyKeys()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile, PlainObject]
            public record CreateTodoDto(string Title, int Count);

            [Transpile]
            public class Factory
            {
                public CreateTodoDto Make() => new CreateTodoDto(Count: 3, Title: "Eggs");
            }
            """);

        var output = result["factory.ts"];
        // Even when the user supplies named args in non-declaration order, the keys
        // should match the property names (TS will sort or not — this checks both
        // appear with the right values).
        await Assert.That(output).Contains("count: 3");
        await Assert.That(output).Contains("title: \"Eggs\"");
    }

    [Test]
    public async Task WithExpression_LowersToSpread()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile, PlainObject]
            public record TodoDto(string Title, bool Completed);

            [Transpile]
            public class Toggler
            {
                public TodoDto Toggle(TodoDto dto) => dto with { Completed = !dto.Completed };
            }
            """);

        var output = result["toggler.ts"];
        // Spread instead of .with({...}). The printer may emit on a single line or
        // wrap to multiple lines depending on length, so just match the spread token
        // and the property assignment separately.
        await Assert.That(output).Contains("...dto");
        await Assert.That(output).Contains("completed: !dto.completed");
        await Assert.That(output).DoesNotContain("dto.with(");
    }

    [Test]
    public async Task ReferencedFromAnotherFile_ImportsAsType()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile, PlainObject]
            public record TodoDto(string Title);

            [Transpile]
            public class Holder
            {
                public TodoDto? Current { get; set; }
            }
            """);

        var output = result["holder.ts"];
        // Should be `import type` since the value is never accessed at runtime
        await Assert.That(output).Contains("import type { TodoDto }");
    }

    [Test]
    public async Task PlainObject_NoEqualsHashCodeWithMethods()
    {
        // Sanity: a regular record has equals/hashCode/with; a [PlainObject] one does not.
        var result = TranspileHelper.Transpile(
            """
            [Transpile, PlainObject]
            public record Point(int X, int Y);
            """);

        var output = result["point.ts"];
        await Assert.That(output).DoesNotContain("equals");
        await Assert.That(output).DoesNotContain("hashCode");
        await Assert.That(output).DoesNotContain("with(");
        // Also no HashCode runtime import
        await Assert.That(output).DoesNotContain("HashCode");
    }
}
