namespace Metano.Tests;

public class LazyStructTriggerTests
{
    [Test]
    public async Task DispatchedDefaultConstruction_WritesBackingStorageWithoutTriggeringAccessor()
    {
        var output = TranspileHelper.Transpile(
            """
            [Transpile]
            public struct LazyDispatched
            {
                public int Value { get; set; } = 7;
                static LazyDispatched() { }
                public LazyDispatched(int value) { Value = value; }
                public LazyDispatched(string value) { Value = value.Length; }
            }
            """
        )["lazy-dispatched.ts"];

        await Assert.That(output).DoesNotContain("this.value = 0;");

        await Assert
            .That(output)
            .Contains("if (args.length === 0) {\n      this.value$LazyDispatched_");

        await Assert.That(output).Contains("get value(): number {\n    LazyDispatched.#$init();");
    }

    [Test]
    public async Task AutoProperties_UseGuardedAccessorsAndDeferredBackingInitializers()
    {
        var output = TranspileHelper.Transpile(
            """
            [Transpile]
            public struct LazyValue
            {
                public int Value { get; set; } = 7;
                public int ReadOnly { get; } = 8;
                public int Field;
                static LazyValue() { }
                public LazyValue(int value) { Value = value; ReadOnly = value; }
            }
            """
        )["lazy-value.ts"];

        await Assert.That(output).Contains("get value(): number {\n    LazyValue.#$init();");
        await Assert.That(output).Contains("set value(value: number) {\n    LazyValue.#$init();");
        await Assert.That(output).Contains("get readOnly(): number {\n    LazyValue.#$init();");
        await Assert.That(output).DoesNotContain("set readOnly(");
        await Assert.That(output).Contains("this.field = 0;");
        await Assert.That(output).Contains("value$LazyValue_");
        await Assert.That(output).Contains("readOnly$LazyValue_");
        await Assert.That(output).Contains("toJSON(): unknown");
    }

    [Test]
    public async Task PositionalRecordStruct_DoesNotShadowGuardedAccessorsWithParameterProperties()
    {
        var output = TranspileHelper.Transpile(
            """
            [Transpile]
            public readonly record struct LazyRecord(int Value)
            {
                static LazyRecord() { }
            }
            """
        )["lazy-record.ts"];

        await Assert.That(output).Contains("constructor(value: number)");
        await Assert.That(output).DoesNotContain("constructor(readonly value:");
        await Assert.That(output).Contains("get value(): number {\n    LazyRecord.#$init();");

        await Assert
            .That(output)
            .Contains("equals(other: any): boolean {\n    LazyRecord.#$init();");

        await Assert.That(output).Contains("hashCode(): number {\n    LazyRecord.#$init();");
    }

    [Test]
    public async Task SynthesizedEqualsOfEmptyRecordStruct_RunsGuardWithoutReadingAnyProperty()
    {
        var output = TranspileHelper.Transpile(
            """
            [Transpile]
            public record struct LazyEmpty
            {
                static LazyEmpty() { }
            }
            """
        )["lazy-empty.ts"];

        await Assert
            .That(output)
            .Contains("equals(other: any): boolean {\n    LazyEmpty.#$init();");

        await Assert.That(output).DoesNotContain("constructor(");
    }

    [Test]
    public async Task PlainStructEquality_ReadsBackingFieldsNotGuardedAccessors()
    {
        var output = TranspileHelper.Transpile(
            """
            [Transpile]
            public struct PlainAutoProp
            {
                public int P { get; set; }
                static PlainAutoProp() { }
            }
            """
        )["plain-auto-prop.ts"];

        await Assert.That(output).Contains("this.p$PlainAutoProp_");
        await Assert.That(output).DoesNotContain("this.p ===");
        await Assert.That(output).DoesNotContain("hc.add(this.p)");
    }
}
