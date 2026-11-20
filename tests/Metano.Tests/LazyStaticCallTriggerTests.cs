namespace Metano.Tests;

/// <summary>FR-066 / #696: erased static calls still trigger their source owner.</summary>
public class LazyStaticCallTriggerTests
{
    [Test]
    public async Task InlineCall_BindsArgumentsBeforeTriggeringTheOwner()
    {
        var output = TranspileHelper.Transpile(
            """
            [Transpile]
            public class LazyInline
            {
                static LazyInline() { }
                [Inline] public static int Twice(int value) => value + value;
                [Inline(InlineMode.Substitute)] public static int Substitute(int value) => value + value;
            }
            [Transpile]
            public static class Calls
            {
                public static int Next() => 4;
                public static int Run() => LazyInline.Twice(Next());
                public static int RunSubstitute() => LazyInline.Substitute(Next());
            }
            """
        )["calls.ts"];

        await Assert.That(output).Contains("LazyInline.$initialize();");
        await Assert.That(output).Contains("})(Calls.next())");
        await Assert.That(output).DoesNotContain("Calls.next() + Calls.next()");
    }

    [Test]
    public async Task EmitAndMappedCall_BindEvenIgnoredOrRepeatedArguments()
    {
        var output = TranspileHelper.Transpile(
            """
            [assembly: MapMethod(typeof(LazyMapped), "Twice", JsTemplate = "($0 + $0)")]
            [assembly: MapMethod(typeof(LazyMapped), "Abs", JsMethod = "Math.abs")]
            [assembly: MapMethod(typeof(LazyMapped), "Filtered", JsTemplate = "$0", WhenArg0StringEquals = "x")]
            [Transpile]
            public class LazyMapped
            {
                static LazyMapped() { }
                public static int Twice(int value) => value + value;
                public static int Abs(int value) => System.Math.Abs(value);
                public static string Filtered(string value) => value;
                [Emit("7")] public static int Ignore(int value) => 7;
            }
            [Transpile]
            public static class Calls
            {
                public static int Next() => 4;
                public static int Run() => LazyMapped.Twice(Next());
                public static int RunEmit() => LazyMapped.Ignore(Next());
                public static int RunMethod() => LazyMapped.Abs(Next());
                public static string RunFiltered() => LazyMapped.Filtered("x");
            }
            """
        )["calls.ts"];

        await Assert.That(output).Contains("LazyMapped.$initialize();");
        await Assert.That(output).Contains("$arg0 + $arg0");
        await Assert.That(output).Contains("Math.abs($arg0)");
        await Assert.That(output).DoesNotContain("LazyMapped.Math");
        await Assert.That(output).Contains("})(Calls.next())");
    }

    [Test]
    public async Task RecordNullComparisons_TriggerTheBoundOperatorOwner()
    {
        var output = TranspileHelper.Transpile(
            """
            [Transpile]
            public record LazyRecord(int Value) { static LazyRecord() { } }
            [Transpile]
            public static class Calls
            {
                public static bool Equal(LazyRecord? value) => value == null;
                public static bool NotEqual(LazyRecord? value) => value != null;
                public static bool BothNull() => (LazyRecord?)null == null;
            }
            """
        )["calls.ts"];

        await Assert.That(output.Split("LazyRecord.$initialize();").Length).IsEqualTo(4);
        await Assert.That(output).Contains("})(value, null)");
        await Assert.That(output).Contains("})(null, null)");
    }

    [Test]
    public async Task LiftedRecordStructOperator_TriggersOnlyWithBothValuesPresent()
    {
        var output = TranspileHelper.Transpile(
            """
            [Transpile]
            public record struct LazyValue(int Value) { static LazyValue() { } }
            [Transpile]
            public static class Calls
            {
                public static bool Equal(LazyValue? left, LazyValue? right) => left == right;
            }
            """
        )["calls.ts"];

        await Assert.That(output).Contains("if ($arg0 != null && $arg1 != null)");
        await Assert.That(output).Contains("LazyValue.$initialize();");
        await Assert.That(output).Contains("})(left, right)");
    }

    [Test]
    public async Task OrdinaryStaticMethod_KeepsItsOwnGuardWithoutCallSiteGuard()
    {
        var output = TranspileHelper.Transpile(
            """
            [Transpile]
            public class LazyNormal
            {
                static LazyNormal() { }
                public static int Value(int value) => value;
            }
            [Transpile]
            public static class Calls { public static int Run() => LazyNormal.Value(4); }
            """
        )["calls.ts"];

        await Assert.That(output).Contains("LazyNormal.value(4)");
        await Assert.That(output).DoesNotContain("$initialize");
    }

    [Test]
    public async Task ErasedType_DoesNotAcquireAnInitializerBridgeCall()
    {
        var output = TranspileHelper.Transpile(
            """
            [Transpile, PlainObject]
            public record PlainLazy(int Value)
            {
                static PlainLazy() { }
                [Inline] public static int ValueOf(int value) => value;
            }
            [Transpile]
            public static class Calls { public static int Run() => PlainLazy.ValueOf(4); }
            """
        )["calls.ts"];

        await Assert.That(output).DoesNotContain("$initialize");
    }
}
