namespace Metano.Tests;

/// <summary>
/// Pins the import of a nested record case's owner when another module uses the
/// case in a value position (#726). The case is reached through its owner's static
/// getter (<c>ShotSource.Fresh</c>), so the owner must be imported as a value: a
/// type-only import fails with TS1361 under <c>tsc</c> and throws
/// <c>ReferenceError</c> at run time.
/// </summary>
public class NestedCaseOwnerValueImportTests
{
    private const string ClosedOwnerSource = """
        [assembly: TranspileAssembly]

        namespace System.Runtime.CompilerServices
        {
            // The closed-hierarchy marker the .NET 11 BCL ships; the test
            // compilation references the .NET 10 one.
            internal sealed class IsClosedTypeAttribute : Attribute;
        }

        public closed record ShotSource
        {
            public sealed record Fresh : ShotSource;

            public sealed record Reused(int From) : ShotSource;
        }
        """;

    private const string AbstractOwnerSource = """
        [assembly: TranspileAssembly]

        public abstract record NestedSource
        {
            public sealed record Fresh : NestedSource;

            public sealed record Reused(int From) : NestedSource;
        }
        """;

    private const string SwitchArmConsumer = """

        public static class Label
        {
            public static string Of(OWNER source) => source switch
            {
                OWNER.Fresh => "fresh",
                OWNER.Reused reused => $"reused {reused.From}",
                _ => "other",
            };
        }
        """;

    private const string IsPatternConsumer = """

        public static class Probe
        {
            public static bool IsFresh(object value) => value is OWNER.Fresh;
        }
        """;

    [Test]
    [Arguments(ClosedOwnerSource, "ShotSource", "shot-source")]
    [Arguments(AbstractOwnerSource, "NestedSource", "nested-source")]
    public async Task SwitchArm_ImportsOwnerAsValue(string ownerSource, string owner, string file)
    {
        var result = TranspileHelper.Transpile(
            ownerSource + SwitchArmConsumer.Replace("OWNER", owner)
        );

        var output = result["label.ts"];

        await Assert.That(output).Contains($"instanceof {owner}.Fresh");
        await Assert.That(output).Contains($"import {{ {owner} }} from \"./{file}\";");
        await Assert.That(output).DoesNotContain($"import type {{ {owner} }}");
    }

    [Test]
    [Arguments(ClosedOwnerSource, "ShotSource", "shot-source")]
    [Arguments(AbstractOwnerSource, "NestedSource", "nested-source")]
    public async Task IsPattern_ImportsOwnerAsValue(string ownerSource, string owner, string file)
    {
        var result = TranspileHelper.Transpile(
            ownerSource + IsPatternConsumer.Replace("OWNER", owner)
        );

        var output = result["probe.ts"];

        await Assert.That(output).Contains($"instanceof {owner}.Fresh");
        await Assert.That(output).Contains($"import {{ {owner} }} from \"./{file}\";");
        await Assert.That(output).DoesNotContain($"import type {{ {owner} }}");
    }

    [Test]
    public async Task Construction_ImportsOwnerAsValue()
    {
        var result = TranspileHelper.Transpile(
            AbstractOwnerSource
                + """

                public static class Factory
                {
                    public static NestedSource Reuse(int from) => new NestedSource.Reused(from);
                }
                """
        );

        var output = result["factory.ts"];

        await Assert.That(output).Contains("new NestedSource.Reused(from)");
        await Assert.That(output).Contains("import { NestedSource } from \"./nested-source\";");
    }

    [Test]
    public async Task StaticMemberOfCase_ImportsOwnerAsValue()
    {
        var result = TranspileHelper.Transpile(
            """
            [assembly: TranspileAssembly]

            public abstract record Source
            {
                public sealed record Fresh : Source
                {
                    public static readonly Fresh Instance = new();
                }
            }

            public static class Defaults
            {
                public static Source Initial() => Source.Fresh.Instance;
            }
            """
        );

        var output = result["defaults.ts"];

        await Assert.That(output).Contains("Source.Fresh.instance");
        await Assert.That(output).Contains("import { Source } from \"./source\";");
    }
}
