namespace MetaSharp.Tests;

public class EnumExtensionTests
{
    [Test]
    public async Task EnumExtensionMethod_GeneratesFunction()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile, StringEnum]
            public enum Priority { Low, Medium, High, Urgent }

            [Transpile]
            public static class PriorityExtensions
            {
                public static bool IsElevated(this Priority priority)
                {
                    return priority == Priority.High || priority == Priority.Urgent;
                }
            }
            """
        );

        var output = result["priority-extensions.ts"];
        await Assert.That(output).Contains("export function isElevated(priority: Priority): boolean");
        await Assert.That(output).Contains("Priority.High");
        await Assert.That(output).Contains("Priority.Urgent");
    }

    [Test]
    public async Task NumericEnumExtensionMethod_GeneratesFunction()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public enum Level { Info = 0, Warning = 1, Error = 2 }

            [Transpile]
            public static class LevelExtensions
            {
                public static bool IsCritical(this Level level)
                {
                    return level == Level.Error;
                }

                public static string Label(this Level level)
                {
                    return level switch
                    {
                        Level.Info => "INFO",
                        Level.Warning => "WARN",
                        Level.Error => "ERROR",
                        _ => "UNKNOWN"
                    };
                }
            }
            """
        );

        var output = result["level-extensions.ts"];
        await Assert.That(output).Contains("export function isCritical(level: Level): boolean");
        await Assert.That(output).Contains("export function label(level: Level): string");
    }
}
