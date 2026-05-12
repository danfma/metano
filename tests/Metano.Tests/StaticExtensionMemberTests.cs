namespace Metano.Tests;

/// <summary>
/// Stage 3 of #156: static extension members. C# 14 <c>extension(R r) { ... }</c>
/// blocks may declare <c>static</c> methods and properties that ignore the
/// receiver entirely. Helpers emit at module level without a receiver
/// parameter; call sites dispatch via <c>Type.Member</c> and lower to the bare
/// helper invocation.
/// </summary>
public class StaticExtensionMemberTests
{
    [Test]
    public async Task StaticExtensionMethod_HelperEmittedWithoutReceiver()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            public class Date
            {
                public int Year { get; set; }
                public int Month { get; set; }
                public int Day { get; set; }
            }

            [Transpile]
            public static class DateExtensions
            {
                extension(Date date)
                {
                    public static Date Create(int year, int month, int day) =>
                        new Date { Year = year, Month = month, Day = day };
                }
            }
            """
        );

        var output = result["date-extensions.ts"];
        await Assert
            .That(output)
            .Contains("export function create(year: number, month: number, day: number): Date");
        await Assert.That(output).DoesNotContain("date: Date");
    }

    [Test]
    public async Task StaticExtensionMethod_LowersToBareCall()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            public class Date
            {
                public int Year { get; set; }
                public int Month { get; set; }
                public int Day { get; set; }
            }

            [Transpile]
            public static class DateExtensions
            {
                extension(Date date)
                {
                    public static Date Create(int year, int month, int day) =>
                        new Date { Year = year, Month = month, Day = day };
                }
            }

            [Transpile]
            public class Caller
            {
                public Date Build() => Date.Create(2024, 1, 1);
            }
            """
        );

        var output = result["caller.ts"];
        await Assert.That(output).Contains("create(2024, 1, 1)");
        await Assert.That(output).DoesNotContain("Date.create");
    }

    [Test]
    public async Task StaticExtensionProperty_HelperEmittedWithoutReceiver()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            public class Date
            {
                public int Year { get; set; }
                public int Month { get; set; }
                public int Day { get; set; }
            }

            [Transpile]
            public static class DateExtensions
            {
                extension(Date date)
                {
                    public static Date Zero =>
                        new Date { Year = 0, Month = 0, Day = 0 };
                }
            }
            """
        );

        var output = result["date-extensions.ts"];
        await Assert.That(output).Contains("export function zero$get(): Date");
        await Assert.That(output).DoesNotContain("date: Date");
    }

    [Test]
    public async Task StaticExtensionProperty_Read_LowersToGetCall()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            public class Date
            {
                public int Year { get; set; }
                public int Month { get; set; }
                public int Day { get; set; }
            }

            [Transpile]
            public static class DateExtensions
            {
                extension(Date date)
                {
                    public static Date Zero =>
                        new Date { Year = 0, Month = 0, Day = 0 };
                }
            }

            [Transpile]
            public class Caller
            {
                public Date Run() => Date.Zero;
            }
            """
        );

        var output = result["caller.ts"];
        await Assert.That(output).Contains("zero$get()");
        await Assert.That(output).DoesNotContain("Date.zero");
    }

    [Test]
    public async Task StaticExtensionProperty_Write_LowersToSetCall()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            public class Counter
            {
                public int Value { get; set; }
            }

            [Transpile]
            public static class CounterExtensions
            {
                private static int _default;

                extension(Counter counter)
                {
                    public static int Default
                    {
                        get => _default;
                        set => _default = value;
                    }
                }
            }

            [Transpile]
            public class Caller
            {
                public void Run() { Counter.Default = 5; }
            }
            """
        );

        var output = result["caller.ts"];
        await Assert.That(output).Contains("default$set(5)");
        await Assert.That(output).DoesNotContain("Counter.default");
    }

    [Test]
    public async Task StaticExtensionProperty_Setter_HelperEmittedWithoutReceiver()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            public class Counter
            {
                public int Value { get; set; }
            }

            [Transpile]
            public static class CounterExtensions
            {
                private static int _default;

                extension(Counter counter)
                {
                    public static int Default
                    {
                        get => _default;
                        set => _default = value;
                    }
                }
            }
            """
        );

        var output = result["counter-extensions.ts"];
        await Assert.That(output).Contains("export function default$get(): number");
        await Assert.That(output).Contains("export function default$set(value: number)");
        await Assert.That(output).DoesNotContain("counter: Counter");
    }

    [Test]
    public async Task StaticExtensionProperty_CompoundAssign_LowersWithoutLetBinding()
    {
        // Static setters have no receiver to capture, so the compound form
        // emits as a flat `prop$set(prop$get() ± n)` — no IIFE / let needed.
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            public class Counter { public int Value { get; set; } }

            [Transpile]
            public static class CounterExtensions
            {
                private static int _default;
                extension(Counter counter)
                {
                    public static int Default
                    {
                        get => _default;
                        set => _default = value;
                    }
                }
            }

            [Transpile]
            public class Caller
            {
                public void Run() { Counter.Default += 1; }
            }
            """
        );

        var output = result["caller.ts"];
        await Assert.That(output).Contains("default$set(default$get() + 1)");
        await Assert.That(output).DoesNotContain("receiver$temp$");
    }

    [Test]
    public async Task StaticExtensionMethod_BodyDispatchesAtModuleLevel()
    {
        // The static factory body — including a `new Date { … }` literal —
        // must lower cleanly without any receiver substitution.
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            public class Date
            {
                public int Year { get; set; }
                public int Month { get; set; }
                public int Day { get; set; }
            }

            [Transpile]
            public static class DateExtensions
            {
                extension(Date date)
                {
                    public static Date Create(int year, int month, int day) =>
                        new Date { Year = year, Month = month, Day = day };
                }
            }
            """
        );

        var output = result["date-extensions.ts"];
        await Assert.That(output).Contains("new Date(");
    }

    [Test]
    public async Task StaticExtensionMethod_CrossFileImport()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            public class Date
            {
                public int Year { get; set; }
                public int Month { get; set; }
                public int Day { get; set; }
            }

            [Transpile]
            public static class DateExtensions
            {
                extension(Date date)
                {
                    public static Date Create(int year, int month, int day) =>
                        new Date { Year = year, Month = month, Day = day };
                }
            }

            [Transpile]
            public class Caller
            {
                public Date Build() => Date.Create(2024, 1, 1);
            }
            """
        );

        var caller = result["caller.ts"];
        await Assert.That(caller).Contains("create(2024, 1, 1)");
        await Assert.That(caller).Contains("import { create } from \"./date-extensions\";");
    }
}
