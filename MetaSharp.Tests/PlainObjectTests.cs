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
    public async Task DefaultValuedParam_BecomesOptionalField()
    {
        // Constructor params with defaults in C# become optional fields in the
        // emitted TS interface (`name?: Type`). The construction side already drops
        // omitted args from the literal, so the two halves agree.
        var result = TranspileHelper.Transpile(
            """
            [Transpile, PlainObject]
            public record Page(string Title, int PageNumber = 1, bool Draft = false);
            """);

        var output = result["page.ts"];
        await Assert.That(output).Contains("readonly title: string");
        await Assert.That(output).Contains("readonly pageNumber?: number");
        await Assert.That(output).Contains("readonly draft?: boolean");
    }

    [Test]
    public async Task OmittedDefaultArgs_NotInObjectLiteral()
    {
        // Construction with positional args that omit the optional ones produces a
        // literal without the omitted keys (the receiver reads `undefined`, which
        // is fine because the field is optional in the interface).
        var result = TranspileHelper.Transpile(
            """
            [Transpile, PlainObject]
            public record Page(string Title, int PageNumber = 1);

            [Transpile]
            public class Factory
            {
                public Page Make() => new Page("intro");
            }
            """);

        var output = result["factory.ts"];
        // Only `title` in the literal — pageNumber omitted (the receiver gets
        // undefined, the optional field allows it).
        await Assert.That(output).Contains("{ title: \"intro\" }");
        await Assert.That(output).DoesNotContain("pageNumber");
    }

    [Test]
    public async Task ExplicitDefaultArg_StillEmitted()
    {
        // When the user explicitly passes the default value, the literal includes
        // it (the user said so).
        var result = TranspileHelper.Transpile(
            """
            [Transpile, PlainObject]
            public record Page(string Title, int PageNumber = 1);

            [Transpile]
            public class Factory
            {
                public Page Make() => new Page("intro", 5);
            }
            """);

        await Assert.That(result["factory.ts"]).Contains("{ title: \"intro\", pageNumber: 5 }");
    }

    [Test]
    public async Task InstanceMethod_LowersToStandaloneHelper()
    {
        // [PlainObject] records can declare methods, which lower to top-level
        // helper functions that take the type as their first parameter.
        var result = TranspileHelper.Transpile(
            """
            [Transpile, PlainObject]
            public record TodoItem(string Title, bool Completed)
            {
                public TodoItem ToggleCompleted() => this with { Completed = !Completed };
            }
            """);

        var output = result["todo-item.ts"];
        // Interface stays plain.
        await Assert.That(output).Contains("export interface TodoItem");
        // Helper function takes self as first param.
        await Assert.That(output).Contains("export function toggleCompleted(self: TodoItem)");
        // Body uses self.completed (the implicit `this.Completed` from C# rewrites
        // to `self.completed`).
        await Assert.That(output).Contains("self.completed");
    }

    [Test]
    public async Task InstanceMethodCallSite_RewritesToHelperCall()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile, PlainObject]
            public record TodoItem(string Title, bool Completed)
            {
                public TodoItem ToggleCompleted() => this with { Completed = !Completed };
            }

            [Transpile]
            public class Service
            {
                public TodoItem Toggle(TodoItem item) => item.ToggleCompleted();
            }
            """);

        var output = result["service.ts"];
        // Call site is rewritten to `toggleCompleted(item)` not `item.toggleCompleted()`
        await Assert.That(output).Contains("toggleCompleted(item)");
        await Assert.That(output).DoesNotContain("item.toggleCompleted");
    }

    [Test]
    public async Task PlainObjectMethod_WithReservedName_StillEscaped()
    {
        // Top-level function declarations can NOT use reserved words even though
        // class methods can. The helper function name escapes; the call site
        // matches via the same path.
        var result = TranspileHelper.Transpile(
            """
            [Transpile, PlainObject]
            public record Bin(int Count)
            {
                public Bin Delete() => this with { Count = 0 };
            }

            [Transpile]
            public class Service
            {
                public Bin Empty(Bin b) => b.Delete();
            }
            """);

        var binOutput = result["bin.ts"];
        var svcOutput = result["service.ts"];
        await Assert.That(binOutput).Contains("export function delete_");
        await Assert.That(svcOutput).Contains("delete_(b)");
    }

    [Test]
    public async Task PlainObjectMethod_WithArgs_PassesAlongsideSelf()
    {
        // Helper function signature: (self, arg1, arg2, ...). Call site passes the
        // receiver as the first argument plus the rest in order.
        var result = TranspileHelper.Transpile(
            """
            [Transpile, PlainObject]
            public record Counter(int Value)
            {
                public Counter Add(int amount) => this with { Value = Value + amount };
            }

            [Transpile]
            public class Service
            {
                public Counter Bump(Counter c, int n) => c.Add(n);
            }
            """);

        await Assert.That(result["counter.ts"]).Contains("export function add(self: Counter, amount: number)");
        await Assert.That(result["service.ts"]).Contains("add(c, n)");
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
