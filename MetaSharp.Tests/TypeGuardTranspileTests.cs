namespace MetaSharp.Tests;

public class TypeGuardTranspileTests
{
    [Test]
    public async Task Record_GeneratesGuardWithTypeofChecks()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile, GenerateGuard]
            public record Point(int X, int Y);
            """
        );

        var output = result["point.ts"];
        await Assert.That(output).Contains("export function isPoint(value: unknown): value is Point");
        await Assert.That(output).Contains("value instanceof Point");
        await Assert.That(output).Contains("typeof v.x === \"number\"");
        await Assert.That(output).Contains("typeof v.y === \"number\"");
    }

    [Test]
    public async Task StringEnum_GeneratesLiteralUnionGuard()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile, StringEnum, GenerateGuard]
            public enum Currency
            {
                [Name("BRL")] Brl,
                [Name("USD")] Usd,
                [Name("EUR")] Eur,
            }
            """
        );

        var output = result["currency.ts"];
        await Assert.That(output).Contains("export function isCurrency(value: unknown): value is Currency");
        await Assert.That(output).Contains("value === \"BRL\"");
        await Assert.That(output).Contains("value === \"USD\"");
        await Assert.That(output).Contains("value === \"EUR\"");
    }

    [Test]
    public async Task NumericEnum_GeneratesValueCheckGuard()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile, GenerateGuard]
            public enum Status { Active, Inactive }
            """
        );

        var output = result["status.ts"];
        await Assert.That(output).Contains("export function isStatus(value: unknown): value is Status");
        await Assert.That(output).Contains("typeof value === \"number\"");
        await Assert.That(output).Contains("value === 0");
        await Assert.That(output).Contains("value === 1");
    }

    [Test]
    public async Task Interface_GeneratesShapeGuardWithoutInstanceof()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile, GenerateGuard]
            public interface IEntity
            {
                string Id { get; }
                string Name { get; }
            }
            """
        );

        var output = result["i-entity.ts"];
        await Assert.That(output).Contains("export function isIEntity(value: unknown): value is IEntity");
        await Assert.That(output).DoesNotContain("instanceof");
        await Assert.That(output).Contains("typeof v.id === \"string\"");
        await Assert.That(output).Contains("typeof v.name === \"string\"");
    }

    [Test]
    public async Task Record_WithTranspiledField_CallsNestedGuard()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App
            {
                [Transpile, StringEnum, GenerateGuard]
                public enum Currency { Brl, Usd }

                [Transpile, GenerateGuard]
                public record Money(int Cents, Currency Currency);
            }
            """
        );

        var moneyOutput = result["money.ts"];
        await Assert.That(moneyOutput).Contains("isCurrency(v.currency)");
        await Assert.That(moneyOutput).Contains("isCurrency");
    }

    [Test]
    public async Task NullableField_AcceptsNullOrInnerType()
    {
        var result = TranspileHelper.Transpile(
            """
            #nullable enable
            [Transpile, GenerateGuard]
            public record Profile(string Name, string? Bio);
            """
        );

        var output = result["profile.ts"];
        await Assert.That(output).Contains("isProfile");
        await Assert.That(output).Contains("v.bio == null");
    }

    [Test]
    public async Task InheritedRecord_GuardChecksAllFields()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App
            {
                [Transpile]
                public record Base(int Id);

                [Transpile, GenerateGuard]
                public record Child(int Id, string Name) : Base(Id);
            }
            """
        );

        var childOutput = result["child.ts"];
        await Assert.That(childOutput).Contains("isChild");
        await Assert.That(childOutput).Contains("typeof v.id === \"number\"");
        await Assert.That(childOutput).Contains("typeof v.name === \"string\"");
    }

    [Test]
    public async Task Exception_NoGuardEvenWithAttribute()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile, GenerateGuard]
            public sealed class MyError(string message) : System.Exception(message);
            """
        );

        var output = result["my-error.ts"];
        await Assert.That(output).DoesNotContain("isMyError");
    }

    [Test]
    public async Task WithoutAttribute_NoGuardGenerated()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public record Item(string Name);
            """
        );

        var output = result["item.ts"];
        await Assert.That(output).DoesNotContain("isItem");
    }

    [Test]
    public async Task Guard_HasTypePredicateReturnType()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile, GenerateGuard]
            public record Item(string Name);
            """
        );

        var output = result["item.ts"];
        await Assert.That(output).Contains("): value is Item");
    }

    [Test]
    public async Task Class_WithPublicField_GuardChecksField()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile, GenerateGuard]
            public class Counter
            {
                public int Count;
            }
            """
        );

        var output = result["counter.ts"];
        await Assert.That(output).Contains("export function isCounter(value: unknown): value is Counter");
        await Assert.That(output).Contains("typeof v.count === \"number\"");
    }

    [Test]
    public async Task InheritedProtectedField_GuardChecksInheritedField()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App
            {
                [Transpile]
                public class Base
                {
                    protected bool _active = true;
                }

                [Transpile, GenerateGuard]
                public class Child : Base
                {
                    public int Count;
                }
            }
            """
        );

        var output = result["child.ts"];
        await Assert.That(output).Contains("typeof v._active === \"boolean\"");
        await Assert.That(output).Contains("typeof v.count === \"number\"");
    }

    [Test]
    public async Task Guard_WithPropertyAndField_ChecksBoth()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile, GenerateGuard]
            public class Sample
            {
                public int Count;
                public string Name { get; set; } = "";
            }
            """
        );

        var output = result["sample.ts"];
        await Assert.That(output).Contains("typeof v.count === \"number\"");
        await Assert.That(output).Contains("typeof v.name === \"string\"");
    }
}
